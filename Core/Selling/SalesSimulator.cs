using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Selling
{
    // What the simulator needs for the minutes it runs. The adapter refreshes it from DemandModel
    // whenever the clock, weather or stock changes the estimate.
    public sealed class SalesInput
    {
        // Items the vehicle sells. Items without a product entry are ignored.
        public IReadOnlyCollection<string> Menu { get; set; } = Array.Empty<string>();
        // Units per item in the vehicle. The simulator never changes it; Draws in the result say what to take out.
        public IReadOnlyDictionary<string, int> Stock { get; set; } = new Dictionary<string, int>();
        // DemandEstimate.CustomersPerHour.
        public double CustomersPerHour { get; set; }
        // PriceCurve.Conversion at the vehicle's prices: the share who still buy.
        public double Conversion { get; set; } = 1.0;
        // DemandEstimate.CustomerShares. Empty means every stocked category is equally likely.
        public IReadOnlyDictionary<string, double>? CustomerShares { get; set; }
        // Per category, the only item its customers buy (the game side's best-accepted item at its price).
        // When set, a customer whose category has no entry, or whose item has run out, walks on: other
        // items may be priced beyond what they accept. Null: each category sells its item with the most units.
        public IReadOnlyDictionary<string, string>? CategoryItems { get; set; }
        // The only drink food buyers add, chosen the same way; none once it runs out. Null: the add-on
        // item with the most units.
        public string? AddOnItem { get; set; }
        // DemandEstimate.AddOnsPerCustomer, averaged over all customers.
        public double AddOnsPerCustomer { get; set; }
        // Workers serving on their own: hires, or the player at the window (the game side counts them as
        // one server). 0 when nobody serves, or when the player serves through ServeHead.
        public int Servers { get; set; }
        // Multiplies each worker's service speed: order time is ServiceMinutes / ServiceSpeed.
        // Non-finite or non-positive values use 1.0; speeds above 10 are capped.
        public double ServiceSpeed { get; set; } = 1.0;
        // Waiting customers, excluding orders at the window. 0 or less uses the global MaxQueue;
        // a positive limit is capped at MaxQueue.
        public int QueueLimit { get; set; }
        public bool Raining { get; set; }
    }

    public enum CustomerExit
    {
        Served,
        // Waited longer than their patience.
        GaveUp,
        // Found the queue full and walked on.
        QueueFull,
        // Wanted something the vehicle no longer has.
        NothingLeft,
        // The vehicle closed or sold out with them still waiting.
        Closed
    }

    public readonly struct Sale
    {
        internal Sale(long customerId, string item, bool addOn, long minute)
        {
            CustomerId = customerId;
            Item = item;
            AddOn = addOn;
            Minute = minute;
        }

        public long CustomerId { get; }
        public string Item { get; }
        // A drink added to another order: same customer, no service time of its own.
        public bool AddOn { get; }
        public long Minute { get; }
    }

    public sealed class SalesResult
    {
        private readonly List<Sale> sales = new List<Sale>();
        private readonly List<long> approached = new List<long>();
        private readonly List<long> joined = new List<long>();
        private readonly List<(long Id, CustomerExit Exit)> left = new List<(long, CustomerExit)>();
        private readonly Dictionary<string, int> draws = new Dictionary<string, int>(StringComparer.Ordinal);

        // Every item sold, add-ons included, in the order they were sold.
        public IReadOnlyList<Sale> Sales => sales;
        // Customers who turned up and started walking to the line, by ID, for the visible queue to spawn.
        public IReadOnlyList<long> Approached => approached;
        // Customers who reached the line and joined the queue, by ID.
        public IReadOnlyList<long> Joined => joined;
        // Customers who left without buying, by ID where they had one (0 for those who never queued).
        public IReadOnlyList<(long Id, CustomerExit Exit)> Left => left;
        // Units to take out of the vehicle per item; the same as the sales counted by item.
        public IReadOnlyDictionary<string, int> Draws => draws;
        public int MinutesSimulated { get; internal set; }
        // Elapsed minutes above MaxMinutesPerCall: nothing simulated, as SalesTick does.
        public bool Skipped { get; internal set; }
        // Nothing on the menu is left; the queue has been sent away and the window should close.
        public bool SoldOut { get; internal set; }

        public int Count(CustomerExit exit) => exit == CustomerExit.Served
            ? sales.Count(s => !s.AddOn)
            : left.Count(l => l.Exit == exit);

        internal void Sell(Sale sale)
        {
            sales.Add(sale);
            draws.TryGetValue(sale.Item, out var units);
            draws[sale.Item] = units + 1;
        }

        internal void Approach(long id) => approached.Add(id);
        internal void Join(long id) => joined.Add(id);
        internal void Leave(long id, CustomerExit exit) => left.Add((id, exit));
    }

    // Milestone 3's per-minute sales tick: customers arrive at random from the demand rate, walk up to the
    // line for a few minutes (QueueRules.WalkIn*), join the queue, give up after about 20 minutes of
    // waiting there (sooner in rain), and are served one order per worker at the order's service time
    // divided by their service speed. Customers who reach a queue at its limit walk on. The sale happens
    // when the order is done, at the end of its service time.
    // Each sale draws one unit; a sold-out item can't be chosen and selling out everything sends the
    // queue away. The same tick drives the visible queue near the player and
    // the statistical sales elsewhere, so switching views changes nothing.
    // Deterministic: the random state lives in ServiceState, so a save and load replay the same minutes.
    // Replaces SalesTick and SalesRate's "customers above the cap are lost" on the game side since 0.4.0.
    public sealed class SalesSimulator
    {
        public const int MaxMinutesPerCall = 1440;
        private const double Epsilon = 1e-9;
        // A Poisson mean of 60 per minute is 3,600 customers an hour, far above any spot.
        private const double MaxArrivalsPerMinute = 60;

        private readonly DemandTables tables;
        private readonly QueueRules rules;

        public SalesSimulator(DemandTables tables, QueueRules rules)
        {
            this.tables = tables ?? throw new ArgumentNullException(nameof(tables));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        // A fresh service for a vehicle that just opened. SeededRandom.SeedFor(vehicleId, day) is a
        // good seed: reopening on another day gives other customers.
        public static ServiceState Open(long seed) => new ServiceState { RandomState = seed };

        // Runs the given minutes. The state changes in place; the result says what happened.
        public SalesResult Advance(ServiceState state, SalesInput input, int minutes)
        {
            Check(state, input);
            var result = new SalesResult();
            Prune(state, result);
            if (minutes <= 0) return result;
            if (minutes > MaxMinutesPerCall)
            {
                result.Skipped = true;
                return result;
            }

            var random = new SeededRandom(state.RandomState);
            var stock = StockOnMenu(input);
            for (var i = 0; i < minutes; i++)
            {
                if (result.SoldOut) break;
                if (TotalUnits(stock) == 0)
                {
                    SellOut(state, result);
                    break;
                }
                Minute(state, input, stock, random, result);
                result.MinutesSimulated++;
            }
            if (!result.SoldOut && TotalUnits(stock) == 0) SellOut(state, result);
            state.RandomState = random.State;
            return result;
        }

        // The player serves the customer at the head of the queue: the sale happens now, the player's
        // own interaction stands in for the service time. Empty result when nobody is waiting.
        public SalesResult ServeHead(ServiceState state, SalesInput input)
        {
            Check(state, input);
            var result = new SalesResult();
            Prune(state, result);
            var random = new SeededRandom(state.RandomState);
            var stock = StockOnMenu(input);
            GiveUp(state, input, result, state.Minute);
            var head = StartNext(state, input, stock, result);
            if (head != null) Complete(state, head, head.Item!, input, stock, random, result);
            if (TotalUnits(stock) == 0) SellOut(state, result);
            state.RandomState = random.State;
            return result;
        }

        // Everyone walking up, waiting or at the window leaves; the state can be dropped afterwards.
        public static SalesResult Close(ServiceState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new SalesResult();
            SendAway(state, result);
            return result;
        }

        private void Minute(ServiceState state, SalesInput input, Dictionary<string, int> stock, SeededRandom random, SalesResult result)
        {
            Arrive(state, input, stock, random, result);
            Join(state, input, result);
            // Rain that started since the last call shortens everyone's patience before the window opens.
            GiveUp(state, input, result, state.Minute);
            Serve(state, input, stock, random, result);
            // Selling the last unit closes the window before anyone's patience runs out this minute.
            if (TotalUnits(stock) == 0) SellOut(state, result);
            else GiveUp(state, input, result, state.Minute + 1);
            state.Minute++;
        }

        // New customers pick what they want and start walking to the line. Whether the queue is full is
        // checked when they reach it (Join), not here: counting the people still walking would hold places
        // for minutes and leave the window idle, and the queue they could see on arrival has moved on by the
        // time they get there. Checked on joining, a window serves as many as it did without the walk.
        private void Arrive(ServiceState state, SalesInput input, Dictionary<string, int> stock, SeededRandom random, SalesResult result)
        {
            var perHour = Usable(input.CustomersPerHour) * Usable(input.Conversion);
            var arrivals = random.Poisson(Math.Min(MaxArrivalsPerMinute, perHour / 60.0));
            for (var i = 0; i < arrivals; i++)
            {
                var category = PickCategory(input.CustomerShares, stock, random);
                if (category == null)
                {
                    result.Leave(0, CustomerExit.NothingLeft);
                    continue;
                }
                var spread = rules.PatienceSpread * (2 * random.NextDouble() - 1);
                var walk = rules.WalkInMinMinutes;
                if (rules.WalkInMaxMinutes > walk)
                    walk += Math.Min(rules.WalkInMaxMinutes - walk, (int)Math.Floor(random.NextDouble() * (rules.WalkInMaxMinutes - walk + 1)));
                var customer = new ServiceCustomer
                {
                    Id = state.NextCustomerId++,
                    ArrivedAtMinute = state.Minute,
                    JoinsAtMinute = state.Minute + walk,
                    Patience = rules.PatienceMinutes * (1 + spread),
                    Category = category
                };
                state.Approaching.Add(customer);
                result.Approach(customer.Id);
            }
        }

        // Customers whose walk is over join the back of the queue, the earliest to arrive at the line first
        // (ties by ID, the order they turned up). Their patience counts from now. A full queue sends them on.
        private void Join(ServiceState state, SalesInput input, SalesResult result)
        {
            if (state.Approaching.Count == 0) return;
            var queueLimit = input.QueueLimit <= 0 ? rules.MaxQueue : Math.Min(input.QueueLimit, rules.MaxQueue);
            var due = state.Approaching.Where(c => c.JoinsAtMinute <= state.Minute)
                .OrderBy(c => c.JoinsAtMinute).ThenBy(c => c.Id).ToList();
            foreach (var customer in due)
            {
                state.Approaching.Remove(customer);
                if (state.Queue.Count >= queueLimit)
                {
                    result.Leave(customer.Id, CustomerExit.QueueFull);
                    continue;
                }
                customer.ArrivedAtMinute = state.Minute;
                customer.JoinsAtMinute = 0;
                state.Queue.Add(customer);
                result.Join(customer.Id);
            }
        }

        // Each worker has one minute of work. A finished order takes the next customer straight away,
        // so a 0.5-minute order lets one worker serve two customers in a minute.
        private void Serve(ServiceState state, SalesInput input, Dictionary<string, int> stock, SeededRandom random, SalesResult result)
        {
            var servers = Math.Max(0, input.Servers);
            // Fewer workers than orders (a hire went home): the extra orders go back to the head of the queue.
            while (state.Window.Count > servers)
            {
                var order = state.Window[state.Window.Count - 1];
                state.Window.RemoveAt(state.Window.Count - 1);
                order.Item = null;
                order.MinutesLeft = 0;
                // They reached the window once; their wait starts again rather than ending at once.
                order.ArrivedAtMinute = state.Minute;
                state.Queue.Insert(0, order);
            }

            // Idle workers take the next customers before any time passes, so a worker who frees up
            // later in the minute never picks ahead of one who was idle at its start.
            var active = new List<ServiceCustomer?>(state.Window);
            state.Window.Clear();
            for (var worker = active.Count; worker < servers; worker++) active.Add(StartNext(state, input, stock, result));
            // Completions run in time order across the workers (ties by worker), so the order that
            // finishes first gets the last unit, and a freed worker starts the next customer at that moment.
            var used = new double[servers];
            while (true)
            {
                var next = -1;
                var nextTime = double.MaxValue;
                for (var worker = 0; worker < servers; worker++)
                {
                    var order = active[worker];
                    if (order == null) continue;
                    var done = used[worker] + order.MinutesLeft;
                    if (done <= 1.0 + Epsilon && done < nextTime - Epsilon)
                    {
                        next = worker;
                        nextTime = done;
                    }
                }
                if (next < 0) break;

                var finished = active[next]!;
                used[next] = Math.Min(1.0, nextTime);
                finished.MinutesLeft = 0;
                // An order whose item is no longer on offer (sold out, or its price now refused) gets the
                // category's current item, or the customer walks on.
                var item = StillOffered(finished, input, stock) ? finished.Item : ItemFor(finished.Category, input, stock);
                if (item == null) result.Leave(finished.Id, CustomerExit.NothingLeft);
                else Complete(state, finished, item!, input, stock, random, result);
                active[next] = null;
                // The last unit is gone: the orders still being made leave with the queue (SellOut).
                if (TotalUnits(stock) == 0) break;
                if (used[next] < 1.0 - Epsilon) active[next] = StartNext(state, input, stock, result);
            }
            for (var worker = 0; worker < servers; worker++)
            {
                var order = active[worker];
                if (order == null)
                {
                    // A worker who finished right at the end of the minute calls the next customer now.
                    if (used[worker] >= 1.0 - Epsilon) order = StartNext(state, input, stock, result);
                    if (order == null) continue;
                }
                else order.MinutesLeft = Math.Max(0, order.MinutesLeft - (1.0 - used[worker]));
                state.Window.Add(order);
            }
        }

        // The next customer in line who can still get something, now at the window.
        private ServiceCustomer? StartNext(ServiceState state, SalesInput input, Dictionary<string, int> stock, SalesResult result)
        {
            // Sold out: leave the queue for SellOut, which sends it away as Closed.
            while (state.Queue.Count > 0 && TotalUnits(stock) > 0)
            {
                var customer = state.Queue[0];
                state.Queue.RemoveAt(0);
                var item = ItemFor(customer.Category, input, stock);
                if (item == null)
                {
                    result.Leave(customer.Id, CustomerExit.NothingLeft);
                    continue;
                }
                customer.Item = item;
                var speed = input.ServiceSpeed;
                if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0) speed = 1.0;
                customer.MinutesLeft = tables.Product(item)!.ServiceMinutes / Math.Min(10, speed);
                return customer;
            }
            return null;
        }

        // Everyone in the queue who has waited their patience by waitedUntil leaves.
        private void GiveUp(ServiceState state, SalesInput input, SalesResult result, long waitedUntil)
        {
            var rain = input.Raining ? rules.RainPatience : 1.0;
            for (var i = 0; i < state.Queue.Count;)
            {
                var customer = state.Queue[i];
                if (waitedUntil - customer.ArrivedAtMinute >= customer.Patience * rain)
                {
                    state.Queue.RemoveAt(i);
                    result.Leave(customer.Id, CustomerExit.GaveUp);
                }
                else i++;
            }
        }

        private void Complete(ServiceState state, ServiceCustomer customer, string item, SalesInput input,
            Dictionary<string, int> stock, SeededRandom random, SalesResult result)
        {
            // Add-ons ride only with customers who came for a main item, so the per-customer average
            // is spread over those customers alone. The rate comes from the stock before this sale,
            // so selling the last hot dog still brings its drink.
            var mainCustomer = tables.Category(customer.Category).AddOnFactor <= 0;
            var rate = mainCustomer ? AddOnsPerMainCustomer(input, stock) : 0;
            Take(stock, item);
            result.Sell(new Sale(customer.Id, item, false, state.Minute));
            if (!mainCustomer) return;

            var count = (int)Math.Floor(rate) + (random.Chance(rate - Math.Floor(rate)) ? 1 : 0);
            for (var i = 0; i < count; i++)
            {
                var addOn = AddOnItem(input, stock);
                if (addOn == null) break;
                Take(stock, addOn);
                result.Sell(new Sale(customer.Id, addOn, true, state.Minute));
            }
        }

        private double AddOnsPerMainCustomer(SalesInput input, Dictionary<string, int> stock)
        {
            var average = Usable(input.AddOnsPerCustomer);
            if (average <= 0) return 0;
            var shares = input.CustomerShares;
            if (shares == null || shares.Count == 0)
            {
                // Without shares every stocked category is equally likely (PickCategory).
                var stocked = stock.Where(p => p.Value > 0).Select(p => tables.Product(p.Key)!.Category).Distinct().ToList();
                var mains = stocked.Count(c => tables.Category(c).AddOnFactor <= 0);
                return mains > 0 ? Math.Min(10, average * stocked.Count / mains) : 0;
            }
            var main = 0.0;
            // Ordinal order, so the sum rounds the same whatever order the dictionary was filled in.
            foreach (var pair in shares.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!IsCategory(pair.Key) || tables.Category(pair.Key).AddOnFactor > 0) continue;
                main += Usable(pair.Value);
            }
            return main > 0 ? Math.Min(10, average / main) : 0;
        }

        // A category weighted by the customer shares. A customer whose category has run out walks on
        // (null): within a long call the demand for a sold-out category must not turn into drink sales.
        // Without shares, every stocked category is equally likely.
        private string? PickCategory(IReadOnlyDictionary<string, double>? shares, Dictionary<string, int> stock, SeededRandom random)
        {
            var stocked = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var pair in stock)
                if (pair.Value > 0) stocked.Add(tables.Product(pair.Key)!.Category);
            if (stocked.Count == 0) return null;

            var weights = new List<(string Category, double Weight)>();
            if (shares == null || shares.Count == 0)
            {
                foreach (var category in stocked) weights.Add((category, 1.0));
            }
            else
            {
                foreach (var pair in shares.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var weight = Usable(pair.Value);
                    if (weight > 0 && IsCategory(pair.Key)) weights.Add((pair.Key, weight));
                }
                if (weights.Count == 0) return null;
            }

            var total = weights.Sum(w => w.Weight);
            var draw = random.NextDouble() * total;
            var picked = weights[weights.Count - 1].Category;
            foreach (var (category, weight) in weights)
            {
                if (draw < weight)
                {
                    picked = category;
                    break;
                }
                draw -= weight;
            }
            return stocked.Contains(picked) ? picked : null;
        }

        // The category's chosen item (CategoryItems) while it lasts, or without choices its item with the
        // most units left, ties broken by name.
        private string? ItemFor(string category, SalesInput input, Dictionary<string, int> stock)
        {
            if (input.CategoryItems != null)
            {
                return input.CategoryItems.TryGetValue(category, out var chosen) && InStock(chosen, stock) &&
                       tables.Product(chosen)!.Category == category
                    ? chosen
                    : null;
            }
            string? best = null;
            var bestUnits = 0;
            foreach (var pair in stock.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value > bestUnits && tables.Product(pair.Key)!.Category == category)
                {
                    best = pair.Key;
                    bestUnits = pair.Value;
                }
            }
            return best;
        }

        private string? AddOnItem(SalesInput input, Dictionary<string, int> stock)
        {
            if (input.AddOnItem != null)
            {
                return InStock(input.AddOnItem, stock) && tables.Category(tables.Product(input.AddOnItem)!.Category).AddOnFactor > 0
                    ? input.AddOnItem
                    : null;
            }
            string? best = null;
            var bestUnits = 0;
            foreach (var pair in stock.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Value > bestUnits && tables.Category(tables.Product(pair.Key)!.Category).AddOnFactor > 0)
                {
                    best = pair.Key;
                    bestUnits = pair.Value;
                }
            }
            return best;
        }

        // Units of menu items that are products, the simulator's working copy.
        private Dictionary<string, int> StockOnMenu(SalesInput input)
        {
            var stock = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in input.Menu)
            {
                if (item == null || tables.Product(item) == null || stock.ContainsKey(item)) continue;
                stock[item] = input.Stock.TryGetValue(item, out var units) ? Math.Max(0, units) : 0;
            }
            return stock;
        }

        private static bool StillOffered(ServiceCustomer order, SalesInput input, Dictionary<string, int> stock) =>
            InStock(order.Item, stock) &&
            (input.CategoryItems == null ||
             (input.CategoryItems.TryGetValue(order.Category, out var chosen) && chosen == order.Item));

        private static bool InStock(string? item, Dictionary<string, int> stock) =>
            item != null && stock.TryGetValue(item, out var units) && units > 0;

        private static void Take(Dictionary<string, int> stock, string item) => stock[item]--;

        private static long TotalUnits(Dictionary<string, int> stock)
        {
            long total = 0;
            foreach (var units in stock.Values) total += units;
            return total;
        }

        private static void SellOut(ServiceState state, SalesResult result)
        {
            result.SoldOut = true;
            SendAway(state, result);
        }

        private static void SendAway(ServiceState state, SalesResult result)
        {
            foreach (var order in state.Window) result.Leave(order.Id, CustomerExit.Closed);
            foreach (var customer in state.Queue) result.Leave(customer.Id, CustomerExit.Closed);
            foreach (var customer in state.Approaching) result.Leave(customer.Id, CustomerExit.Closed);
            state.Window.Clear();
            state.Queue.Clear();
            state.Approaching.Clear();
        }

        private bool IsCategory(string name) => tables.CategoryNames.Contains(name);

        private static double Usable(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;

        private void Check(ServiceState state, SalesInput input)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Menu == null) throw new ArgumentException("Menu must be set.", nameof(input));
            if (input.Stock == null) throw new ArgumentException("Stock must be set.", nameof(input));
            if (state.Queue == null || state.Window == null || state.Approaching == null)
                throw new ArgumentException("Queue, Window and Approaching must be set.", nameof(state));
        }

        // A save edited by hand or written by another version may name a category this one lacks.
        private void Prune(ServiceState state, SalesResult result)
        {
            foreach (var list in new[] { state.Window, state.Queue, state.Approaching })
            {
                for (var i = 0; i < list.Count;)
                {
                    var customer = list[i];
                    if (customer != null && IsCategory(customer.Category)) i++;
                    else
                    {
                        list.RemoveAt(i);
                        if (customer != null) result.Leave(customer.Id, CustomerExit.NothingLeft);
                    }
                }
            }
        }
    }
}
