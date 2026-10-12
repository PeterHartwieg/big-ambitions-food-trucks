using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit.Sdk;
using Storage = FoodTrucks.Core.Selling.Storage;

namespace FoodTrucks.Core.Tests.Soak;

// Game-owned cargo, positions and balance are plain stand-ins. Core owns all route, service,
// commissary, wage-hour, ledger and taking decisions. The oracle never calls Core to derive totals.
internal sealed partial class SoakWorld
{
    private readonly int seed;
    private readonly Random random;
    private readonly HireRunner runner = new(ShippedData.Rules.Fleet);
    private readonly HireSchedules schedules = new(ShippedData.Rules.Fleet);
    private readonly SalesSimulator simulator = new(ShippedData.Tables, ShippedData.Rules.Queue);
    private readonly SaveCodec codec = new();
    private SaveState state = new() { CrewHoursSince = 1 };
    private readonly Dictionary<string, Van> vans = new();
    private readonly Queue<string> actions = new();
    private long now;
    private long asleepUntil;
    private string? atWindow;
    private int nextEmployee;
    private int saves, crewSales, playerSales, routeSales, slept, invalidPlans;
    private readonly Dictionary<(string, int), long> revenue = new();
    private readonly Dictionary<(string, int), long> costs = new();
    private readonly Dictionary<(string, int), (long Cents, int Sales)> crew = new();
    private readonly HashSet<(string, int)> paidCrew = new();
    private readonly HashSet<(string, long)> creditedHours = new();

    private sealed class Van
    {
        public double X, Z;
        public string? Bay;
        public bool Property;
        public double SoldThrough;
        // Batches carry purchase prices just as game cargo entries do (not averaged).
        public List<Lot> Cargo = new();
    }
    private sealed record Lot(string Item, int Units, long Price);
    private int Day => (int)(now / 1440) + 1;
    private int Minute => (int)(now % 1440);
    private bool Awake => now >= asleepUntil;
    private static readonly string[] Pieces = { "ba:itemname_hotdoggrill", "ba:itemname_industrialgrill",
        "ba:itemname_industrialcoffeemachine", "ba:itemname_concessionsdisplaycase",
        "foodtrucks:itemname_griddle", "foodtrucks:itemname_storagecabinet" };

    public SoakWorld(int seed)
    {
        this.seed = seed;
        random = new Random(seed);
        foreach (var site in new[] { "a", "b" }) Core.Commissary.Commissaries.Unlock(state.Commissary, site);
        for (var i = 0; i < 1 + seed % 4; i++)
        {
            var v = new VehicleState { VehicleId = $"v{i}", VehicleType = i % 2 == 0 ? "truck" : "cart",
                HasPermit = true, Crew = new() { $"e{nextEmployee++}" } };
            state.Vehicles.Add(v);
            var van = vans[v.VehicleId] = new Van();
            var piece = Pieces[(seed % 5 + i) % 5];
            van.Cargo.Add(new Lot(piece, 1, 20000));
            foreach (var food in ShippedData.Tables.EquipmentForGameItem(piece)!.Products)
                van.Cargo.Add(new Lot(food, 8, 100));
            v.Schedule = NewRoute(i == 0 ? 1 : random.Next(4));
            Rent(v, "a");
        }
        state.Licence.HasVendorLicence = true;
        state.Licence.LicenceExpiresOnDay = int.MaxValue;
        AddUnits(initial, Inventory());
        initialValue = vans.Values.SelectMany(v => v.Cargo).Sum(l => l.Units * l.Price);
        Note("world created; initial cargo is an opening asset balance");
    }

    public string Coverage => $"saves={saves}, crew/player/route sales={crewSales}/{playerSales}/{routeSales}, " +
        $"paid/refused deliveries={delivered.Count}/{refused.Count}, invalid plans={invalidPlans}, sleep days={slept}";

