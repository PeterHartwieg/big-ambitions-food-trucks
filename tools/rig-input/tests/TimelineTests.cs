using AgentRigInput;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class TimelineTests
{
    [Fact]
    public void EdgesLastExactlyTheFrameTheHeldStateChanges()
    {
        var s = new VirtualMouseState();
        s.Apply(new MouseSample(1, 2, 0, "hover"), 1);
        Assert.Equal((0, 0, 0), (s.Held, s.Down, s.Up));
        s.Apply(new MouseSample(1, 2, 1, "down"), 2);
        Assert.True(s.IsHeld(0) && s.IsDown(0) && !s.IsUp(0));
        s.Hold(3);
        Assert.True(s.IsHeld(0) && !s.IsDown(0) && !s.IsUp(0));
        s.Apply(new MouseSample(1, 2, 1 | 2, "down"), 4);
        Assert.True(s.IsDown(1) && !s.IsDown(0) && s.IsHeld(0) && s.IsHeld(1));
        s.Apply(new MouseSample(1, 2, 2, "up"), 5);
        Assert.True(s.IsUp(0) && !s.IsHeld(0) && s.IsHeld(1) && !s.IsUp(1));
        s.Apply(new MouseSample(1, 2, 0, "up"), 6);
        Assert.True(s.IsUp(1) && s.Held == 0);
        Assert.False(s.IsHeld(-1) || s.IsHeld(3) || s.IsDown(7));
    }

    [Fact]
    public void RepeatedDownWithoutReleaseIsNoNewEdge()
    {
        var s = new VirtualMouseState();
        s.Apply(new MouseSample(0, 0, 1, "down"), 1);
        s.Apply(new MouseSample(0, 0, 1, "down"), 2);
        Assert.False(s.IsDown(0));
        Assert.True(s.IsHeld(0));
    }

    [Fact]
    public void ScrollIsAOneFramePulse()
    {
        var t = new MouseTimeline();
        t.Enqueue(Gestures.Scroll(new ScreenPoint(5, 5), 0, 0, -3));
        Assert.True(t.Step(1, 0));
        Assert.Equal(-3, t.State.ScrollY);
        Assert.True(t.Step(2, 0));
        Assert.Equal(0, t.State.ScrollY);
        Assert.False(t.Step(3, 0));
        Assert.Equal(0, t.State.ScrollY);
    }

    [Fact]
    public void OneSamplePerFrameAndZeroDurationStillUsesDistinctFrames()
    {
        var t = new MouseTimeline();
        var samples = Gestures.Drag(new ScreenPoint(10, 10), new ScreenPoint(50, 10), 0, 4, 0, 2, fresh: true);
        t.Enqueue(samples);
        var frames = new List<(long Frame, string Phase, int Held, int Down, int Up)>();
        for (long f = 100; t.Pending > 0; f++)
        {
            Assert.True(t.Step(f, 0));
            Assert.False(t.Step(f, 0)); // a second call in the same frame changes nothing
            frames.Add((f, t.State.Phase, t.State.Held, t.State.Down, t.State.Up));
        }
        Assert.Equal(new[] { "prepare", "hover", "down", "move", "move", "move", "move", "hold", "up", "settle", "settle" }, frames.Select(x => x.Phase));
        Assert.Equal(frames.Count, frames.Select(x => x.Frame).Distinct().Count());
        var down = frames.Single(x => x.Down != 0);
        var up = frames.Single(x => x.Up != 0);
        Assert.Equal("down", down.Phase);
        Assert.Equal("up", up.Phase);
        Assert.True(up.Frame > down.Frame + 4);
        // The endpoint is held for one full frame before the release.
        var hold = frames.Single(x => x.Phase == "hold");
        Assert.Equal(1, hold.Held);
        Assert.Equal(up.Frame - 1, hold.Frame);
        Assert.Equal(50, t.State.X);
    }

    [Fact]
    public void TimedSamplesWaitOnUnscaledTimeWithoutCollapsingFrames()
    {
        var t = new MouseTimeline();
        t.Enqueue(Gestures.Drag(new ScreenPoint(0, 0), new ScreenPoint(100, 0), 0, 2, 1.0, 0, fresh: false));
        double now = 0;
        var applied = new List<(long, string, double)>();
        for (long f = 1; t.Pending > 0 && f < 1000; f++, now += 0.1)
            if (t.Step(f, now)) applied.Add((f, t.State.Phase, now));
            else Assert.Equal(0, t.State.Down | t.State.Up);
        Assert.Equal(new[] { "hover", "down", "move", "move", "hold", "up" }, applied.Select(a => a.Item2));
        // The second move waits for the full duration; the hold and up then follow on the next frames.
        var lastMove = applied[3];
        Assert.True(lastMove.Item3 >= 1.0 - 1e-9);
        Assert.Equal(lastMove.Item1 + 1, applied[4].Item1);
        Assert.Equal(lastMove.Item1 + 2, applied[5].Item1);
    }

    [Fact]
    public void FrozenClockHoldsTheTimelineButNeverEmitsEdges()
    {
        var t = new MouseTimeline();
        t.Enqueue(Gestures.Drag(new ScreenPoint(0, 0), new ScreenPoint(10, 0), 0, 1, 5, 0, fresh: false));
        t.Step(1, 0); t.Step(2, 0);
        Assert.True(t.State.IsHeld(0));
        for (long f = 3; f < 50; f++) Assert.False(t.Step(f, 0.5));
        Assert.True(t.State.IsHeld(0) && t.State.Down == 0);
        Assert.Equal(3, t.Pending);
    }

    [Fact]
    public void BusyTimelineRefusesAnotherBatchAndCancelClears()
    {
        var t = new MouseTimeline();
        t.Enqueue(Gestures.Down(new ScreenPoint(1, 1), 0, 0));
        Assert.Throws<InvalidOperationException>(() => t.Enqueue(Gestures.Down(new ScreenPoint(1, 1), 0, 1)));
        t.Cancel();
        Assert.Equal(0, t.Pending);
        t.Enqueue(Gestures.Down(new ScreenPoint(1, 1), 0, 1));
    }

    [Fact]
    public void MoveInterpolatesAndKeepsHeldButtons()
    {
        var list = Gestures.Move(new ScreenPoint(0, 0), new ScreenPoint(30, 60), 1, 3);
        Assert.Equal(new[] { 10.0, 20.0, 30.0 }, list.Select(s => s.X));
        Assert.Equal(new[] { 20.0, 40.0, 60.0 }, list.Select(s => s.Y));
        Assert.All(list, s => Assert.Equal(1, s.Held));
    }
}

