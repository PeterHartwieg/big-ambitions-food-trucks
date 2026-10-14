using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Storage = FoodTrucks.Core.Selling.Storage;

namespace FoodTrucks.Core.Tests.Soak;

internal sealed partial class SoakWorld
{
    private readonly CommissaryRules commissaryRules = new();
    private const long OpeningMoney = 10_000_000;
    private long money = OpeningMoney;
    private sealed record Entry(long Cents, string Kind, int Day, string Key);
    private readonly List<Entry> book = new();
    private readonly HashSet<(string, string)> paid = new();
    private readonly Dictionary<long, (string Site, string Item, int Units, long Price, long Total)> orders = new();
    private readonly HashSet<long> decided = new(), delivered = new(), refused = new();
    private readonly HashSet<(string, int)> nights = new(), rents = new();
    private readonly Dictionary<string, long> initial = new(), received = new(), sold = new(), refunded = new();
    private long initialValue, receivedValue, refundedValue;

    // The book records each accepted game payment. Refusal touches neither balance nor book.
    // Forced wages can overdraw; delivery, rent and fine payments require enough cash.
    private bool Move(long cents, string kind, int day, string key, bool tryOnly = false, bool refuse = false)
    {
        var balance = money; var entries = book.Count;
        if (refuse || (tryOnly && cents < 0 && money < -cents))
        {
            Require(money == balance && book.Count == entries, "refused payment changed money");
            return false;
        }
        Require(paid.Add((kind, key)), $"duplicate {kind} payment: {key}");
        money += cents;
        book.Add(new Entry(cents, kind, day, key));
        return true;
    }

    private void Rent(VehicleState v, string site)
    {
        Commissaries.Rent(state.Commissary, commissaryRules, v.VehicleId, v.VehicleType, site, Day);
        ChargeRent();
    }

    private void ChargeRent()
    {
        foreach (var charge in Commissaries.RentDue(state.Commissary, commissaryRules, Day))
        {
            Require(rents.Add((charge.VehicleId, charge.Day)), "rent generated twice, including release/re-rent");
            Require(charge.Cents == commissaryRules.DailyRentCents(state.Vehicles.Single(v => v.VehicleId == charge.VehicleId).VehicleType), "wrong vehicle rent");
            if (Move(-charge.Cents, "rent", Day, $"{charge.VehicleId}:{charge.Day}", tryOnly: true))
                SalesLedger.RecordCommissaryRent(state, Day, charge.VehicleId, charge.Cents);
            else state.Commissary.Charges.Add(charge);
        }
    }