    public void Run(int days)
    {
        try
        {
            for (var hour = 0; hour < days * 24; hour++)
            {
                now = hour * 60L;
                // The previous hour has sold completely before this hour's events (midnight included).
                CloseHours(hour);
                if (hour > 0 && hour % 24 == 0)
                {
                    Payroll(Day - 1);
                    PayTakings(false);
                    CheckLedger(Day - 1);
                    SalesLedger.MarkInSummary(state, Day - 1);
                }
                CountRouteHours(hour);
                CommissaryTick();
                if (Awake) PlayerAction();
                var boundary = now + (hour == days * 24 - 1 ? random.Next(1, 60) : 60);
                foreach (var id in vans.Keys) RunVehicle(state.Vehicles.Single(v => v.VehicleId == id), boundary);
                now = boundary;
                CheckWorld();
            }
            // Stop at a random minute of the last day: uninstall must pay today's waiting sales too.
            Uninstall();
            Require(saves > 0 && crewSales > 0 && playerSales > 0 && routeSales > 0 && invalidPlans > 0 &&
                slept > 0 && delivered.Count > 0 && refused.Count > 0,
                "non-vacuous save, crew-sale and delivery coverage: " + Coverage);
        }
        catch (Exception ex)
        {
            throw new XunitException($"seed={seed}, day={Day}, time={Minute / 60:D2}:{Minute % 60:D2}\n" +
                $"{ex}\nLast 20 actions:\n{string.Join("\n", actions)}");
        }
    }

    private void PlayerAction()
    {
        var v = state.Vehicles[random.Next(state.Vehicles.Count)];
        var van = vans[v.VehicleId];
        var choice = random.Next(12);
        Note($"player action {choice} on {v.VehicleId}");
        switch (choice)
        {
            case 0: Order(v); break;
            case 1:
                // Travel to a bay is game-owned. Never teleport an in-transit or open vehicle.
                if (v.Transit == null && !v.IsOpen) { van.Bay = random.Next(2) == 0 ? "a" : "b"; van.X = van.Z = 0; van.Property = false; Restock(v, van.Bay); }
                break;
            case 2: if (v.Transit == null && !v.IsOpen) { atWindow = v.VehicleId; Open(v, null, now + random.NextDouble()); } break;
            case 3: Close(v); break;
            case 4: if (v.IsOpen) atWindow = v.VehicleId; break;
            case 5: atWindow = null; break;
            case 6: ChangeRoute(v); break;
            case 7:
                if (v.Crew.Count < ShippedData.Tables.Vehicle(v.VehicleType)!.Staff)
                { v.Crew.Add($"e{nextEmployee++}"); HireRunner.Replan(v, now); }
                break;
            case 8:
                if (v.Crew.Count > 0)
                {
                    var id = v.Crew[0];
                    SettleFired(id);
                    v.Crew.RemoveAt(0);
                    if (v.Crew.Count == 0 && v.HireStop != null) Close(v);
                    HireRunner.Replan(v, now);
                }
                break;
            case 9: Rent(v, random.Next(2) == 0 ? "a" : "b"); break;
            case 10: ChargeRent(); Core.Commissary.Commissaries.Release(state.Commissary, v.VehicleId); break;
            case 11:
                if (random.Next(8) == 0) { asleepUntil = now + 1440; atWindow = null; slept++; Note("sleep for 1440 minutes; hourly events and routes continue"); }
                else if (!v.IsOpen && v.Transit == null) { van.Property = random.Next(2) == 0; van.Bay = null; van.X = random.Next(1000); v.Arrival = null; }
                break;
        }
    }

