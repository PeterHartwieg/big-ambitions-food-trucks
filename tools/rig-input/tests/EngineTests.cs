using AgentRigInput;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class FakeHost : IMouseHost
{
    public long Frame { get; set; } = 100;
    public double Now { get; set; }
    public int ScreenWidth => 1920;
    public int ScreenHeight => 1080;
    public int Activations, Restores, ForceReleases, AfterReleases;
    public bool FailActivation;
    public long NativeReleaseFrame { get; set; } = -1;
    // Simulates the Schedule handle: its native release runs in the frame the virtual button goes up.
    public bool NativeHandle;
    public MouseEngine Engine;
    public List<long> AfterReleaseFrames = new List<long>();

    public void Activate()
    {
        Activations++;
        if (FailActivation) throw new MouseError("rig_mouse_unavailable", "no EventSystem in the scene");
    }
    public void Restore() => Restores++;
    public void ForceRelease() => ForceReleases++;
    public JObject ObserveFrame()
    {
        if (NativeHandle && Engine.Timeline.State.IsUp(0) && Engine.Timeline.State.Frame == Frame) NativeReleaseFrame = Frame;
        return new JObject { ["reads"] = 1 };
    }
    public JToken RunAfterRelease() { AfterReleases++; AfterReleaseFrames.Add(Frame); return new JObject { ["closed"] = "phone" }; }

    // One Unity frame: early hook, consumers (nothing here), late hook.
    public void Tick(double seconds = 1.0 / 30)
    {
        Engine.EarlyFrame();
        Engine.LateFrame();
        Frame++;
        Now += seconds;
    }
}

public sealed class EngineTests
{
    readonly FakeHost host = new FakeHost();
    readonly MouseEngine engine;
    int reservations, ended;
    const string A = "owner-a", B = "owner-b", S = "session-1";

    public EngineTests()
    {
        engine = new MouseEngine(host);
        host.Engine = engine;
    }

    Func<Action> Reserve => () => { reservations++; return () => ended++; };

    MouseCommand Drag(bool after = false, int settle = 2, bool? observed = null, string owner = A)
    {
        var c = engine.Begin("drag", "drag", S, owner, (fresh, at) => Gestures.Drag(new ScreenPoint(900, 500), new ScreenPoint(940, 500), 0, 12, 0, settle, fresh),
            true, after, observed, Reserve, 20);
        c.Start = new ScreenPoint(900, 500);
        c.End = new ScreenPoint(940, 500);
        return c;
    }

    JObject Run(MouseCommand c, int limit = 200)
    {
        for (var i = 0; i < limit; i++)
        {
            var reply = engine.Poll(c);
            if (reply != null) return reply;
            host.Tick();
        }
        throw new Exception("command never completed");
    }

    [Fact]
    public void SelfContainedDragRestoresInputAndEndsItsReservationOnce()
    {
        host.NativeHandle = true;
        var reply = Run(Drag(observed: true));
        Assert.False((bool)reply["active"]);
        Assert.Equal(new[] { false, false, false }, reply["buttons"].ToObject<bool[]>());
        Assert.True((long)reply["down_frame"] < (long)reply["last_move_frame"]);
        Assert.True((long)reply["last_move_frame"] < (long)reply["up_frame"]);
        Assert.Equal((long)reply["up_frame"], (long)reply["native_release_frame"]);
        Assert.True((bool)reply["native_release_observed"]);
        Assert.Equal((long)reply["up_frame"] + 2, (long)reply["completed_frame"]);
        Assert.Equal(1 + 1 + 1 + 12 + 1 + 1 + 2, (int)reply["frames_applied"]);
        Assert.Equal(940, (double)reply["end"]["x"]);
        Assert.Equal((1, 1, 1, 1), (host.Activations, host.Restores, reservations, ended));
        Assert.Null(engine.Current);
        var trace = engine.Traces.Find((string)reply["trace_id"], S).Json();
        Assert.False((bool)trace["trace_truncated"]);
        Assert.Contains(trace["trace"], e => (string)e["phase"] == "up");
    }

    [Fact]
    public void GenericTargetsReportNoReleaseObserverRatherThanSuccess()
    {
        var reply = Run(Drag());
        Assert.Equal(JTokenType.Null, reply["native_release_observed"].Type);
        Assert.Equal(JTokenType.Null, reply["native_release_frame"].Type);
    }

