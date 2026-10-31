using Entities;
using FoodTrucks;
using FoodTrucks.Core.Saves;
using Helpers;

static class Program
{
    const string Truck = "foodtrucks:vehicletype_foodtruck", Cart = "foodtrucks:vehicletype_foodcart", Spike = "foodtrucks:vehicletype_spike";
    // 15000 purchase price, 2 x 10 paid stock and 1 x 5 nested stock, in cents.
    const long Refund = 1502500;
    static GameInstance Game => SaveGameManager.Current!;
    static List<VehicleInstance> Street => Game.VehicleInstances;
    static List<VehicleInstance> Garage => Game.privateDriverVehicleInstances!;
    static int failures;

    static void Reset()
    {
        SaveGameManager.Current = new GameInstance();
        FoodTrucksSession.CurrentState = new SaveState(); FoodTrucksSession.LastLoadStatus = LoadStatus.Loaded;
        CartSelling.Settle = true; CartSelling.Notices.Clear();
        Payments.Accept = true; Payments.Paid.Clear();
        VehicleHelper.DeleteFault = VehicleHelper.Fault.None; VehicleHelper.Deletes = VehicleHelper.NullControllerDeletes = 0;
        Commissary.Reversed = 0; HireCrew.Released = 0;
    }

    // Two presses within the confirmation window, well after any earlier arming has lapsed.
    static void Uninstall()
    {
        UnityEngine.Time.unscaledTime += 100f; FoodTrucks.Uninstall.Request();
        UnityEngine.Time.unscaledTime += 1f; FoodTrucks.Uninstall.Request();
    }

    static string First => CartSelling.Notices.Count == 0 ? "" : CartSelling.Notices[0];
    static string Last => CartSelling.Notices.Count == 0 ? "" : CartSelling.Notices[^1];
    static bool NoModVehicles => !Street.Concat(Game.privateDriverVehicleInstances ?? new()).Any(v => FoodTrucksMod.IsModVehicle(v.vehicleTypeName));

    static void Check(string name, bool condition, string why)
    {
        if (condition) return;
        failures++;
        Console.WriteLine($"FAIL {name}: {why} (notices: {string.Join(" | ", CartSelling.Notices)}; paid: {string.Join(",", Payments.Paid)}; street {Street.Count}, garage {Game.privateDriverVehicleInstances?.Count})");
    }