    private HireSchedule NewRoute(int kind)
    {
        ScheduleStop Stop(string name, int open, int close) => new() { SpotKey = name, X = random.Next(0, 500),
            Z = random.Next(0, 100), OpenAt = open, CloseAt = close };
        var route = new HireSchedule();
        if (kind == 1) route.Stops.Add(Stop("night", 23 * 60 + 30, 26 * 60 + 30));
        else if (kind == 2) { route.Stops.Add(Stop("early", 30, 120)); route.Stops.Add(Stop("night", 1380, 1590)); route.Days.Add(DayOfWeek.Friday); }
        else { route.Stops.Add(Stop("lunch", 660 + random.Next(2) * 30, 810)); route.Stops.Add(Stop("evening", 1080, 1200)); }
        if (kind == 3) route.Days = new() { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday };
        return route;
    }

    private void ChangeRoute(VehicleState v)
    {
        if (v.Schedule == null || v.Schedule.Stops.Count == 0) v.Schedule = NewRoute(random.Next(4));
        // Edits use RouteEditor, preserving yesterday's late legs and remapping indices.
        var before = codec.Serialize(state);
        var index = random.Next(v.Schedule.Stops.Count);
        var open = random.Next(48) * 30;
        var close = open + (random.Next(6) + 1) * 30;
        var previous = v.Schedule.Stops[index];
        Note($"{v.VehicleId}: edit stop {index} {previous.OpenAt}-{previous.CloseAt} to {open}-{close}; hire stop={v.HireStop}");
        var problem = RouteEditor.SetTimes(schedules, v, index, open, close);
        if (problem != ScheduleProblem.None) Require(before == codec.Serialize(state), "refused route edit mutated state");
        else HireRunner.Replan(v, now);
        if (random.Next(4) == 0)
        {
            var days = random.Next(2) == 0 ? new List<DayOfWeek>() : new List<DayOfWeek> { DayOfWeek.Friday };
            before = codec.Serialize(state);
            problem = RouteEditor.SetDays(schedules, v, days);
            if (problem != ScheduleProblem.None) Require(before == codec.Serialize(state), "refused days edit mutated state");
            else HireRunner.Replan(v, now);
        }
        // Malformed older saves enter through public state. Probe a copy, not a live edited route.
        var bad = new VehicleState { VehicleId = "old", VehicleType = v.VehicleType, Crew = new() { "probe" }, Schedule = NewRoute(0) };
        switch (random.Next(4))
        {
            case 0: bad.Schedule.Stops[0].CloseAt = bad.Schedule.Stops[0].OpenAt + 1441; break;
            case 1: bad.Schedule.Stops[1].OpenAt = bad.Schedule.Stops[0].CloseAt - 30; break;
            case 2: bad.Schedule.Stops[0].OpenAt = -30; break;
            case 3:
                bad.Schedule.Stops[0].OpenAt = 30; bad.Schedule.Stops[0].CloseAt = 120;
                bad.Schedule.Stops[1].OpenAt = 1380; bad.Schedule.Stops[1].CloseAt = 1590;
                break;
        }
        // SaveCodec deliberately preserves structurally sound old routes; HireRunner refuses their plans.
        var old = codec.Load(codec.Serialize(new SaveState { Vehicles = new() { bad } }));
        Require(old.Status == LoadStatus.Loaded, "old invalid route could not load");
        bad = old.State!.Vehicles[0];
        Require(runner.RouteProblem(bad) != ScheduleProblem.None, "invalid old route accepted");
        runner.EnsurePlan(bad, now, 0, 0);
        Require(bad.Shifts.Count == 0, "invalid old route received a plan");
        invalidPlans++;
    }

