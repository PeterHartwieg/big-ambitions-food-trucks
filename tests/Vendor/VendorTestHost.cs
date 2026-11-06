using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Vendor;
using FoodTrucks.ModApi;

namespace FoodTrucks.Core.Tests.Vendor;

// The game side for VendorService tests: a save, a clock and food vehicles whose sale books like CartSelling.SellItem
// (ledger and midnight pot first, then the unit).
internal sealed class VendorTestHost : IVendorHost
{
    public const string Owner = "persistentcity";

    public VendorApiStatus Status { get; set; } = VendorApiStatus.Ready;
    public SaveState Save { get; set; } = new SaveState();
    public SaveState? State => Status == VendorApiStatus.Ready ? Save : null;
    public long Now { get; set; }
    public readonly SortedDictionary<string, VendorFacts> Vehicles = new SortedDictionary<string, VendorFacts>(StringComparer.Ordinal);
    public readonly List<string> Lines = new List<string>();
    public int Changes;
    public int FactsCalls;
    public bool ThrowBeforeRecord;
    public bool ThrowAfterRecord;
    public readonly List<(string Vehicle, string Item, long Minute)> Sales = new List<(string, string, long)>();

    public IReadOnlyList<VendorFacts> Facts()
    {
        FactsCalls++;
        return Vehicles.Values.ToList();
    }

    public VendorFacts? FactsOf(string vehicleId) => Vehicles.TryGetValue(vehicleId, out var facts) ? facts : null;

    public long SellOne(string vehicleId, string item, long minute)
    {
        if (ThrowBeforeRecord) throw new InvalidOperationException("before");
        var facts = Vehicles[vehicleId];
        var index = facts.Items.ToList().IndexOf(item);
        var cents = facts.PriceCents[index];
        var day = VendorBook.DayOf(minute);
        if (cents <= 0 || facts.Units[index] <= 0) return -1;
        StreetSales.Record(Save, vehicleId, day, cents, 1);
        CrewTakings.Accrue(Save, vehicleId, day, cents);
        Sales.Add((vehicleId, item, minute));
        if (ThrowAfterRecord) throw new InvalidOperationException("after");
        var units = facts.Units.ToArray();
        units[index]--;
        facts.Units = units;
        return cents;
    }

    public void Changed() => Changes++;
    public void Log(string line) => Lines.Add(line);

    public static long At(int day, int hour, int minute) => (day - 1) * 1440L + hour * 60 + minute;

    public VendorFacts AddVehicle(string id, string kind = "cart", string[]? items = null, long[]? prices = null, int[]? units = null)
    {
        items ??= new[] { "ba:itemname_hotdog" };
        var facts = new VendorFacts
        {
            VehicleId = id, Kind = kind, X = 1, Y = 2, Z = 3, QueueX = 4, QueueY = 2, QueueZ = 5, QueueHeading = 90, MealsPerHour = 30,
            Items = items, PriceCents = prices ?? items.Select(_ => 450L).ToArray(), Units = units ?? items.Select(_ => 20).ToArray()
        };
        Vehicles[id] = facts;
        Save.Vehicles.Add(new VehicleState { VehicleId = id, VehicleType = kind });
        return facts;
    }

    public VehicleState Vehicle(string id) => StreetSales.Find(Save, id)!;

    // A window opens as CartSelling.StartSelling opens it: a fresh queue, then the vendor book's ID and owner.
    public string Open(string id, long minute, bool byCrew = true)
    {
        var vehicle = Vehicle(id);
        vehicle.IsOpen = true;
        vehicle.HireStop = byCrew ? 0 : (int?)null;
        vehicle.Service = SalesSimulator.Open(7);
        return VendorBook.Opened(Save, id, minute, byCrew, Lines);
    }

    public void Close(string id)
    {
        if (Save.Vendor != null) VendorBook.Closed(Save, id);
        var vehicle = Vehicle(id);
        vehicle.IsOpen = false;
        vehicle.HireStop = null;
        vehicle.Service = null;
    }
}