    static void Main()
    {
        // Stored-only: a truck left with the private driver is counted, taken out of storage and refunded once.
        Reset(); Garage.Add(new VehicleInstance("t1", Truck));
        Uninstall();
        Check("stored-only", First == "foodtrucks:notification_uninstall_confirm count=1 deliveries=0", "first press did not count the stored truck");
        Check("stored-only", Garage.Count == 0, "stored truck left in the garage");
        Check("stored-only", Payments.Paid.SequenceEqual(new[] { Refund }), "not refunded exactly once");
        Check("stored-only", Last.StartsWith("foodtrucks:notification_uninstall_done count=1"), "no success notice");
        Check("stored-only", VehicleHelper.NullControllerDeletes == 0, "native delete called without a controller for a stored truck");
        // A repeated confirmed uninstall pays nothing more.
        CartSelling.Notices.Clear(); Uninstall(); Uninstall();
        Check("repeat", Payments.Paid.Count == 1, "second uninstall paid again");
        Check("repeat", Last == "foodtrucks:notification_uninstall_none", "repeat not reported clean");

        // Active-only.
        Reset(); Street.Add(new VehicleInstance("c1", Cart));
        Uninstall();
        Check("active-only", Street.Count == 0 && Payments.Paid.SequenceEqual(new[] { Refund }), "active cart not removed/refunded once");
        Check("active-only", VehicleHelper.Deletes == 1 && VehicleHelper.NullControllerDeletes == 0 && HireCrew.Released == 1, "native delete or crew release missing");
        Check("active-only", Last.StartsWith("foodtrucks:notification_uninstall_done count=1"), "no success notice");

        // Mixed: active cart, stored truck and an old spike type in storage; vanilla and other-mod vehicles in both lists stay.
        Reset();
        Street.Add(new VehicleInstance("c1", Cart)); Street.Add(new VehicleInstance("v1", "ba:vehicletype_van"));
        Garage.Add(new VehicleInstance("v2", "ba:vehicletype_sedan")); Garage.Add(new VehicleInstance("t1", Truck));
        Garage.Add(new VehicleInstance("s1", Spike)); Garage.Add(new VehicleInstance("o1", "othermod:vehicletype_bus"));
        Uninstall();
        Check("mixed", First == "foodtrucks:notification_uninstall_confirm count=3 deliveries=0", "confirmation miscounted");
        Check("mixed", NoModVehicles && Payments.Paid.Count == 3 && Payments.Paid.Sum() == 3 * Refund, "fleet not removed/refunded once each");
        Check("mixed", Street.Select(v => v.id).SequenceEqual(new[] { "v1" }) && Garage.Select(v => v.id).SequenceEqual(new[] { "v2", "o1" }), "unrelated vehicles touched");
        Check("mixed", Last.StartsWith("foodtrucks:notification_uninstall_done count=3"), "no success notice");

        // Empty, and an active cart with no private-driver list at all.
        Reset(); Uninstall();
        Check("empty", CartSelling.Notices.SequenceEqual(new[] { "foodtrucks:notification_uninstall_confirm_none", "foodtrucks:notification_uninstall_none" }) && Payments.Paid.Count == 0, "empty save not clean");
        Reset(); Game.privateDriverVehicleInstances = null; Street.Add(new VehicleInstance("c1", Cart)); Uninstall();
        Check("null-storage", Street.Count == 0 && Payments.Paid.Count == 1 && Last.StartsWith("foodtrucks:notification_uninstall_done"), "missing garage list broke uninstall");

        // Same object in both lists, a distinct stored copy with the same ID and a repeated stored reference.
        Reset(); var shared = new VehicleInstance("t1", Truck);
        Street.Add(shared); Garage.Add(shared); Garage.Add(new VehicleInstance("t1", Truck)); Garage.Add(shared);
        Uninstall();
        Check("same-id", First == "foodtrucks:notification_uninstall_confirm count=1 deliveries=0", "one vehicle ID counted more than once");
        Check("same-id", Street.Count == 0 && Garage.Count == 0 && Payments.Paid.SequenceEqual(new[] { Refund }), "copies left or refunded more than once");
        Check("same-id", Last.StartsWith("foodtrucks:notification_uninstall_done count=1"), "no success notice");
        Reset(); Street.Add(shared); Street.Add(shared); Uninstall();
        Check("repeated-ref", Street.Count == 0 && Payments.Paid.Count == 1, "repeated street reference left or paid twice");

        // A stored truck with a pending delivery: the delivery is cancelled and the truck refunded.
        Reset(); Garage.Add(new VehicleInstance("t1", Truck)); Game.vehicleDeliveryContracts!.Add(new VehicleDeliveryContract { vehicleTypeName = Truck });
        Uninstall();
        Check("delivery", First == "foodtrucks:notification_uninstall_confirm count=1 deliveries=1", "confirmation miscounted");
        Check("delivery", Game.vehicleDeliveryContracts.Count == 0 && Garage.Count == 0 && Payments.Paid.Count == 1 && Last.StartsWith("foodtrucks:notification_uninstall_done"), "not cleaned");

        // Native delete fails (throws, or returns without removing): no refund, no success; a later retry pays once.
        foreach (var fault in new[] { VehicleHelper.Fault.Throw, VehicleHelper.Fault.Keep })
        {
            Reset(); Street.Add(new VehicleInstance("t1", Truck)); Garage.Add(new VehicleInstance("t2", Truck));
            VehicleHelper.DeleteFault = fault; Uninstall();
            Check($"delete-{fault}", Street.Count == 1 && Garage.Count == 0 && Payments.Paid.SequenceEqual(new[] { Refund }), "failed vehicle refunded or stored one not handled");
            Check($"delete-{fault}", Last.StartsWith("foodtrucks:notification_uninstall_partial count=1 left=1"), "failure not reported as partial");
            VehicleHelper.DeleteFault = VehicleHelper.Fault.None; Uninstall(); Uninstall();
            Check($"delete-{fault}-retry", NoModVehicles && Payments.Paid.Count == 2, "retry did not pay exactly once");
        }

        // Refund refused: the truck is gone but there is no success notice.
        Reset(); Garage.Add(new VehicleInstance("t1", Truck)); Payments.Accept = false; Uninstall();
        Check("unpaid", Garage.Count == 0 && Last.StartsWith("foodtrucks:notification_uninstall_partial count=1 left=0") && Last.Contains("unpaid=" + Refund), "unpaid refund reported as done");

        // Mod items in a stored truck come off the commissary deduction once.
        Reset(); var stocked = new VehicleInstance("t1", Truck); stocked.cargoInstances[0].itemName = ModItems.Prefix + "tacos";
        Garage.Add(stocked); Uninstall(); Uninstall();
        Check("deduction", Commissary.Reversed == 2000 && Payments.Paid.Count == 1, "mod-item deduction not reversed exactly once");

        // The held-vehicle guard and the newer-save gate still stop before anything goes.
        Reset(); Street.Add(new VehicleInstance("c1", Cart)); Garage.Add(new VehicleInstance("t1", Truck)); Game.ActiveVehicleId = "c1"; Uninstall();
        Check("held", Street.Count == 1 && Garage.Count == 1 && Payments.Paid.Count == 0 && Last == "foodtrucks:notification_uninstall_release", "held cart not refused");
        Reset(); Garage.Add(new VehicleInstance("t1", Truck)); CartSelling.Settle = false;
        FoodTrucksSession.CurrentState = null; FoodTrucksSession.LastLoadStatus = LoadStatus.NewerVersion;
        Uninstall();
        Check("newer", Garage.Count == 1 && Payments.Paid.Count == 0 && Last == "foodtrucks:notification_uninstall_newer_version", "newer save not refused");

        // A vehicle without an ID is never taken for gone.
        Reset(); Garage.Add(new VehicleInstance(null, Truck)); Uninstall();
        Check("no-id", Payments.Paid.Count == 0 && !CartSelling.Notices.Any(n => n.StartsWith("foodtrucks:notification_uninstall_done") || n == "foodtrucks:notification_uninstall_none"), "vehicle without an ID reported safe");

        if (failures > 0)
        {
            Console.WriteLine($"{failures} uninstall checks failed");
            Environment.Exit(1);
        }
        Console.WriteLine("PASS: uninstall stored-only, active-only, mixed, empty, same ID, repeat, unrelated vehicles, delete and payment failures, gates");
    }
}