    private void RunVehicle(VehicleState v, long until)
    {
        var van = vans[v.VehicleId];
        var hourStart = now;
        var t = now;
        var saveAt = random.Next(96) == 0 ? now + random.Next(1, 60) : -1;
        while (t < until)
        {
            now = t;
            var from = HireRunner.PlanFrom(v, van.X, van.Z);
            runner.EnsurePlan(v, t, from.X, from.Z);
            for (var guard = 0; guard < 4; guard++)
            {
                var action = runner.Step(v, t, new VehicleWhere(van.X, van.Z, van.Property,
                    hasStock: Stock(v).Values.Any(n => n > 0), stayPut: Awake && atWindow == v.VehicleId));
                if (action.Kind == HireActionKind.None) break;
                Note($"{v.VehicleId}: route {action.Kind} stop {action.Stop}");
                switch (action.Kind)
                {
                    case HireActionKind.Close: Close(v); break;
                    case HireActionKind.Depart: Close(v); HireRunner.Depart(v, action, t, van.X, 0, van.Z, 0); van.Bay = null; break;
                    case HireActionKind.Arrive:
                        var transit = v.Transit!;
                        var fits = random.Next(10) != 0;
                        van.X = fits ? transit.ToX + (v.VehicleType == "truck" ? 35 : 0) : transit.FromX;
                        van.Z = fits ? transit.ToZ : transit.FromZ;
                        HireRunner.Arrived(v, van.X, van.Z, fits, t); break;
                    case HireActionKind.Open: Open(v, action.Stop, t); break;
                }
            }
            var next = until;
            foreach (var p in v.Shifts)
                foreach (var leg in p.Legs)
                { if (leg.Start > t) next = Math.Min(next, leg.Start); if (leg.End > t) next = Math.Min(next, leg.End); }
            if (v.Transit != null && v.Transit.ArrivesAt > t) next = Math.Min(next, v.Transit.ArrivesAt);
            if (saveAt > t) next = Math.Min(next, saveAt);
            Advance(v, t, next);
            t = next;
            now = t;
            if (t == saveAt) { SaveLoad(); v = state.Vehicles.Single(x => x.VehicleId == v.VehicleId); }
        }
        // Closed windows are sold-through too: they have no pending simulation.
        van.SoldThrough = until;
        now = hourStart;
    }

    private List<CargoLine> Lines(VehicleState v) => vans[v.VehicleId].Cargo.Select(l => new CargoLine(l.Item, l.Units, false)).ToList();
    private Dictionary<string, int> Stock(VehicleState v) => vans[v.VehicleId].Cargo
        .Where(l => ShippedData.Tables.Product(l.Item) != null).GroupBy(l => l.Item).ToDictionary(g => g.Key, g => g.Sum(l => l.Units));

    private void Open(VehicleState v, int? stop, double minute)
    {
        if (Licensing.CanOpen(state, v.VehicleId, Day, Awake && atWindow == v.VehicleId, v.Crew.Count) != OpenProblem.None) return;
        if (Menu.FromCargo(ShippedData.Tables, v.VehicleType, Lines(v)).Menu.PickNext(Stock(v)) == null) return;
        v.IsOpen = true;
        v.HireStop = stop;
        v.Service = SalesSimulator.Open(SeededRandom.SeedFor(v.VehicleId, Day) ^ seed);
        vans[v.VehicleId].SoldThrough = SalesClock.Start(minute);
        Note($"{v.VehicleId}: opened at {minute:F2}, hire stop={stop}");
    }

    private void Close(VehicleState v)
    {
        if (v.Service != null) SalesSimulator.Close(v.Service);
        v.Service = null; v.IsOpen = false; v.HireStop = null;
        if (atWindow == v.VehicleId) atWindow = null;
    }

