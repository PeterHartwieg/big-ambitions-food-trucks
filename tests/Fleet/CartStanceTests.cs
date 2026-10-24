using System;
using System.IO;
using System.Text.RegularExpressions;
using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

// The worker or player at a cart stands behind it at the equipment midpoint, facing the customers,
// at every heading and wherever the cart stands (bug of 24 October 2026: they stood diagonally off to one side).
public class CartStanceTests
{
    [Fact]
    public void EveryEquipmentSpecRecordsTheImportedFrontAndOperatorAxis()
    {
        var root = RepoRoot();
        var specs = Directory.GetFiles(Path.Combine(root, "art/equipment"), "spec.json", SearchOption.AllDirectories);
        Assert.Equal(18, specs.Length);
        foreach (var spec in specs)
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(spec));
            var model = (string)json["name"]!;
            Assert.Equal("+Z", (string?)json["unity_front_axis"]);
            var axis = (string?)json["unity_operator_axis"];
            Assert.Contains(axis, new[] { "+Z", "-Z" });
            foreach (var side in new[] { 1, -1 })
            for (var yaw = 0; yaw < 360; yaw++)
            {
                var facing = CartStance.Forward(yaw + CartStance.EquipmentYaw(side, model));
                var sign = axis == "+Z" ? 1 : -1;
                Near(Unity(yaw, (-side, 0)), (sign * facing.X, sign * facing.Z), model + " controls toward server");
            }
        }
    }

    private const double Tolerance = 1e-9;

    public static TheoryData<double, int> Headings()
    {
        var data = new TheoryData<double, int>();
        foreach (var yaw in new[] { 0.0, 90.0, 180.0, 270.0, 17.3, 45.0, 133.7, 241.25, 359.9, -30.0, 721.0 })
            foreach (var side in new[] { 1, -1 })
                data.Add(yaw, side);
        return data;
    }

    // Unity's Quaternion.Euler(0, yaw, 0) * local, written out independently of CartStance.
    private static (double X, double Z) Unity(double yaw, (double X, double Z) local)
    {
        var r = yaw * Math.PI / 180.0;
        return (local.X * Math.Cos(r) + local.Z * Math.Sin(r), -local.X * Math.Sin(r) + local.Z * Math.Cos(r));
    }

    private static void Near((double X, double Z) expected, (double X, double Z) actual, string what)
    {
        Assert.True(Math.Abs(expected.X - actual.X) < 1e-6 && Math.Abs(expected.Z - actual.Z) < 1e-6,
            $"{what}: expected ({expected.X:F6}, {expected.Z:F6}), got ({actual.X:F6}, {actual.Z:F6})");
    }

    private static double Dot((double X, double Z) a, (double X, double Z) b) => a.X * b.X + a.Z * b.Z;
    private static (double X, double Z) Minus((double X, double Z) a, (double X, double Z) b) => (a.X - b.X, a.Z - b.Z);

    [Theory]
    [InlineData(true, -1, 1, -1)]
    [InlineData(true, 1, -1, 1)]
    [InlineData(true, null, -1, -1)]
    [InlineData(true, null, 1, 1)]
    [InlineData(false, -1, -1, 1)]
    [InlineData(false, null, -1, 1)]
    public void DisplaysFollowQueueAndRetainSideWhileOpenWithoutLine(bool open, int? queue, int previous, int expected) =>
        Assert.Equal(expected, CartStance.DisplaySide(open, queue, previous));

    [Fact]
    public void UnturnedCartWithTheQueueOnTheRight()
    {
        Assert.Equal((0.29, 0.95, 0.615, 90.0), CartStance.Register(1));
        Assert.Equal((-0.85, 0.0), CartStance.Server(1));
        Assert.Equal((1.05, 0.0), CartStance.Window(1, 1.05));
        Near((1, 0), CartStance.Facing((0, 1), 1), "faces +X");
    }

    [Fact]
    public void QueueOnTheLeftMirrorsRegisterAndServer()
    {
        Assert.Equal((-0.29, 0.95, 0.615, -90.0), CartStance.Register(-1));
        Assert.Equal((0.85, 0.0), CartStance.Server(-1));
        Near((-1, 0), CartStance.Facing((0, 1), -1), "faces -X");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(-7, -1)]
    public void SidesAreTheSignWithNoneCountingAsTheRight(int side, int expected)
    {
        Assert.Equal(expected, CartStance.Side(side));
        Assert.Equal(CartStance.Register(expected), CartStance.Register(side));
        Assert.Equal(CartStance.Server(expected), CartStance.Server(side));
    }

    [Fact]
    public void ServerIsCentredAndIndependentOfTheCornerRegister()
    {
        // The register stays on the front corner; service belongs at the working centre.
        foreach (var side in new[] { 1, -1 })
        {
            Assert.Equal(0, CartStance.Server(side).Z, 9);
            Assert.NotEqual(CartStance.Register(side).Z, CartStance.Server(side).Z);
            Assert.True(Math.Sign(CartStance.Server(side).X) == -side, "server on the side away from the customers");
            Assert.True(Math.Sign(CartStance.Register(side).X) == side, "register on the customers' side of the counter");
        }
    }

    [Theory]
    [MemberData(nameof(Headings))]
    public void MatchesUnityTransformAtEveryHeading(double yaw, int side)
    {
        var cart = (12.5, -40.25);
        var forward = CartStance.Forward(yaw);
        var server = CartStance.ToWorld(cart, forward, CartStance.Server(side));
        var u = Unity(yaw, CartStance.Server(side));
        Near((cart.Item1 + u.X, cart.Item2 + u.Z), server, "server");
        Near(Unity(yaw, (side, 0)), CartStance.Facing(forward, side), "facing");
    }

    [Theory]
    [MemberData(nameof(Headings))]
    public void AtTheWorkingCentreFacingTheCustomers(double yaw, int side)
    {
        var cart = (-3.0, 7.0);
        var forward = CartStance.Forward(yaw);
        var reg = CartStance.Register(side);
        var register = CartStance.ToWorld(cart, forward, (reg.X, reg.Z));
        var server = CartStance.ToWorld(cart, forward, CartStance.Server(side));
        var window = CartStance.ToWorld(cart, forward, CartStance.Window(side, CartStance.WindowOut));
        var facing = CartStance.Facing(forward, side);
        var right = CartStance.DirectionToWorld(forward, (1, 0));

        // The first customer is straight ahead; the register remains to one side.
        var toRegister = Minus(register, server);
        var toWindow = Minus(window, server);
        Assert.True(Math.Abs(toRegister.X * facing.Z - toRegister.Z * facing.X) > 0.6, "register remains off centre");
        Assert.True(Math.Abs(toWindow.X * facing.Z - toWindow.Z * facing.X) < Tolerance, "first customer straight ahead");
        Assert.Equal(CartStance.BehindMetres + CartStance.RegisterAcross, Dot(toRegister, facing), 9);
        Assert.True(Dot(toWindow, facing) > Dot(toRegister, facing), "the register stands between server and customer");

        // Behind the cart: on the side away from the queue, clear of the 0.50 m body and wheels.
        Assert.Equal(-side * CartStance.BehindMetres, Dot(Minus(server, cart), right), 9);
        Assert.Equal(0, Dot(Minus(server, cart), forward), 9);

        // The register faces the customers: its model faces +Z, turned by its yaw on top of the cart's.
        Near(facing, CartStance.Forward(yaw + reg.Yaw), "register faces the customers");
    }

    [Theory]
    [MemberData(nameof(Headings))]
    public void ToLocalUndoesToWorld(double yaw, int side)
    {
        var cart = (100.0, 0.5);
        var forward = CartStance.Forward(yaw);
        foreach (var local in new[] { CartStance.Server(side), CartStance.Window(side, CartStance.WindowOut), (0.3, -1.4) })
            Near(local, CartStance.ToLocal(cart, forward, CartStance.ToWorld(cart, forward, local)), "round trip");
    }

    [Fact]
    public void PlacesFollowTheCartWhenItIsMovedOrTurned()
    {
        // The places come from the live position and heading only: a cart moved 20 m and turned 90 degrees has
        // the same places relative to itself, not those of where it stood before.
        var before = CartStance.ToWorld((0, 0), CartStance.Forward(0), CartStance.Server(1));
        var after = CartStance.ToWorld((20, 5), CartStance.Forward(90), CartStance.Server(1));
        Near((-0.85, 0.0), before, "before");
        Near((20.0, 5 + 0.85), after, "after");
        Near(CartStance.Server(1), CartStance.ToLocal((20, 5), CartStance.Forward(90), after), "same place on the cart");
    }

    [Fact]
    public void ForwardOfAnyLengthOrTiltGivesTheSamePlaces()
    {
        var unit = CartStance.ToWorld((1, 2), (0.6, 0.8), CartStance.Server(-1));
        Near(unit, CartStance.ToWorld((1, 2), (0.3, 0.4), CartStance.Server(-1)), "half-length forward (a tilted cart)");
        Near(unit, CartStance.ToWorld((1, 2), (6, 8), CartStance.Server(-1)), "long forward");
        Assert.Equal(CartStance.Yaw((0.6, 0.8)), CartStance.Yaw((0.3, 0.4)), 9);
        Near((1 + 0.85 * 0.8, 2 - 0.85 * 0.6), unit, "worked example");
    }

    [Fact]
    public void ACartWithNoGroundForwardCountsAsFacingPlusZ()
    {
        Near(CartStance.Server(1), CartStance.ToWorld((0, 0), (0, 0), CartStance.Server(1)), "degenerate forward");
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, 90.0)]
    [InlineData(180.0, 180.0)]
    [InlineData(270.0, 270.0)]
    [InlineData(-90.0, 270.0)]
    [InlineData(33.3, 33.3)]
    public void YawRoundTrips(double yaw, double expected)
    {
        Assert.Equal(expected, CartStance.Yaw(CartStance.Forward(yaw)), 6);
    }

    [Fact]
    public void WaitingCrewStandAlongTheServersSideAwayFromThePlayer()
    {
        foreach (var side in new[] { 1, -1 })
        {
            var server = CartStance.Server(side);
            // The player at the server's place: crew wait towards the rear, a step apart.
            Assert.Equal((server.X, server.Z - 0.9), CartStance.Waiting(side, 0, server.Z));
            Assert.Equal((server.X, server.Z - 1.8), CartStance.Waiting(side, 1, server.Z));
            // The player at the rear end: crew wait towards the front instead.
            Assert.Equal((server.X, server.Z + 0.9), CartStance.Waiting(side, 0, -1.07));
            // No player place known: towards the rear.
            Assert.Equal((server.X, server.Z - 0.9), CartStance.Waiting(side, 0, null));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => CartStance.Waiting(1, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => CartStance.Server(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CartStance.Server(1, double.NaN));
    }

    [Theory]
    [InlineData(0.615, false)]
    [InlineData(0.30, true)]
    [InlineData(-0.30, true)]
    [InlineData(0.0, true)]
    [InlineData(2.5, false)]
    [InlineData(-1.07, false)]
    public void OnlyAServerNearTheWorkingCentreFacesStraightAcross(double along, bool across)
    {
        Assert.Equal(across, CartStance.FacesAcross(along));
    }

    [Fact]
    public void ServerKeepsAWiderBodyClearAtTheCentre()
    {
        Assert.Equal((-1.2, 0.0), CartStance.Server(1, 1.2));
        Assert.Equal((1.2, 0.0), CartStance.Server(-1, 1.2));
    }

    [Theory]
    [InlineData(0.3, -0.3, 0)]
    [InlineData(0.6, -0.2, 0.2)]
    [InlineData(0.6, null, 0)]
    [InlineData(null, -0.2, 0)]
    [InlineData(null, null, 0)]
    public void FittedSpotsChooseMidpointOrCounterCentre(double? front, double? rear, double expected)
    {
        var along = CartStance.WorkAlong(front, rear);
        Assert.Equal(expected, along, 9);
        foreach (var side in new[] {1, -1})
        {
            Assert.Equal(along, CartStance.Server(side, along: along).Z);
            Assert.Equal(along, CartStance.Window(side, 1.05, along).Z);
            Assert.True(CartStance.FacesAcross(along, along));
            Assert.Equal(along - 0.9, CartStance.Waiting(side, 0, null, along: along).Z);
        }
    }

    [Theory]
    [MemberData(nameof(Headings))]
    public void AsymmetricFittedMidpointAlignsServerAndCustomerAtEveryHeading(double yaw, int side)
    {
        var along = CartStance.WorkAlong(0.6, -0.2);
        var server = CartStance.ToWorld((3, 7), CartStance.Forward(yaw), CartStance.Server(side, along: along));
        var customer = CartStance.ToWorld((3, 7), CartStance.Forward(yaw), CartStance.Window(side, 1.05, along));
        Near(Unity(yaw, (side * 1.9, 0)), Minus(customer, server), "customer across from midpoint server");
    }

    // CartAssets builds the register; the build editor can't reference Core, so read its numbers from the source.
    [Fact]
    public void RegisterMatchesTheCartPrefabBuilder()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "unity", "BuildEditor", "CartAssets.cs"));
        var position = Regex.Match(source, @"RegisterPosition = new Vector3\(([-0-9.]+)f, ([-0-9.]+)f, ([-0-9.]+)f\)");
        var rotation = Regex.Match(source, @"RegisterRotation = Quaternion\.Euler\(0f, ([-0-9.]+)f, 0f\)");
        Assert.True(position.Success && rotation.Success, "CartAssets' register position and rotation not found");
        var register = CartStance.Register(1);
        Assert.Equal(register.X, double.Parse(position.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 6);
        Assert.Equal(register.Y, double.Parse(position.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), 6);
        Assert.Equal(register.Z, double.Parse(position.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), 6);
        Assert.Equal(register.Yaw, double.Parse(rotation.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 6);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FoodTrucks.sln"))) return dir.FullName;
        throw new InvalidOperationException("FoodTrucks.sln not found above " + AppContext.BaseDirectory);
    }
}