    [Fact]
    public void AfterReleaseEscapeRunsInTheNativeReleaseFrame()
    {
        host.NativeHandle = true;
        var reply = Run(Drag(after: true, settle: 0, observed: true));
        Assert.Equal(1, host.AfterReleases);
        Assert.Equal((long)reply["native_release_frame"], host.AfterReleaseFrames[0]);
        Assert.True((bool)reply["after_release"]["same_frame"]);
        Assert.Equal("phone", (string)reply["after_release"]["result"]["closed"]);
        Assert.Equal((long)reply["up_frame"], (long)reply["completed_frame"]);
    }

    [Fact]
    public void AfterReleaseWithoutANativeReleaseFailsAndDoesNotEscape()
    {
        var c = Drag(after: true, settle: 0, observed: true);
        var error = Assert.Throws<MouseError>(() => Run(c));
        Assert.Equal("mouse_no_native_release", error.Code);
        Assert.Equal(0, host.AfterReleases);
        Assert.Null(engine.Current);
        Assert.Equal(1, ended);
    }

    MouseCommand Down(string owner = A) => engine.Begin("down", "press", S, owner, (fresh, at) =>
    {
        var list = Gestures.Prepare(new ScreenPoint(10, 10), 0, fresh);
        list.AddRange(Gestures.Down(new ScreenPoint(10, 10), engine.Timeline.State.Held, 0));
        return list;
    }, false, false, null, Reserve, 10);

    MouseCommand Up(string owner = A) => engine.Begin("up", "press", S, owner, (fresh, at) => Gestures.Up(at, engine.Timeline.State.Held, 0, 2), false, false, null, Reserve, 10);

    [Fact]
    public void IncrementalDownKeepsOneReservationUntilUpOnALaterFrame()
    {
        var down = Run(Down());
        Assert.True((bool)down["active"]);
        Assert.True(down["buttons"][0].Value<bool>());
        Assert.Equal((1, 0), (reservations, ended));
        for (var i = 0; i < 10; i++) host.Tick();
        Assert.True(engine.Timeline.State.IsHeld(0));
        // Unrelated gestures and other owners are refused while the button is held.
        Assert.Equal("mouse_busy", Assert.Throws<MouseError>(() => Drag()).Code);
        Assert.Equal("mouse_busy", Assert.Throws<MouseError>(() => Up(B)).Code);
        Assert.Equal("mouse_busy", Assert.Throws<MouseError>(() => engine.Release(B, S)).Code);
        Assert.NotNull(engine.Current);
        var up = Run(Up());
        Assert.True((long)up["up_frame"] > (long)down["down_frame"] + 10);
        Assert.False((bool)up["active"]);
        Assert.Equal((1, 1, 1), (reservations, ended, host.Restores));
    }

    [Fact]
    public void IdleHoldWatchdogReleasesWithAFrameAndRestores()
    {
        Run(Down());
        host.Now += MouseArgs.HoldTimeoutSeconds + 1;
        for (var i = 0; i < 5 && engine.Current != null; i++) host.Tick();
        Assert.Null(engine.Current);
        Assert.StartsWith("mouse_timeout", engine.LastError);
        Assert.Equal((1, 0, 1), (host.Restores, host.ForceReleases, ended));
        Assert.False(engine.Timeline.State.IsHeld(0));
    }