    private void Advance(VehicleState v, long from, long until)
    {
        var van = vans[v.VehicleId];
        if (!v.IsOpen || v.Service == null) return;
        var player = Awake && atWindow == v.VehicleId && PlayerServing.Serves(v.VehicleType, true, 0, 8);
        var team = CrewPay.Team(v.Crew.Select(e => (e, 60.0)), player ? 80.0 : (double?)null, ShippedData.Tables.Vehicle(v.VehicleType)!.Staff);
        var serviceTeam = ShippedData.Rules.Service.Team(team.Skills);
        var input = new SalesInput { Menu = Menu.FromCargo(ShippedData.Tables, v.VehicleType, Lines(v)).Menu.Items,
            Stock = Stock(v), CustomersPerHour = 15 + (from / 60 % 5) * 8, Servers = team.Skills.Count,
            ServiceSpeed = serviceTeam.Speed, Raining = Day % 3 == 0, AddOnsPerCustomer = 0.2 };
        while (SalesClock.NextStep(van.SoldThrough, until, 60) is { } end)
        {
            var clock = van.SoldThrough;
            Require(end <= (Math.Floor(clock / 60) + 1) * 60, "sales clock crossed an hour");
            var startMinute = v.Service.Minute;
            var result = simulator.Advance(v.Service, input, SalesClock.Elapsed(clock, end));
            Require(result.Draws.Sum(p => p.Value) == result.Sales.Count, "draws differ from sold units");
            if (CrewPay.OpenHourDue(v, (long)clock)) foreach (var e in team.Crew) CrewPay.Served(state, e, v.VehicleId, (long)clock);
            foreach (var sale in result.Sales)
            {
                var soldAt = clock + sale.Minute - startMinute;
                Require(soldAt >= from && soldAt < until, "sale outside simulated interval");
                if (v.HireStop != null)
                {
                    var leg = HireRunner.Want(v, (long)soldAt);
                    Require(RouteSaleAllowed(v, (long)soldAt),
                        $"route sale outside stop hours; sold at {soldAt}, leg={leg?.Kind} stop={leg?.StopIndex} " +
                        $"[{leg?.Start},{leg?.End}), hire stop={v.HireStop}, " +
                        $"hours={v.Schedule!.Stops[v.HireStop.Value].OpenAt}-{v.Schedule.Stops[v.HireStop.Value].CloseAt}");
                    routeSales++;
                }
                var day = GameDays.SaleDay(soldAt, GameDays.DayOf(until));
                Require(day == (int)(soldAt / 1440) + 1, "sale booked on wrong calendar day");
                var price = 500L;
                StreetSales.Record(state, v.VehicleId, day, price, 1);
                var cost = Draw(v, sale.Item);
                SalesLedger.RecordCost(state, day, v.VehicleId, cost);
                Add(revenue, (v.VehicleId, day), price); Add(costs, (v.VehicleId, day), cost);
                Add(sold, sale.Item, 1);
                if (player) { Move(price, "sale", day, $"{v.VehicleId}:{++playerSales}"); }
                else
                {
                    Require(!paidCrew.Contains((v.VehicleId, day)), "sale arrived after its day was paid");
                    CrewTakings.Accrue(state, v.VehicleId, day, price);
                    crew.TryGetValue((v.VehicleId, day), out var total);
                    crew[(v.VehicleId, day)] = (total.Cents + price, total.Sales + 1);
                    crewSales++;
                }
            }
            van.SoldThrough += SalesClock.Elapsed(clock, end);
            if (result.SoldOut) { Close(v); break; }
            input.Stock = Stock(v);
        }
    }

    // HireRunner.Step keeps an already-open window at its stop during a replan's
    // approach leg. That leg is a planning artefact, not an actual departure.
    // Preserve the hours check: merely naming the same stop must not allow early sales.
    internal static bool RouteSaleAllowed(VehicleState v, long minute)
    {
        if (!v.IsOpen || v.HireStop == null || v.Transit != null) return false;
        var leg = HireRunner.Want(v, minute);
        if (leg == null || leg.StopIndex != v.HireStop) return false;
        if (leg.Kind == LegKind.Open) return true;
        var plan = v.Shifts.Single(p => p.Legs.Contains(leg));
        var opening = plan.Legs.FirstOrDefault(l => l.Kind == LegKind.Open &&
            l.StopIndex == v.HireStop && l.End > minute);
        if (opening == null) return false;
        var starts = plan.Day < GameDays.DayOf(minute) ? opening.Start :
            HireSchedules.Absolute(plan.Day, v.Schedule!.Stops[v.HireStop.Value].OpenAt);
        return minute >= starts && minute < opening.End;
    }