public sealed class ArgsTests
{
    static JObject J(string json) => JObject.Parse(json);

    [Fact]
    public void OpsAndUnsupportedFields()
    {
        Assert.Equal("drag", MouseArgs.Op(J("{\"op\":\"drag\",\"from\":{},\"timeout_s\":5,\"__bridge_lock_token\":\"x\"}")));
        Assert.StartsWith("mouse_invalid", Assert.Throws<MouseError>(() => MouseArgs.Op(J("{\"op\":\"click\"}"))).Message);
        Assert.StartsWith("mouse_invalid", Assert.Throws<MouseError>(() => MouseArgs.Op(J("{\"op\":\"status\",\"to\":{}}"))).Message);
        Assert.Throws<MouseError>(() => MouseArgs.Op(J("{\"op\":1}")));
        Assert.Throws<MouseError>(() => MouseArgs.Op(new JObject()));
    }

    [Fact]
    public void NumbersRejectBooleansFractionsNaNAndBounds()
    {
        Assert.Equal(12, MouseArgs.Int(J("{\"steps\":12.0}"), "steps", 1, 1, 240));
        Assert.Equal(7, MouseArgs.Int(new JObject(), "steps", 7, 1, 240));
        foreach (var bad in new[] { "{\"steps\":true}", "{\"steps\":12.5}", "{\"steps\":0}", "{\"steps\":241}", "{\"steps\":\"3\"}" })
            Assert.Throws<MouseError>(() => MouseArgs.Int(J(bad), "steps", 1, 1, 240));
        Assert.Throws<MouseError>(() => MouseArgs.Number(new JObject { ["d"] = double.NaN }, "d", 0, 0, 10));
        Assert.Throws<MouseError>(() => MouseArgs.Number(new JObject { ["d"] = double.PositiveInfinity }, "d", 0, 0, 10));
        Assert.Throws<MouseError>(() => MouseArgs.Number(J("{\"d\":false}"), "d", 0, 0, 10));
        Assert.Equal(0.4, MouseArgs.Number(new JObject(), "d", 0.4, 0, 10));
        Assert.Throws<MouseError>(() => MouseArgs.Button(J("{\"button\":3}")));
        Assert.Throws<MouseError>(() => MouseArgs.Button(J("{\"button\":true}")));
    }

    [Fact]
    public void PointsAreBottomLeftPixelsInsideTheScreenOrTargets()
    {
        var p = MouseArgs.Point(J("{\"at\":{\"x\":0,\"y\":1079.5}}"), "at", true, 1920, 1080);
        Assert.Equal((0.0, 1079.5), (p.Screen.Value.X, p.Screen.Value.Y));
        foreach (var bad in new[] { "{\"at\":{\"x\":1920,\"y\":0}}", "{\"at\":{\"x\":-1,\"y\":0}}", "{\"at\":{\"x\":1}}", "{\"at\":{\"x\":1,\"y\":2,\"z\":3}}",
                                    "{\"at\":[1,2]}", "{\"at\":{\"x\":true,\"y\":2}}", "{\"at\":{\"target\":\"\"}}", "{\"at\":{\"target\":\"t\",\"uv\":{\"x\":1.5,\"y\":0}}}",
                                    "{\"at\":{\"target\":\"t\",\"extra\":1}}" })
            Assert.Throws<MouseError>(() => MouseArgs.Point(J(bad), "at", true, 1920, 1080));
        var t = MouseArgs.Point(J("{\"at\":{\"target\":\"ui3.4\",\"uv\":{\"x\":1,\"y\":0}}}"), "at", true, 1920, 1080);
        Assert.Equal(("ui3.4", 1.0, 0.0), (t.Target, t.U, t.V));
        Assert.Equal(0.5, MouseArgs.Point(J("{\"at\":{\"target\":\"ui\"}}"), "at", true, 1, 1).U);
        Assert.Null(MouseArgs.Point(new JObject(), "at", false, 1, 1));
        Assert.Throws<MouseError>(() => MouseArgs.Point(new JObject(), "at", true, 1, 1));
    }

    [Fact]
    public void AfterReleaseIsOnlyTheNarrowEscape()
    {
        Assert.False(MouseArgs.AfterReleaseEscape(new JObject()));
        Assert.True(MouseArgs.AfterReleaseEscape(J("{\"after_release\":{\"op\":\"ui_escape\",\"phase\":\"after_native_release\"}}")));
        foreach (var bad in new[] { "{\"after_release\":{\"op\":\"ui_click\",\"phase\":\"after_native_release\"}}",
                                    "{\"after_release\":{\"op\":\"ui_escape\"}}", "{\"after_release\":\"ui_escape\"}",
                                    "{\"after_release\":{\"op\":\"ui_escape\",\"phase\":\"after_native_release\",\"cmd\":\"quit\"}}" })
            Assert.Throws<MouseError>(() => MouseArgs.AfterReleaseEscape(J(bad)));
    }
}