    private void Order(VehicleState v)
    {
        var sites = state.Commissary.Spaces.Select(s => s.CommissaryId).Distinct().ToArray();
        if (sites.Length == 0) return;
        var site = sites[random.Next(sites.Length)];
        var menu = Menu.FromCargo(ShippedData.Tables, v.VehicleType, Lines(v)).Menu.Items;
        var item = random.Next(5) == 0 ? Pieces[random.Next(Pieces.Length)] : menu[random.Next(menu.Count)];
        var machine = ShippedData.Tables.EquipmentForGameItem(item) != null;
        var price = machine ? random.Next(20000, 100000) : random.Next(80, 201);
        var quote = CommissaryOrdering.Quote(commissaryRules, new[] { new CommissaryOrderRequest(item, machine ? 1 : 40, price, random.Next(1, 4), !machine) });
        // The order form refuses a food over its weekly limit; Place must refuse it too.
        if (!CommissarySupply.Check(state.Commissary, commissaryRules, site, quote, CommissaryRepeat.Once, Day).Fits)
        {
            Require(Throws(() => CommissaryOrdering.Place(state.Commissary, commissaryRules, site, quote, (Day, Minute))), "order over the weekly limit placed");
            Note($"order of {item} at {site} refused: weekly limit");
            return;
        }
        var before = money;
        foreach (var o in CommissaryOrdering.Place(state.Commissary, commissaryRules, site, quote, (Day, Minute)))
        {
            orders.Add(o.Id, (site, item, o.Units, price, o.TotalCents));
            Note($"order {o.Id}: {o.Units} {item} at {site}, due day {o.DeliverDay}");
        }
        Require(money == before, "order paid before delivery");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private void CommissaryTick()
    {
        // Commissary.Update: retry debts, rent due, deliveries, then the overnight check.
        foreach (var c in state.Commissary.Charges.ToList())
            if (Move(-c.Cents, "rent", Day, $"{c.VehicleId}:{c.Day}", tryOnly: true))
            { SalesLedger.RecordCommissaryRent(state, Day, c.VehicleId, c.Cents); state.Commissary.Charges.Remove(c); }
        foreach (var f in state.Commissary.Fines.ToList())
            if (Move(-f.Cents, "fine", Day, $"{f.VehicleId}:{f.Day}", tryOnly: true))
            { SalesLedger.RecordOvernightFine(state, Day, f.VehicleId, f.Cents); state.Commissary.Fines.Remove(f); }
        ChargeRent();
        var before = Inventory();
        var attempted = new List<long>();
        var result = Commissaries.Deliver(state.Commissary, commissaryRules, Day, Minute, o =>
        {
            Require(o.DeliverDay < Day || (o.DeliverDay == Day && Minute >= 480), "payment requested before 08:00 delivery");
            Require(!decided.Contains(o.Id), "cancelled/delivered order attempted again");
            attempted.Add(o.Id);
            Require(orders[o.Id] == (o.CommissaryId, o.ItemName, o.Units, o.UnitPriceCents, o.TotalCents), "order changed before delivery");
            var accepted = Move(-o.TotalCents, "order payment", Day, o.Id.ToString(), tryOnly: true, refuse: random.Next(5) == 0);
            if (accepted) SalesLedger.RecordDeliveryFees(state, Day, o.FeeCents);
            return accepted;
        });
        var delta = new Dictionary<string, long>();
        foreach (var o in result.Delivered)
        {
            Require(decided.Add(o.Id) && delivered.Add(o.Id) && !refused.Contains(o.Id), "order delivered twice or after cancellation");
            Require(paid.Contains(("order payment", o.Id.ToString())), "unpaid order delivered");
            Add(delta, o.ItemName, o.Units); Add(received, o.ItemName, o.Units);
            receivedValue += o.GoodsCents;
            Note($"delivered order {o.Id}");
        }
        foreach (var o in result.Refused)
        {
            Require(decided.Add(o.Id) && refused.Add(o.Id) && !paid.Contains(("order payment", o.Id.ToString())), "refused order paid");
            Note($"refused/cancelled order {o.Id}");
        }
        Require(attempted.SequenceEqual(result.Delivered.Concat(result.Refused).OrderBy(o => o.DeliverDay).ThenBy(o => o.Id).Select(o => o.Id)), "delivery payment order differs");
        AddUnits(before, delta);
        EqualUnits(before, Inventory(), "delivered stock differs from ordered and paid units");
        Require(state.Commissary.Orders.All(o => !decided.Contains(o.Id)), "decided order remains open");

        // Flags are game facts at the check; Core owns exemptions and the per-night cursor.
        var flags = state.Vehicles.Select(v => new CommissaryVehicle { VehicleId = v.VehicleId,
            AtOwnCommissaryBay = vans[v.VehicleId].Bay == state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == v.VehicleId)?.CommissaryId
                && vans[v.VehicleId].Bay != null,
            PlayerInside = Awake && atWindow == v.VehicleId,
            OnLateStop = v.IsOpen && v.HireStop != null,
            InTransit = v.Transit != null,
            AtPlayerProperty = vans[v.VehicleId].Property }).ToList();
        var fines = Commissaries.NightCheck(state.Commissary, commissaryRules, Day, Minute, flags);
        if (Minute == 240)
        {
            var expected = flags.Where(f => !f.PlayerInside && !f.OnLateStop && !f.InTransit && !f.AtPlayerProperty && !f.AtOwnCommissaryBay)
                .Select(f => f.VehicleId).OrderBy(x => x);
            Require(expected.SequenceEqual(fines.Select(f => f.VehicleId).OrderBy(x => x)), "04:00 fines on wrong vehicles or exemptions");
        }
        else Require(fines.Count == 0, "fine generated outside 04:00");
        foreach (var fine in fines)
        {
            Require(fine.Day == Day && fine.Cents == 25000 && nights.Add((fine.VehicleId, fine.Day)), "wrong/duplicate nightly fine");
            if (Move(-fine.Cents, "fine", Day, $"{fine.VehicleId}:{fine.Day}", tryOnly: true))
                SalesLedger.RecordOvernightFine(state, Day, fine.VehicleId, fine.Cents);
            else state.Commissary.Fines.Add(fine);
        }
        Require(Commissaries.NightCheck(state.Commissary, commissaryRules, Day, Minute, flags).Count == 0, "night repeat produced fines");
    }

    private Dictionary<string, int> Available(string site) => state.Commissary.Stock.TryGetValue(site, out var lines)
        ? lines.GroupBy(l => l.ItemName).ToDictionary(g => g.Key, g => g.Sum(l => l.Units)) : new();

    private void Restock(VehicleState v, string site)
    {
        var before = Inventory();
        Load(v, site, Storage.FitPieces(ShippedData.Tables, v.VehicleType, Lines(v), Available(site)));
        var plan = Storage.Restock(ShippedData.Tables, v.VehicleType, Lines(v), Available(site));
        Load(v, site, plan.Load);
        EqualUnits(before, Inventory(), "restock created or lost units");
    }

    private void Load(VehicleState v, string site, IReadOnlyDictionary<string, int> wanted)
    {
        var taken = Commissaries.Restock(state.Commissary, site, wanted);
        foreach (var l in taken)
        {
            var permitted = Storage.Check(ShippedData.Tables, v.VehicleType, Lines(v), l.ItemName, l.Units, false).Allowed;
            Require(permitted == l.Units, "restock planner exceeds loader capacity");
            // The game can reject cargo for its own slot reasons: return leftovers with the FIFO batch.
            var accepted = random.Next(8) == 0 ? random.Next(l.Units + 1) : l.Units;
            if (accepted > 0) vans[v.VehicleId].Cargo.Add(new Lot(l.ItemName, accepted, l.UnitPriceCents));
            if (accepted < l.Units)
            {
                l.Units -= accepted;
                Commissaries.ReturnToStock(state.Commissary, site, new[] { l });
            }
        }
    }