    private long Draw(VehicleState v, string item)
    {
        var cargo = vans[v.VehicleId].Cargo;
        var index = cargo.FindIndex(l => l.Item == item && l.Units > 0);
        Require(index >= 0, $"sale created stock: {item}");
        var lot = cargo[index];
        if (lot.Units == 1) cargo.RemoveAt(index); else cargo[index] = lot with { Units = lot.Units - 1 };
        return lot.Price;
    }

    private void SaveLoad()
    {
        var json = codec.SerializeForSave(state, "0.9.10");
        var loaded = codec.Load(json);
        Require(loaded.Status == LoadStatus.Loaded, "load failed: " + loaded.Error);
        Require(loaded.State!.SchemaVersion == SaveState.CurrentSchemaVersion, "save version is not current");
        Require(JToken.DeepEquals(JToken.Parse(json), JToken.Parse(codec.Serialize(loaded.State))), "save/load changed state");
        state = loaded.State; // Never keep a pre-load Core reference; game-owned cargo/pose/balance survives separately.
        saves++;
        Note("save/load; continue with loaded Core state");
    }

    private void CountRouteHours(long hour)
    {
        CrewPay.Prune(state, hour);
        foreach (var v in state.Vehicles)
        {
            var van = vans[v.VehicleId]; var from = HireRunner.PlanFrom(v, van.X, van.Z);
            if (runner.PlannedShiftMinutes(v, now, now + 60, from.X, from.Z) < 30) continue;
            foreach (var e in v.Crew) if (CrewPay.TryPay(state, e, hour)) Credit(e, v.VehicleId, hour);
        }
    }

    private bool Credit(string e, string vehicle, long hour)
    {
        Require(creditedHours.Add((e, hour)), "employee credited twice for an hour");
        CrewLog.AddHour(state, (int)(hour / 24) + 1, e, vehicle);
        return true;
    }

    private void CloseHours(long before) => CrewPay.CloseHours(state, before, Credit);

    private void Payroll(int day)
    {
        foreach (var e in state.CrewHours.Where(h => h.Day == day).Select(h => h.EmployeeId).Distinct().ToList())
        {
            var hours = state.CrewHours.Where(h => h.Day == day && h.EmployeeId == e).Sum(h => h.Hours);
            var cents = hours * 1200L;
            var split = CrewLog.Attribute(state, day, e, cents, hours, null);
            Require(split.Sum(x => x.Cents) == cents, "wage attribution lost money");
            Move(-cents, "wage", day, $"{e}:payroll:{day}");
            foreach (var share in split) SalesLedger.RecordGameCost(state, day, share.VehicleId, LedgerLine.Wages, share.Cents);
            CrewLog.Consume(state, day, e);
        }
    }

    private void SettleFired(string e)
    {
        var owed = CrewPay.Owed(state, e, Day);
        if (owed.Hours > 0)
        {
            Move(-owed.Hours * 1200L, "wage", Day, $"{e}:fire:{now}");
            foreach (var (vehicle, hours) in owed.ByVehicle) SalesLedger.RecordGameCost(state, Day, vehicle, LedgerLine.Wages, hours * 1200L);
        }
        CrewPay.Settled(state, e, Day, owed);
    }

    private void PayTakings(bool all)
    {
        var due = all ? CrewTakings.All(state) : CrewTakings.Due(state, Day, id => vans[id].SoldThrough);
        foreach (var t in due)
        {
            var key = (t.VehicleId, t.Day);
            Require(crew.TryGetValue(key, out var expected) && expected == (t.Cents, t.Sales), "taking differs from crew sales");
            Require(all || (now == t.Day * 1440L), "taking not paid at the midnight after its day");
            Require(paidCrew.Add(key), "crew taking paid twice");
            Move(t.Cents, "crew payout", t.Day, $"{t.VehicleId}:{t.Day}");
            Require(CrewTakings.Settled(state, t), "settlement did not remove taking");
        }
        Require(all ? state.CrewTakings.Count == 0 : state.CrewTakings.All(t => t.Day >= Day), "old crew taking left unpaid");
    }