    [Fact]
    public void GestureWatchdogFailsTheCommandAsTimeoutNotSuccess()
    {
        var c = engine.Begin("drag", "drag", S, A, (fresh, at) => Gestures.Drag(new ScreenPoint(0, 0), new ScreenPoint(5, 0), 0, 2, 9, 0, fresh), true, false, null, Reserve, 1);
        host.Tick();
        host.Now += 5;
        var error = Assert.Throws<MouseError>(() => Run(c));
        Assert.Equal("mouse_timeout", error.Code);
        for (var i = 0; i < 5; i++) host.Tick();
        Assert.Null(engine.Current);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void ExplicitReleaseIsIdempotentAndCancelsTheRunningCommand()
    {
        Assert.Null(engine.Release(A, S));
        var drag = engine.Begin("drag", "drag", S, A, (fresh, at) => Gestures.Drag(new ScreenPoint(0, 0), new ScreenPoint(5, 0), 0, 2, 5, 0, fresh), true, false, null, Reserve, 30);
        host.Tick(); host.Tick(); host.Tick();
        var release = engine.Release(A, S);
        Assert.Equal("mouse_cancelled", Assert.Throws<MouseError>(() => engine.Poll(drag)).Code);
        var reply = Run(release);
        Assert.True((bool)reply["released"]);
        Assert.False((bool)reply["active"]);
        Assert.Null(engine.Release(A, S));
        Assert.Equal((1, 1, 0), (host.Restores, ended, host.ForceReleases));
    }

    [Fact]
    public void SecondReleaseWhileDrainingWaitsForTheSameDrain()
    {
        Run(Down());
        var first = engine.Release(A, S);
        var second = engine.Release(A, S);
        Assert.Same(first, second);
        Assert.True((bool)Run(second)["released"]);
        Assert.NotNull(engine.Poll(first));
        Assert.Equal((1, 1), (host.Restores, ended));
    }

    [Fact]
    public void ImmediateCancelIsIdempotent()
    {
        Run(Down());
        engine.Cancel("the bridge stopped", "mouse_cancelled", false);
        engine.Cancel("the bridge stopped", "mouse_cancelled", false);
        engine.ResetSession("again");
        Assert.Equal((1, 1, 1), (host.Restores, host.ForceReleases, ended));
        Assert.Null(engine.Current);
        Assert.False(engine.Timeline.State.IsHeld(0));
    }

    [Fact]
    public void ActivationFailureLeavesNothingHeldOrReserved()
    {
        host.FailActivation = true;
        Assert.Equal("rig_mouse_unavailable", Assert.Throws<MouseError>(() => Drag()).Code);
        Assert.Null(engine.Current);
        Assert.Equal((1, 1, 1), (reservations, ended, host.Restores));
        Assert.Equal(0, engine.Timeline.Pending);
    }

    [Fact]
    public void BusyRefusalBeforeOwningDoesNotDisturbTheOwner()
    {
        var c = Drag();
        host.Tick();
        Assert.Equal("mouse_busy", Assert.Throws<MouseError>(() => Drag(owner: B)).Code);
        Assert.Equal("mouse_busy", Assert.Throws<MouseError>(() => Drag()).Code);
        Assert.False((bool)Run(c)["active"]);
        Assert.Equal(1, reservations);
    }

    [Fact]
    public void HoverInteractionPersistsUntilReleased()
    {
        var move = engine.Begin("move", "hover", S, A, (fresh, at) => Gestures.Prepare(new ScreenPoint(3, 4), 0, fresh), false, false, null, Reserve, 5);
        Assert.True((bool)Run(move)["active"]);
        var again = engine.Begin("move", "hover", S, A, (fresh, at) => Gestures.Move(at, new ScreenPoint(13, 4), 0, 5), false, false, null, Reserve, 5);
        var reply = Run(again);
        Assert.Equal(5, (int)reply["frames_applied"]);
        Assert.Equal(13, (double)reply["position"]["x"]);
        Assert.Equal(1, reservations);
        Run(engine.Release(A, S));
        Assert.Equal(1, ended);
    }

    [Fact]
    public void TracesBelongToTheirSession()
    {
        var reply = Run(Drag());
        var id = (string)reply["trace_id"];
        Assert.Equal("mouse_unknown_trace", Assert.Throws<MouseError>(() => engine.Traces.Find(id, "other")).Code);
        Assert.Equal("mouse_unknown_trace", Assert.Throws<MouseError>(() => engine.Traces.Find("nope", S)).Code);
        engine.ResetSession("stop");
        Assert.Throws<MouseError>(() => engine.Traces.Find(id, S));
    }

    [Fact]
    public void TraceIsBounded()
    {
        var t = new MouseTrace("x", S);
        for (var i = 0; i < MouseTrace.MaxEntries + 5; i++) t.Add(new JObject());
        Assert.True((bool)t.Json()["trace_truncated"]);
        Assert.Equal(MouseTrace.MaxEntries, ((JArray)t.Json()["trace"]).Count);
    }

    [Fact]
    public void ReleaseDrainThatNeverCompletesIsForced()
    {
        Run(Down());
        engine.Cancel("owner lost", "mouse_cancelled", true);
        host.Now += MouseEngine.ReleaseDrainSeconds + 1;
        engine.Timeline.Cancel(); // the drain samples never run (no frames reached the timeline)
        engine.EarlyFrame();
        Assert.Null(engine.Current);
        Assert.Equal((1, 1), (host.ForceReleases, ended));
    }
}
