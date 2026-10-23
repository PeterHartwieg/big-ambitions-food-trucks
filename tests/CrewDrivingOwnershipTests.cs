using System;
using FoodTrucks.Core.CrewDriving;
using Xunit;

namespace FoodTrucks.Core.Tests;

public class CrewDrivingOwnershipTests
{
    [Fact]
    public void SameSessionRejectsDuplicateVehicleEvenWithReplacementController()
    {
        var owners = new ProbeOwnership(Guid.NewGuid());
        Assert.True(owners.TryAcquire("truck", 11, out var first));
        Assert.False(owners.TryAcquire("truck", 11, out _));
        Assert.False(owners.TryAcquire("truck", 12, out _));
        Assert.True(owners.TryAcquire("other", 12, out var second));
        Assert.Equal(2, owners.Count);
        Assert.True(owners.Owns(first));
        Assert.True(owners.Owns(second));
    }

    [Fact]
    public void IndependentControllersAndSessionsDoNotShareProgressOrRelease()
    {
        var owners = new ProbeOwnership(Guid.NewGuid());
        var a = new CrewDrivingTests.Rig(owners, "a");
        var b = new CrewDrivingTests.Rig(owners, "b");
        for (var i = 0; i < 100; i++) a.Tick();
        Assert.True(a.Controller.RearAlong > 1);
        Assert.Equal(0, b.Controller.RearAlong);
        Assert.Equal(ProbeState.Starting, b.Controller.State);
        a.Controller.Cancel(a.Token);
        Assert.Equal(ProbeState.Starting, b.Controller.State);
        Assert.True(owners.Release(a.Token));
        Assert.True(owners.Owns(b.Token));
        var nextSession = new ProbeOwnership(Guid.NewGuid());
        Assert.True(nextSession.TryAcquire("b", b.Token.ControllerInstance, out var next));
        Assert.False(nextSession.Release(b.Token));
        Assert.True(nextSession.Owns(next));
        Assert.True(owners.Owns(b.Token));
    }

    [Fact]
    public void TwoMovingControllersCanBeSteppedInterleavedAndCancelledIndependently()
    {
        var owners = new ProbeOwnership(Guid.NewGuid());
        var straight = new CrewDrivingTests.Rig(owners, "straight");
        var turning = new CrewDrivingTests.Rig(owners, "turning", CrewDrivingTests.Arc(20));
        for (var i = 0; i < 300; i++) { straight.Tick(); turning.Tick(); }
        Assert.True(straight.Controller.RearAlong > turning.Controller.RearAlong);
        straight.Controller.Cancel(straight.Token);
        Assert.True(owners.Release(straight.Token));
        for (var i = 0; i < 20000 && turning.Controller.State != ProbeState.Finished; i++)
        {
            Assert.False(straight.Controller.Cancel(straight.Token).MayApply);
            Assert.NotEqual(ProbeState.Cancelled, turning.Tick().State);
        }
        Assert.Equal(ProbeState.Finished, turning.Controller.State);
        Assert.Equal(1, owners.Count);
        Assert.True(owners.Owns(turning.Token));
    }

    [Fact]
    public void ReacquisitionCannotBeReleasedOrCancelledByOldGeneration()
    {
        var owners = new ProbeOwnership(Guid.NewGuid());
        var old = new CrewDrivingTests.Rig(owners, "truck");
        old.Tick();
        Assert.Equal(ProbeState.Cancelled, old.Controller.Cancel(old.Token).State);
        Assert.Equal(1, owners.Count); // native cleanup still owns the body
        Assert.True(owners.Release(old.Token));
        Assert.False(owners.Release(old.Token));
        var replacement = new CrewDrivingTests.Rig(owners, "truck");
        Assert.True(replacement.Token.Generation > old.Token.Generation);
        Assert.False(owners.Release(old.Token));
        Assert.False(old.Controller.Cancel(old.Token).MayApply);
        Assert.False(old.Controller.Step(old.Token, new ProbeObservation(0, 0, 0, 0, .02)).MayApply);
        var stale = replacement.Controller.Cancel(old.Token);
        Assert.False(stale.MayApply);
        Assert.Equal(ProbeState.Starting, replacement.Controller.State);
        Assert.True(owners.Owns(replacement.Token));
        Assert.True(replacement.Tick().Throttle > 0);
    }

    [Fact]
    public void TokensFromAnotherRegistryCannotBeUsedEvenWithSameSessionIdentity()
    {
        var session = Guid.NewGuid();
        var a = new ProbeOwnership(session); var b = new ProbeOwnership(session);
        Assert.True(a.TryAcquire("truck", 1, out var first));
        Assert.True(b.TryAcquire("truck", 1, out var second));
        Assert.False(a.Owns(second));
        Assert.False(b.Release(first));
        Assert.Throws<ArgumentException>(() => new ProbeController(b, first, CrewDrivingTests.Straight(), CrewDrivingTests.Truck()));
        Assert.False(a.Owns(default));
    }

    [Fact]
    public void OneGenerationCannotConstructTwoPureControllers()
    {
        var rig = new CrewDrivingTests.Rig();
        Assert.Throws<ArgumentException>(() => new ProbeController(rig.Owners, rig.Token, rig.Path, rig.Geometry));
        Assert.True(rig.Tick().MayApply);
    }

    [Fact]
    public void StartupAndCancelAreIdempotentAndNeverReleaseBeforeCleanup()
    {
        var rig = new CrewDrivingTests.Rig();
        for (var i = 0; i < 50; i++)
        {
            var command = rig.Controller.Step(rig.Token, new ProbeObservation(0, 0, 0, 0, .02, nativeReady: false));
            Assert.Equal(ProbeState.Starting, command.State);
            Assert.Equal(0, command.Throttle);
            Assert.Equal(1, command.Handbrake);
        }
        var first = rig.Controller.Cancel(rig.Token);
        var second = rig.Controller.Cancel(rig.Token);
        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Reason, second.Reason);
        Assert.True(second.RequiresStationaryHold);
        Assert.Equal(1, rig.Owners.Count);
        Assert.Equal(0, rig.Tick().Throttle);
        Assert.True(rig.Owners.Release(rig.Token));
    }

    [Fact]
    public void InvalidOwnershipInputsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new ProbeOwnership(Guid.Empty));
        var owners = new ProbeOwnership(Guid.NewGuid());
        Assert.False(owners.TryAcquire("", 1, out _));
        Assert.False(owners.TryAcquire("truck", 0, out _));
        Assert.Equal(0, owners.Count);
    }
}