    private void CheckLedger(int day)
    {
        var expected = revenue.Where(p => p.Key.Item2 == day).Sum(p => p.Value);
        Require(SalesLedger.SumCents(state, day, day) == expected, "tax ledger booked revenue on wrong day");
        foreach (var v in state.Vehicles)
        {
            var line = state.DailySales.SingleOrDefault(d => d.Day == day)?.ByVehicle.SingleOrDefault(x => x.VehicleId == v.VehicleId);
            revenue.TryGetValue((v.VehicleId, day), out var r); costs.TryGetValue((v.VehicleId, day), out var c);
            Require((line?.SalesCents ?? 0) == r && (line?.StockCents ?? 0) == c, "per-vehicle ledger differs from sales/cost oracle");
        }
        var first = (day - 1) / 28 * 28 + 1;
        Require(SalesLedger.SumCents(state, first, day) == revenue.Where(p => p.Key.Item2 >= first && p.Key.Item2 <= day).Sum(p => p.Value), "tax period total differs");
    }

    private void CheckWorld()
    {
        Require(money - OpeningMoney == book.Sum(e => e.Cents), "cash balance differs from transaction book");
        foreach (var v in state.Vehicles)
        {
            Require(vans[v.VehicleId].Cargo.All(l => l.Units > 0), "cargo not positive");
            var view = Storage.Of(ShippedData.Tables, v.VehicleType, Lines(v))!;
            Require(view.Pools.All(p => p.Load <= p.Capacity + 1e-9), "storage pool over capacity");
            Require(Menu.FromCargo(ShippedData.Tables, v.VehicleType, Lines(v)).Problem == MenuProblem.None, "invalid fitted pieces");
            var plan = schedules.PlanDay(v.Schedule!, Day, GameDays.Weekday(Day), 0, 0, now, Travel.ModeFor(v.VehicleType));
            if (schedules.Validate(v.Schedule!) != ScheduleProblem.None) Require(plan.Legs.Count == 0, "invalid schedule has a plan");
            foreach (var p in v.Shifts)
            {
                long end = long.MinValue;
                foreach (var leg in p.Legs)
                {
                    Require(leg.Start >= end && leg.End >= leg.Start, "planned legs overlap"); end = leg.End;
                    if (leg.Kind == LegKind.Open)
                    {
                        var s = v.Schedule!.Stops[leg.StopIndex];
                        Require(leg.End - leg.Start <= 1440 && (p.Day < Day ||
                            (leg.Start >= (p.Day - 1) * 1440L + s.OpenAt && leg.End <= (p.Day - 1) * 1440L + s.CloseAt)),
                            "planned stop outside hours or longer than a day");
                    }
                }
            }
            Require(v.LifetimeRevenueCents == revenue.Where(p => p.Key.Item1 == v.VehicleId).Sum(p => p.Value), "lifetime revenue differs");
        }
        CheckExpenses();
        foreach (var t in state.CrewTakings)
            Require(!paidCrew.Contains((t.VehicleId, t.Day)) && crew[(t.VehicleId, t.Day)] == (t.Cents, t.Sales), "waiting takings differ");
        CheckInventory();
        CheckLedger(Math.Max(1, Day - (Minute == 0 ? 1 : 0)));
    }

    private static void Add<K>(Dictionary<K, long> dict, K key, long value) where K : notnull
    { dict.TryGetValue(key, out var old); dict[key] = old + value; }
    private void Note(string action)
    { actions.Enqueue($"day {Day} {Minute / 60:D2}:{Minute % 60:D2}: {action}"); while (actions.Count > 20) actions.Dequeue(); }
    private static void Require(bool condition, string message) { if (!condition) throw new XunitException(message); }
}
