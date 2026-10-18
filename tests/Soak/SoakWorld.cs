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
    private readonly Random lagRandom;
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
    private int saves, crewSales, playerSales, routeSales, slept, invalidPlans, crewRestocks, standingBooked;
    // The drive back to the commissary bay: trips home, those that left after their planned leg (delayed by the player
    // holding the vehicle, a replan or a gap in the shifts), shift edits at midnight, and the nights each vehicle has
    // owed the drive back with a fully staffed route.
    private int homeTrips, delayedHome, midnightShiftEdits;
    private readonly Dictionary<string, int> owedNights = new();
    // Vehicles that had a HomeChance since the last midnight.
    private readonly HashSet<string> homeChance = new();

    // Each commissary's bay in the soak's world.
    private static (double X, double Z) BayAt(string site) => site == "a" ? (0, 0) : (600, 0);

    // The bay of the commissary where the vehicle rents its space: the route day starts and ends there.
    private (double X, double Z)? HomeOf(VehicleState v)
    {
        var site = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == v.VehicleId)?.CommissaryId;
        return site == null ? null : BayAt(site);
    }
    private readonly Dictionary<(string, int), long> revenue = new();
    private readonly Dictionary<(string, int), long> costs = new();
    private readonly Dictionary<(string, int), (long Cents, int Sales)> takingsOracle = new();
    private readonly HashSet<(string, int)> paidTakings = new();
    private readonly HashSet<(string, long)> creditedHours = new();
    private readonly Dictionary<string, (long From, long Until)> deferred = new();
    private readonly Dictionary<(string, int), int> delayedPayDay = new();
    private int heldAtMidnight, lateCrewSales, delayedPayouts;

    private sealed class Van
    {
        public double X, Z;
        public string? Bay;
        public bool Property;
        public double SoldThrough;
        public long RoutedThrough;
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
        lagRandom = new Random(seed ^ 0x51A7);
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
            Staff(v);
            Rent(v, "a");
        }
        state.Licence.HasVendorLicence = true;
        state.Licence.LicenceExpiresOnDay = int.MaxValue;
        AddUnits(initial, Inventory());
        initialValue = vans.Values.SelectMany(v => v.Cargo).Sum(l => l.Units * l.Price);
        Note("world created; initial cargo is an opening asset balance");
    }

    public string Coverage => $"saves={saves}, crew/player/route sales={crewSales}/{playerSales}/{routeSales}, " +
        $"home trips/delayed/midnight shift edits={homeTrips}/{delayedHome}/{midnightShiftEdits}, " +
        $"paid/refused deliveries={delivered.Count}/{refused.Count}, invalid plans={invalidPlans}, sleep days={slept}, crew restocks={crewRestocks}, standing bookings={standingBooked}, " +
        $"midnight holds/late crew sales/delayed payouts={heldAtMidnight}/{lateCrewSales}/{delayedPayouts}";

    public void Run(int days)
    {
        try
        {
            for (var hour = 0; hour < days * 24; hour++)
            {
                now = hour * 60L;
                // Normally the preceding hour is complete; injected midnight lag catches up
                // only after the closing day's payout attempt, before new player actions.
                CloseHours(hour);
                if (hour > 0 && hour % 24 == 0)
                {
                    Payroll(Day - 1);
                    PayTakings(false);
                    CatchUpDeferred();
                    CheckLedger(Day - 1);
                    SalesLedger.MarkInSummary(state, Day - 1);
                }
                CountRouteHours(hour);
                if (hour % 24 == 0 && hour > 0) { MidnightShiftEdit(); CheckOwedReturns(); }
                CommissaryTick();
                if (Awake) PlayerAction();
                var boundary = now + (hour == days * 24 - 1 ? random.Next(1, 60) : 60);
                foreach (var id in vans.Keys)
                {
                    var v = state.Vehicles.Single(v => v.VehicleId == id);
                    if (hour % 24 == 23 && hour < (days - 2) * 24 && lagRandom.Next(3) == 0)
                    {
                        var start = now;
                        RunVehicle(v, boundary - 15);
                        v = state.Vehicles.Single(v => v.VehicleId == id); // a save may replace it
                        if (v.IsOpen && v.Crew.Count > 0 && !(Awake && atWindow == id) &&
                            state.CrewTakings.Any(t => t.VehicleId == id && t.Day == Day && t.Sales > 0))
                            DeferLastMinutes(id, boundary - 15, boundary);
                        else RunVehicle(v, boundary, boundary - 15);
                        now = start;
                    }
                    else RunVehicle(v, boundary);
                }
                now = boundary;
                CheckWorld();
            }
            // Stop at a random minute of the last day: uninstall must pay today's waiting sales too.
            Uninstall();
            Require(heldAtMidnight == delayedPayDay.Count && delayedPayouts == delayedPayDay.Count,
                "a deferred day was not held and then paid exactly once at a later midnight");
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
                if (v.Transit == null && !v.IsOpen) { van.Bay = random.Next(2) == 0 ? "a" : "b"; (van.X, van.Z) = BayAt(van.Bay); van.Property = false; Restock(v, van.Bay); }
                break;
            case 2: if (v.Transit == null && !v.IsOpen) { atWindow = v.VehicleId; Open(v, null, now + random.NextDouble()); } break;
            case 3: Close(v); break;
            // At the window, or standing by a closed vehicle (the crew leaves it where it is: a delayed drive).
            case 4: if (v.IsOpen || v.Transit == null) atWindow = v.VehicleId; break;
            case 5: atWindow = null; break;
            case 6: ChangeRoute(v); break;
            case 7:
                if (v.Crew.Count < ShippedData.Tables.Vehicle(v.VehicleType)!.Staff)
                { v.Crew.Add($"e{nextEmployee++}"); Staff(v); HireRunner.ReplanShifts(v, now); }
                break;
            case 8:
                if (v.Crew.Count > 0)
                {
                    var id = v.Crew[0];
                    SettleFired(id);
                    v.Crew.RemoveAt(0);
                    WeekPlan.DropCrew(v.Schedule, id);
                    if (v.Crew.Count == 0 && v.HireStop != null) Close(v);
                    HireRunner.ReplanShifts(v, now);
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

    // One day's stops, on the weekdays the kind runs (every day for 0 and 1).
    private HireSchedule NewRoute(int kind, Action<List<ScheduleStop>>? change = null)
    {
        ScheduleStop Stop(string name, int open, int close) => new() { SpotKey = name, X = random.Next(0, 500),
            Z = random.Next(0, 100), OpenAt = open, CloseAt = close };
        var day = new List<ScheduleStop>();
        var days = new List<DayOfWeek>();
        if (kind == 1) day.Add(Stop("night", 23 * 60 + 30, 26 * 60 + 30));
        else if (kind == 2) { day.Add(Stop("early", 30, 120)); day.Add(Stop("night", 1380, 1590)); days.Add(DayOfWeek.Friday); }
        else { day.Add(Stop("lunch", 660 + random.Next(2) * 30, 810)); day.Add(Stop("evening", 1080, 1200)); }
        if (kind == 3) days = new() { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday };
        change?.Invoke(day);
        return new HireSchedule { Stops = FoodTrucks.Core.Tests.Fleet.WeekTestData.On(day, days.ToArray()) };
    }

    // The crew's shifts from the auto schedule; now and then one shift is left out, so the route waits for a shift.
    private void Staff(VehicleState v)
    {
        if (v.Schedule == null) return;
        var days = WeekPlan.RouteDays(schedules, v.Schedule, Travel.ModeFor(v.VehicleType), home: HomeOf(v));
        v.Schedule.CrewShifts = WeekPlan.AutoSchedule(ShippedData.Rules.Fleet, days, v.Crew.Select(e => new CrewWish(e)).ToList(),
            ShippedData.Tables.Vehicle(v.VehicleType)!.Staff, v.Schedule.CrewShifts);
        if (v.Schedule.CrewShifts.Count > 0 && random.Next(4) == 0) v.Schedule.CrewShifts.RemoveAt(random.Next(v.Schedule.CrewShifts.Count));
    }

    private void ChangeRoute(VehicleState v)
    {
        if (v.Schedule == null || v.Schedule.Stops.Count == 0) { v.Schedule = NewRoute(random.Next(4)); Staff(v); }
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
            var day = WeekPlan.Days[random.Next(7)];
            before = codec.Serialize(state);
            problem = RouteEditor.SetClosed(schedules, v, day, !WeekPlan.IsClosed(v.Schedule, day));
            if (problem != ScheduleProblem.None) Require(before == codec.Serialize(state), "refused closed-day edit mutated state");
            else HireRunner.Replan(v, now);
        }
        if (random.Next(6) == 0 && v.Schedule != null)
        {
            Staff(v);
            HireRunner.Replan(v, now);
        }
        // Malformed older saves enter through public state. Probe a copy, not a live edited route.
        var probe = random.Next(4);
        var bad = new VehicleState { VehicleId = "old", VehicleType = v.VehicleType, Crew = new() { "probe" }, Schedule = NewRoute(0, stops =>
        {
            switch (probe)
            {
                case 0: stops[0].CloseAt = stops[0].OpenAt + 1441; break;
                case 1: stops[1].OpenAt = stops[0].CloseAt - 30; break;
                case 2: stops[0].OpenAt = -30; break;
                case 3:
                    stops[0].OpenAt = 30; stops[0].CloseAt = 120;
                    stops[1].OpenAt = 1380; stops[1].CloseAt = 1590;
                    break;
            }
        }) };
        FoodTrucks.Core.Tests.Fleet.WeekTestData.AllDay(bad);
        // SaveCodec deliberately preserves structurally sound old routes; HireRunner refuses their plans.
        var old = codec.Load(codec.Serialize(new SaveState { Vehicles = new() { bad } }));
        Require(old.Status == LoadStatus.Loaded, "old invalid route could not load");
        bad = old.State!.Vehicles[0];
        Require(runner.RouteProblem(bad) != ScheduleProblem.None, "invalid old route accepted");
        runner.EnsurePlan(bad, now, 0, 0);
        Require(bad.Shifts.Count == 0, "invalid old route received a plan");
        invalidPlans++;
    }

    private void RunVehicle(VehicleState v, long until, long? fromMinute = null)
    {
        var van = vans[v.VehicleId];
        var hourStart = now;
        var t = fromMinute ?? now;
        var saveAt = random.Next(96) == 0 ? now + random.Next(1, 60) : -1;
        while (t < until)
        {
            now = t;
            var from = HireRunner.PlanFrom(v, van.X, van.Z);
            var home = HomeOf(v);
            runner.EnsurePlan(v, t, from.X, from.Z, home: home);
            var homeNow = HomeChance(v, t, home);
            for (var guard = 0; guard < 4; guard++)
            {
                // In the bay of its own commissary space with stock there: the crew may restock (CrewRestock).
                var ownBay = van.Bay != null && state.Commissary.Spaces.Any(s => s.VehicleId == v.VehicleId && s.CommissaryId == van.Bay);
                var action = runner.Step(v, t, new VehicleWhere(van.X, van.Z, van.Property,
                    hasStock: Stock(v).Values.Any(n => n > 0), stayPut: Awake && atWindow == v.VehicleId,
                    canRestock: ownBay, stockKey: ownBay ? CrewRestock.StockKey(ShippedData.Tables, v.VehicleType, Lines(v), Available(van.Bay!)) : null,
                    home: home));
                // Free to drive back, owing it, with crew on shift for the whole drive: it goes now.
                if (guard == 0 && homeNow)
                    Require(action.Kind == HireActionKind.Depart && action.Stop == HireSchedules.HomeIndex,
                        $"an owed drive back didn't leave with crew on shift for all of it: {v.VehicleId} got {action.Kind} {action.Stop}");
                if (action.Kind == HireActionKind.None) break;
                Note($"{v.VehicleId}: route {action.Kind} stop {action.Stop}");
                switch (action.Kind)
                {
                    case HireActionKind.Close: Close(v); break;
                    case HireActionKind.Depart:
                        // A drive only with someone on shift all the way (review round 1).
                        Require(WeekPlan.CoverageOf(v.Schedule, v.Crew, t - 1440, action.ArrivesAt + 1440).Earliest(t, action.ArrivesAt - t, action.ArrivesAt) == t,
                            "a drive left without crew on shift for all of it");
                        var toHome = action.Stop == HireSchedules.HomeIndex;
                        if (toHome)
                        {
                            homeTrips++;
                            if (HireRunner.Want(v, t)?.StopIndex != HireSchedules.HomeIndex) delayedHome++;
                        }
                        Close(v);
                        HireRunner.Depart(v, action, t, van.X, 0, van.Z, 0,
                            toHome ? new ScheduleStop { SpotKey = HireSchedules.HomeSpotKey, X = home!.Value.X, Z = home.Value.Z } : null);
                        van.Bay = null; break;
                    case HireActionKind.Arrive:
                        var transit = v.Transit!;
                        var fits = random.Next(10) != 0;
                        var homeTrip = transit.ToSpotKey == HireSchedules.HomeSpotKey;
                        van.X = fits ? transit.ToX + (v.VehicleType == "truck" && !homeTrip ? 35 : 0) : transit.FromX;
                        van.Z = fits ? transit.ToZ : transit.FromZ;
                        // Back in the bay of its space (if it still rents one there).
                        if (fits && homeTrip) van.Bay = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == v.VehicleId)?.CommissaryId;
                        HireRunner.Arrived(v, van.X, van.Z, fits, t); break;
                    case HireActionKind.Open:
                        Require(WeekPlan.OnShiftCrew(v, t).Count > 0, "the crew opened with nobody on shift");
                        Open(v, action.Stop, t); break;
                    case HireActionKind.Restock:
                        Restock(v, van.Bay!);
                        HireRunner.Restocked(v, t, CrewRestock.StockKey(ShippedData.Tables, v.VehicleType, Lines(v), Available(van.Bay!)));
                        crewRestocks++;
                        break;
                }
            }
            // A crew window never stays open with nobody on shift (the step just closed it otherwise).
            Require(!(v.IsOpen && v.HireStop != null) || WeekPlan.OnShiftCrew(v, t).Count > 0 || (van.Property && !v.IsOpen),
                "a crew window stayed open with nobody on shift");
            var next = until;
            foreach (var p in v.Shifts)
                foreach (var leg in p.Legs)
                { if (leg.Start > t) next = Math.Min(next, leg.Start); if (leg.End > t) next = Math.Min(next, leg.End); }
            if (v.Transit != null && v.Transit.ArrivesAt > t) next = Math.Min(next, v.Transit.ArrivesAt);
            if (saveAt > t) next = Math.Min(next, saveAt);
            Advance(v, t, next, GameDays.DayOf(hourStart));
            t = next;
            now = t;
            if (t == saveAt) { SaveLoad(); v = state.Vehicles.Single(x => x.VehicleId == v.VehicleId); }
        }
        // The route cursor and the open window's sales clock are separate, as in
        // CartSelling.SoldThroughOf. Only Advance moves the open window's clock.
        van.RoutedThrough = until;
        now = hourStart;
    }

    // Whether the vehicle stands idle away from its bay owing the drive back with nothing planned, free to go (no skip,
    // not held by the player) and with crew on shift for the whole drive from now; noted for CheckOwedReturns.
    private bool HomeChance(VehicleState v, long t, (double X, double Z)? home)
    {
        var van = vans[v.VehicleId];
        if (home == null || !v.ReturnPending || v.Transit != null || v.IsOpen || van.Property || (Awake && atWindow == v.VehicleId) ||
            v.Crew.Count == 0 || v.Shifts.Any(p => p.Legs.Any(l => l.End > t)) ||
            (v.Skipped != null && v.Skipped.Until > t) ||
            (van.Bay != null && state.Commissary.Spaces.Any(s => s.VehicleId == v.VehicleId && s.CommissaryId == van.Bay)))
            return false;
        var drive = Math.Max(1, Travel.Between(ShippedData.Rules.Fleet, van.X, van.Z, home.Value.X, home.Value.Z, Travel.ModeFor(v.VehicleType)));
        if (WeekPlan.CoverageOf(v.Schedule, v.Crew, t - 1440, t + drive + 1440).Earliest(t, drive, t + drive) != t) return false;
        homeChance.Add(v.VehicleId);
        return true;
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

    private void Advance(VehicleState v, long from, long until, int liveDay)
    {
        var van = vans[v.VehicleId];
        if (!v.IsOpen || v.Service == null) return;
        var player = Awake && atWindow == v.VehicleId && PlayerServing.Serves(v.VehicleType, true, 0, 8);
        // Only the crew on shift serve.
        var team = CrewPay.Team(WeekPlan.OnShiftCrew(v, from).Select(e => (e, 60.0)), player ? 80.0 : (double?)null, ShippedData.Tables.Vehicle(v.VehicleType)!.Staff,
            playerHeld: v.VehicleType == "truck");
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
                var day = GameDays.SaleDay(soldAt, liveDay);
                Require(day == (int)(soldAt / 1440) + 1, "sale booked on wrong calendar day");
                var price = 500L;
                StreetSales.Record(state, v.VehicleId, day, price, 1);
                var cost = Draw(v, sale.Item);
                SalesLedger.RecordCost(state, day, v.VehicleId, cost);
                Add(revenue, (v.VehicleId, day), price); Add(costs, (v.VehicleId, day), cost);
                Add(sold, sale.Item, 1);
                // Every window sale waits for midnight, including the player's. Only the coverage counters differ.
                Require(!paidTakings.Contains((v.VehicleId, day)), "sale arrived after its day was paid");
                if (day < liveDay) lateCrewSales++;
                CrewTakings.Accrue(state, v.VehicleId, day, price);
                takingsOracle.TryGetValue((v.VehicleId, day), out var total);
                takingsOracle[(v.VehicleId, day)] = (total.Cents + price, total.Sales + 1);
                if (player) playerSales++; else crewSales++;
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
        // The stop's hours on the plan's own day, yesterday's for a late stop replanned after midnight (soak seed 35:
        // the shifts scheduled again at 00:00 put a restock leg before a bay stop open since 23:30).
        var starts = HireSchedules.Absolute(plan.Day, v.Schedule!.Stops[v.HireStop.Value].OpenAt);
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
            // Each crew member is paid every hour of their own shift (the week plan), worked or idle.
            foreach (var e in v.Crew)
                if (WeekPlan.OnShift(v.Schedule, e, hour * 60) && CrewPay.TryPay(state, e, hour)) Credit(e, v.VehicleId, hour);
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
        var due = all ? CrewTakings.All(state) : CrewTakings.Due(state, Day, SoldThroughOf);
        if (!all)
        {
            // Payment deadlines are an independent oracle, set when lag is injected;
            // do not derive the expected due set from Core's sold-through filter.
            var expected = takingsOracle.Keys.Where(k => !paidTakings.Contains(k) && k.Item2 < Day &&
                PaymentDay(k) <= Day).OrderBy(k => k.Item2).ThenBy(k => k.Item1, StringComparer.Ordinal);
            Require(expected.SequenceEqual(due.Select(t => (t.VehicleId, t.Day))),
                "sold-through gate paid a held day or withheld a completed day");
            foreach (var key in delayedPayDay.Keys.Where(k => k.Item2 == Day - 1))
            {
                var taking = state.CrewTakings.Single(t => t.VehicleId == key.Item1 && t.Day == key.Item2);
                Require(deferred.ContainsKey(key.Item1) && SoldThroughOf(key.Item1) < key.Item2 * 1440L &&
                    !paidTakings.Contains(key) && takingsOracle[key] == (taking.Cents, taking.Sales),
                    "the incomplete day must retain its entire taking at the first midnight");
                heldAtMidnight++;
                Note($"{key.Item1}: day {key.Item2} held at midnight; sold through {SoldThroughOf(key.Item1)}");
            }
        }
        foreach (var t in due)
        {
            var key = (t.VehicleId, t.Day);
            Require(takingsOracle.TryGetValue(key, out var expected) && expected == (t.Cents, t.Sales), "taking differs from window sales");
            Require(all || now == (PaymentDay(key) - 1) * 1440L,
                "taking not paid at its expected midnight after catch-up");
            Require(paidTakings.Add(key), "window taking paid twice");
            if (delayedPayDay.ContainsKey(key))
            {
                Require(!all, "a deferred day reached uninstall before its later midnight payout");
                delayedPayouts++;
                Note($"{t.VehicleId}: held day {t.Day} paid once at midnight on day {Day}");
            }
            Move(t.Cents, "sales payout", t.Day, $"{t.VehicleId}:{t.Day}");
            Require(CrewTakings.Settled(state, t), "settlement did not remove taking");
        }
        Require(all ? state.CrewTakings.Count == 0 : state.CrewTakings.All(t => t.Day >= Day ||
            delayedPayDay.TryGetValue((t.VehicleId, t.Day), out var day) && day > Day), "old window taking left unpaid");
    }

    private int PaymentDay((string, int) key) => delayedPayDay.TryGetValue(key, out var day) ? day : key.Item2 + 1;

    private double SoldThroughOf(string id)
    {
        var v = state.Vehicles.Single(v => v.VehicleId == id);
        var van = vans[id];
        return Math.Min(v.IsOpen ? van.SoldThrough : double.PositiveInfinity, van.RoutedThrough);
    }

    private void DeferLastMinutes(string id, long from, long until)
    {
        var day = GameDays.DayOf(from);
        Require(until == day * 1440L && SoldThroughOf(id) == from, "lag injection did not leave a genuine incomplete clock");
        deferred.Add(id, (from, until));
        delayedPayDay.Add((id, day), day + 2);
        Note($"{id}: defer 23:45-midnight of day {day} until after its midnight payout attempt");
    }

    private void CatchUpDeferred()
    {
        foreach (var (id, lag) in deferred.ToList())
        {
            Require(now == lag.Until && !paidTakings.Contains((id, Day - 1)), "deferred day was paid before late sales");
            Note($"{id}: catch up the last 15 minutes after midnight, retaining original sale day");
            RunVehicle(state.Vehicles.Single(v => v.VehicleId == id), lag.Until, lag.From);
            Require(SoldThroughOf(id) >= lag.Until, "late catch-up failed to pass the old day's boundary");
            var taking = state.CrewTakings.Single(t => t.VehicleId == id && t.Day == Day - 1);
            Require(takingsOracle[(id, taking.Day)] == (taking.Cents, taking.Sales), "late sales missing from the held day's taking");
            deferred.Remove(id);
        }
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

    // At midnight now and then: a vehicle's shifts are scheduled again (the night's coverage changes while a late stop or
    // a drive back may be running); every unfinished plan is planned again.
    private void MidnightShiftEdit()
    {
        if (random.Next(3) != 0) return;
        var v = state.Vehicles[random.Next(state.Vehicles.Count)];
        if (v.Schedule == null) return;
        Staff(v);
        HireRunner.ReplanShifts(v, now);
        midnightShiftEdits++;
        Note($"{v.VehicleId}: shifts scheduled again at midnight");
    }

    // A vehicle whose route is fully staffed doesn't owe the drive back for more than two nights in a row (the player
    // standing by it, its own building and a route with problems aside). A crew with no shifts in the week can't drive it
    // (the long soak's seed 6, day 273): the vehicle waits for the player, as in the game.
    private void CheckOwedReturns()
    {
        foreach (var v in state.Vehicles)
        {
            var van = vans[v.VehicleId];
            var home = HomeOf(v);
            var owed = v.ReturnPending && v.Transit == null && !v.IsOpen && home != null && !van.Property && atWindow != v.VehicleId &&
                       v.Crew.Count > 0 && (v.Schedule?.CrewShifts.Count ?? 0) > 0 && runner.RouteProblems(v, 1.0, home).Count == 0;
            var chance = homeChance.Remove(v.VehicleId);
            // Out at a late stop or with stops still ahead at midnight isn't owing yet; that night isn't counted.
            var busy = HireRunner.Want(v, now) != null || v.Shifts.Any(p => p.Legs.Any(l => l.StopIndex != HireSchedules.HomeIndex && l.End > now));
            if (owed && busy) continue;
            // A night counts only when the crew had a free chance to drive back during the day before it (homeChance):
            // shifts with days off, the player keeping the vehicle, or a failed arrival whose retry falls after the
            // shift's end leave none, and the runner rightly never drives without crew on shift for the whole drive.
            if (owed && !chance) continue;
            owedNights[v.VehicleId] = owed ? owedNights.GetValueOrDefault(v.VehicleId) + 1 : 0;
            Require(owedNights[v.VehicleId] <= 2, $"a fully staffed vehicle owed the drive back for three nights: {v.VehicleId} at x {van.X}, z {van.Z}, bay {van.Bay}, home {home}, " +
                $"arrival {v.Arrival?.SpotKey}, skipped {v.Skipped?.SpotKey} until {v.Skipped?.Until}, legs {string.Join("; ", v.Shifts.SelectMany(p => p.Legs).Select(l => $"{l.Kind} {l.StopIndex} {l.Start}-{l.End}"))}, " +
                $"shifts {string.Join(", ", v.Schedule?.CrewShifts.Select(c => $"{c.EmployeeId} {c.Day} {c.Start}-{c.End}") ?? new string[0])}, crew {string.Join(",", v.Crew)}, stops {string.Join(", ", v.Schedule?.Stops.Select(x => $"{x.Day} {x.OpenAt}-{x.CloseAt} @{x.X}") ?? new string[0])}");
        }
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
            Require(!paidTakings.Contains((t.VehicleId, t.Day)) && takingsOracle[(t.VehicleId, t.Day)] == (t.Cents, t.Sales), "waiting takings differ");
        CheckInventory();
        CheckLedger(Math.Max(1, Day - (Minute == 0 ? 1 : 0)));
    }

    private static void Add<K>(Dictionary<K, long> dict, K key, long value) where K : notnull
    { dict.TryGetValue(key, out var old); dict[key] = old + value; }
    private void Note(string action)
    { actions.Enqueue($"day {Day} {Minute / 60:D2}:{Minute % 60:D2}: {action}"); while (actions.Count > 20) actions.Dequeue(); }
    private static void Require(bool condition, string message) { if (!condition) throw new XunitException(message); }
}
