using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BridgeControl;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class BridgeLockTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "bridge-lock-tests-" + Guid.NewGuid());
    const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    LeaseStore Store(string name = "normal") => new LeaseStore(Path.Combine(root, name), name);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    [Fact] public void SameLabelCannotStealAndOwnershipSurvivesReopen()
    {
        var s = Store(); s.Acquire("owner", "test", A);
        Assert.Throws<InvalidOperationException>(() => Store().Acquire("owner", "test", B));
        Assert.Throws<InvalidOperationException>(() => Store().Release(B));
        Assert.DoesNotContain(A, File.ReadAllText(Path.Combine(root, "normal", "lease.json")));
        Store().AssertOwner(A); Store().Release(A); Assert.False((bool)Store().Status()["held"]!);
    }
    [Fact] public async Task ConcurrentAcquisitionHasOnlyOneWinner()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            try { Store().Acquire("owner" + i, "test", i.ToString("D64")); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.Single(results, x => x);
    }
    [Fact] public void InstancesHaveIndependentOwners()
    {
        Store().Acquire("normal", "", A); Store("agent1").Acquire("rig", "", B);
        Assert.Throws<InvalidOperationException>(() => Store("agent1").AssertOwner(A));
        Store().Release(A); Store("agent1").AssertOwner(B);
    }
    [Fact] public void LegacyReservationBlocksNormalOnly()
    {
        Directory.CreateDirectory(root); var legacy = Path.Combine(root, "game-lock.txt"); File.WriteAllText(legacy, "owner: someone");
        var normal = new LeaseStore(Path.Combine(root, "normal"), "normal", legacy);
        Assert.True((bool)normal.Status()["legacy_blocked"]!);
        Assert.Throws<InvalidOperationException>(() => normal.Acquire("owner", "", A));
        Store("agent1").Acquire("rig", "", B); Assert.True(File.Exists(legacy));
    }
    [Fact] public void LifecycleWorkIsExclusiveAndPreventsRelease()
    {
        var s=Store(); s.Acquire("owner", "", A); var command=s.Begin(A,"command");
        Assert.Throws<InvalidOperationException>(()=>s.Release(A));
        Assert.Throws<InvalidOperationException>(()=>s.Begin(A,"lifecycle"));
        s.End(command); var lifecycle=s.Begin(A,"lifecycle"); s.AssertPermit(A,lifecycle);
        Assert.Throws<InvalidOperationException>(()=>s.Begin(A,"command"));
        s.End(lifecycle); Assert.Throws<InvalidOperationException>(()=>s.AssertPermit(A,lifecycle)); s.Release(A);
    }
    [Fact] public async Task TimedOutWaitCannotReleasePendingTask()
    {
        var s=Store(); s.Acquire("owner", "", A); ControlGate.ConfigureStore(s);
        var body=ControlGate.PrepareBody(new JObject { ["cmd"]="time", ["expect_session"]="session", ["lock_token"]=A });
        var args=(JObject)body["args"]!; ControlGate.Intercept("time",args);
        var work=new TaskCompletionSource<JToken>(); _ = ControlGate.Track(work.Task,args); ControlGate.EndUnqueued(args);
        Assert.NotSame(work.Task, await Task.WhenAny(work.Task, Task.Delay(10))); Assert.Throws<InvalidOperationException>(()=>s.Release(A));
        work.SetResult(new JObject()); await work.Task; s.Release(A);
    }
    [Fact] public void BackgroundWorkOutlivesAcknowledgement()
    {
        var s=Store();s.Acquire("owner","",A);ControlGate.ConfigureStore(s);
        var body=ControlGate.PrepareBody(new JObject{["cmd"]="time",["expect_session"]="session",["lock_token"]=A});
        var args=(JObject)body["args"]!;ControlGate.Intercept("time",args);ControlGate.BeforeRun("time",args);
        ControlGate.ObserveWork(true);ControlGate.Track(Task.FromResult<JToken>(new JObject()),args);
        Assert.Throws<InvalidOperationException>(()=>s.Release(A));ControlGate.ObserveWork(false);s.Release(A);
    }
    [Fact] public void ReadOnlyIsOpenButReservedArgumentsCannotAuthorizeWrites()
    {
        ControlGate.ConfigureStore(Store()); Assert.Null(ControlGate.Intercept("state",new JObject()));
        Assert.Throws<InvalidOperationException>(()=>ControlGate.PrepareBody(new JObject{["cmd"]="quit"}));
        var body=ControlGate.PrepareBody(new JObject { ["cmd"]="quit",["expect_session"]="session",["args"]=new JObject{["__bridge_lock_token"]=A,["__bridge_lock_permit"]="fake"} });
        Assert.Null((string?)body["args"]!["__bridge_lock_token"]);
        Assert.Null(body["args"]!["__bridge_lock_permit"]);
        Assert.Throws<InvalidOperationException>(()=>ControlGate.Intercept("quit",(JObject)body["args"]!));
    }
    [Fact] public void UnqueuedReservationCanBeCleanedUp()
    {
        var s=Store();s.Acquire("owner","",A);ControlGate.ConfigureStore(s);
        var body=ControlGate.PrepareBody(new JObject{["cmd"]="time",["expect_session"]="session",["lock_token"]=A});
        var args=(JObject)body["args"]!;ControlGate.Intercept("time",args);
        Assert.Throws<InvalidOperationException>(()=>s.Release(A));ControlGate.EndUnqueued(args);s.Release(A);
    }
    [Fact] public void OnlyOwnerCanReserveForcedTerminationDuringBackgroundWork()
    {
        var s=Store();s.Acquire("owner","",A);var work=s.Begin(A,"background");
        Assert.Throws<InvalidOperationException>(()=>s.Begin(B,"terminate"));
        var stop=s.Begin(A,"terminate");Assert.Throws<InvalidOperationException>(()=>s.Begin(A,"command"));
        s.End(work);s.End(stop);s.Release(A);
    }
    [Fact] public void CorruptStateFailsClosed()
    {
        Store().Acquire("owner","",A);File.WriteAllText(Path.Combine(root,"normal","lease.json"),"{}");
        Assert.Throws<InvalidOperationException>(()=>Store().Acquire("owner","",B));
    }
    [Fact] public void MouseStatusAndUiGeometryReadOnlyButMouseGesturesNeedTheOwner()
    {
        Assert.True(ControlGate.IsReadOnly("mouse", new JObject { ["op"] = "status" }));
        Assert.True(ControlGate.IsReadOnly("ui", new JObject { ["op"] = "geometry" }));
        foreach (var op in new[] { "move", "down", "up", "scroll", "drag", "release" })
            Assert.False(ControlGate.IsReadOnly("mouse", new JObject { ["op"] = op }));
        Assert.False(ControlGate.IsReadOnly("mouse", new JObject()));
        Assert.False(ControlGate.IsReadOnly("mouse", new JObject { ["op"] = true }));
        Assert.False(ControlGate.IsReadOnly("ui", new JObject { ["op"] = "click" }));
        var s = Store(); s.Acquire("owner", "", A); ControlGate.ConfigureStore(s);
        Assert.Throws<InvalidOperationException>(() => ControlGate.BeforeRun("mouse", new JObject { ["op"] = "drag", ["__bridge_lock_token"] = B }));
        ControlGate.BeforeRun("mouse", new JObject { ["op"] = "status" });
        s.Release(A);
    }
    // The rig mouse holds an extra command reservation for a whole interaction through the public lock API
    // (big-ambitions-agent-rig rig-input/Runtime/Bridge.cs Reserve): lease release is refused until the interaction ends.
    [Fact] public void InteractionReservationOutlivesItsCommandAndRefusesOtherOwners()
    {
        var s = Store(); s.Acquire("owner", "", A); ControlGate.ConfigureStore(s);
        var reservation = new JObject { ["__bridge_lock_token"] = A };
        Assert.Null(ControlGate.Intercept("mouse", reservation));
        var interaction = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        ControlGate.Track(interaction.Task, reservation);
        ControlGate.EndUnqueued(reservation);
        ControlGate.ObserveWork(false);
        Assert.Throws<InvalidOperationException>(() => s.Release(A));
        Assert.Throws<InvalidOperationException>(() => ControlGate.Intercept("mouse", new JObject { ["__bridge_lock_token"] = B }));
        interaction.SetResult(null!);
        SpinWait.SpinUntil(() => { try { s.Release(A); return true; } catch (InvalidOperationException) { return false; } }, 2000);
        Assert.False((bool)s.Status()["held"]!);
    }
}