    private Dictionary<string, long> Inventory()
    {
        var units = new Dictionary<string, long>();
        foreach (var l in vans.Values.SelectMany(v => v.Cargo)) Add(units, l.Item, l.Units);
        foreach (var l in state.Commissary.Stock.Values.SelectMany(l => l)) Add(units, l.ItemName, l.Units);
        return units;
    }
    private static void AddUnits(Dictionary<string, long> target, Dictionary<string, long> source)
    { foreach (var p in source) Add(target, p.Key, p.Value); }
    private static void EqualUnits(Dictionary<string, long> expected, Dictionary<string, long> actual, string message)
    {
        foreach (var key in expected.Keys.Concat(actual.Keys).Distinct())
        { expected.TryGetValue(key, out var e); actual.TryGetValue(key, out var a); Require(e == a, $"{message}: {key} expected {e}, got {a}"); }
    }
    private void CheckInventory()
    {
        Require(state.Commissary.Stock.Values.SelectMany(l => l).All(l => l.Units > 0), "commissary stock not positive");
        var expected = new Dictionary<string, long>(initial);
        AddUnits(expected, received);
        foreach (var p in sold) Add(expected, p.Key, -p.Value);
        foreach (var p in refunded) Add(expected, p.Key, -p.Value);
        EqualUnits(expected, Inventory(), "initial + paid deliveries - sold - refunded != inventory");
        var value = vans.Values.SelectMany(v => v.Cargo).Sum(l => l.Units * l.Price) +
            state.Commissary.Stock.Values.SelectMany(l => l).Sum(l => l.Units * l.UnitPriceCents);
        Require(initialValue + receivedValue - costs.Values.Sum() - refundedValue == value,
            "FIFO cargo costs/refunds created or lost goods value");
    }

    private void CheckExpenses()
    {
        // The game books retries on payment day, wages on their payroll day, and order goods
        // as stock costs only on sale. Compare ledger expenses against accepted payments.
        var entries = book.Where(e => e.Cents < 0).ToList();
        Require(state.DailySales.Sum(d => d.DeliveryFeesCents) == entries.Where(e => e.Kind == "order payment")
            .Sum(e => orders[long.Parse(e.Key)].Total - orders[long.Parse(e.Key)].Units * orders[long.Parse(e.Key)].Price), "delivery fee ledger differs from payments");
        Require(state.DailySales.SelectMany(d => d.ByVehicle).Sum(v => v.CommissaryRentCents) == -entries.Where(e => e.Kind == "rent").Sum(e => e.Cents), "rent ledger differs from payments");
        Require(state.DailySales.SelectMany(d => d.ByVehicle).Sum(v => v.OvernightFineCents) == -entries.Where(e => e.Kind == "fine").Sum(e => e.Cents), "fine ledger differs from payments");
        Require(state.DailySales.SelectMany(d => d.ByVehicle).Sum(v => v.WagesCents) == -entries.Where(e => e.Kind == "wage").Sum(e => e.Cents), "wage ledger differs from payments");
    }

    private void Uninstall()
    {
        Note("uninstall at end of run");
        CloseHours(long.MaxValue);
        Payroll(Day);
        PayTakings(true);
        ChargeRent();
        var goods = state.Commissary.Stock.Values.SelectMany(l => l).Sum(l => l.Units * l.UnitPriceCents);
        var open = state.Commissary.Orders.Count;
        foreach (var l in state.Commissary.Stock.Values.SelectMany(l => l)) Add(refunded, l.ItemName, l.Units);
        var refund = Commissaries.Refund(state.Commissary);
        refundedValue += refund.Total;
        Require(refund.Total == goods && refund.CancelledOrders == open, "uninstall must refund remaining commissary goods only; open orders unpaid");
        if (refund.Total > 0) Move(refund.Total, "refund", Day, "commissary");
        Require(state.Commissary.Orders.Count == 0 && state.Commissary.Stock.Count == 0 && state.Commissary.Spaces.Count == 0, "uninstall left stock/orders/spaces");
        Require(Commissaries.Refund(state.Commissary).Total == 0, "refund paid twice");
        Require(Commissaries.Deliver(state.Commissary, commissaryRules, Day + 1, 480, _ => throw new InvalidOperationException("cancelled order delivered")).Delivered.Count == 0,
            "cancelled order delivered after uninstall");
        // Vehicle purchase-price/cargo removal refund is Unity-owned Uninstall.RefundCents,
        // not a Core API: cargo remains in the stand-in for inventory conservation checks.
        foreach (var v in state.Vehicles) Close(v);
        CheckWorld();
        SaveLoad();
        foreach (var p in crew) Require(paidCrew.Contains(p.Key), "crew sale never paid");
    }
}
