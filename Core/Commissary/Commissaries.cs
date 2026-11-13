using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Commissary
{
    public static class Commissaries
    {
        public static bool Unlock(CommissaryState state, string id)
        {
            Check(state); Name(id, nameof(id));
            if (state.Unlocked.Contains(id, StringComparer.Ordinal)) return false;
            state.Unlocked.Add(id);
            return true;
        }

        public static CommissarySpace Rent(CommissaryState state, CommissaryRules rules, string vehicleId,
            string kind, string commissaryId, int day)
        {
            Check(state, rules); Name(vehicleId, nameof(vehicleId)); Name(kind, nameof(kind)); Name(commissaryId, nameof(commissaryId)); Time(day, 0);
            if (!state.Unlocked.Contains(commissaryId, StringComparer.Ordinal)) throw new InvalidOperationException("Visit the commissary before renting.");
            if (!rules.RentPerMonthCents.ContainsKey(kind)) throw new ArgumentException("Unknown vehicle kind.", nameof(kind));
            var space = state.Spaces.FirstOrDefault(s => s.VehicleId == vehicleId);
            if (space != null)
            {
                if (day < space.SinceDay || day < space.RentPaidThroughDay) throw new ArgumentOutOfRangeException(nameof(day));
                if (space.Kind != kind) throw new ArgumentException("A rented vehicle cannot change kind.", nameof(kind));
                space.CommissaryId = commissaryId;
                return space;
            }
            state.ReleasedRentPaidThroughDay.TryGetValue(vehicleId, out var releasedCursor);
            space = new CommissarySpace { VehicleId = vehicleId, Kind = kind, CommissaryId = commissaryId,
                SinceDay = day, RentPaidThroughDay = Math.Max(day - 1, releasedCursor) };
            state.Spaces.Add(space);
            state.ReleasedRentPaidThroughDay.Remove(vehicleId);
            return space;
        }

        public static bool Release(CommissaryState state, string vehicleId)
        {
            Check(state); Name(vehicleId, nameof(vehicleId));
            foreach (var space in state.Spaces.Where(s => s.VehicleId == vehicleId))
            {
                state.ReleasedRentPaidThroughDay.TryGetValue(vehicleId, out var releasedCursor);
                state.ReleasedRentPaidThroughDay[vehicleId] = Math.Max(releasedCursor,
                    space.RentPaidThroughDay ?? state.RentPaidThroughDay ?? (space.SinceDay - 1));
            }
            return state.Spaces.RemoveAll(s => s.VehicleId == vehicleId) > 0;
        }

        public static IReadOnlyList<CommissaryCharge> RentDue(CommissaryState state, CommissaryRules rules, int today)
        {
            Check(state, rules); Time(today, 0);
            var charges = new List<CommissaryCharge>();
            var processed = new List<CommissarySpace>();
            foreach (var space in state.Spaces)
            {
                if (!rules.RentPerMonthCents.ContainsKey(space.Kind))
                {
                    space.RentPaidThroughDay ??= Math.Max(space.SinceDay - 1, state.RentPaidThroughDay ?? 0);
                    continue;
                }
                var since = Math.Max(space.SinceDay, (long)(space.RentPaidThroughDay ?? state.RentPaidThroughDay ?? (space.SinceDay - 1)) + 1);
                for (var day = since; day <= today; day++)
                    charges.Add(new CommissaryCharge { VehicleId = space.VehicleId, CommissaryId = space.CommissaryId,
                        Day = (int)day, Cents = rules.DailyRentCents(space.Kind) });
                if (today >= space.SinceDay && (!space.RentPaidThroughDay.HasValue || today >= space.RentPaidThroughDay.Value))
                    processed.Add(space);
            }
            foreach (var space in processed) space.RentPaidThroughDay = today;
            state.RentPaidThroughDay = Math.Max(state.RentPaidThroughDay ?? 0, today);
            return charges;
        }

        public static CommissaryOrder Order(CommissaryState state, CommissaryRules rules, string commissaryId,
            string itemName, int units, long wholesaleUnitPriceCents, (int Day, int Minute) now) =>
            Book(state, rules, commissaryId, itemName, units, wholesaleUnitPriceCents, now, checked(now.Day + 1), 0, null);

        internal static CommissaryOrder Book(CommissaryState state, CommissaryRules rules, string commissaryId, string itemName,
            int units, long wholesaleUnitPriceCents, (int Day, int Minute) now, int deliverDay, int cappedBoxes, long? standingOrderId,
            int? countDay = null)
        {
            if (countDay < 1) throw new ArgumentOutOfRangeException(nameof(countDay));
            Check(state, rules); Name(commissaryId, nameof(commissaryId)); Name(itemName, nameof(itemName)); Time(now.Day, now.Minute);
            if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
            if (wholesaleUnitPriceCents < 0) throw new ArgumentOutOfRangeException(nameof(wholesaleUnitPriceCents));
            if (cappedBoxes < 0) throw new ArgumentOutOfRangeException(nameof(cappedBoxes));
            if (deliverDay < now.Day || (deliverDay == now.Day && standingOrderId == null)) throw new ArgumentOutOfRangeException(nameof(deliverDay));
            if (!state.Unlocked.Contains(commissaryId, StringComparer.Ordinal) || !state.Spaces.Any(s => s.CommissaryId == commissaryId))
                throw new InvalidOperationException("Ordering requires an unlocked commissary with a rented space.");
            var order = new CommissaryOrder { Id = state.NextOrderId, CommissaryId = commissaryId, ItemName = itemName,
                Units = units, UnitPriceCents = wholesaleUnitPriceCents, OrderedDay = now.Day, DeliverDay = deliverDay,
                CappedBoxes = cappedBoxes, StandingOrderId = standingOrderId, CountDay = countDay == deliverDay ? null : countDay };
            _ = checked(order.GoodsCents + rules.DeliveryFeeCents);
            var nextId = checked(state.NextOrderId + 1);
            state.Orders.Add(order);
            state.NextOrderId = nextId;
            return order;
        }

        public static CommissaryDeliveries Deliver(CommissaryState state, CommissaryRules rules, int nowDay, int nowMinute,
            Func<CommissaryOrder, bool> pay)
        {
            Check(state, rules); Time(nowDay, nowMinute);
            if (pay == null) throw new ArgumentNullException(nameof(pay));
            var due = Due(state, rules, nowDay, nowMinute);
            if (due.Any(o => o.DeliverDay < 1)) throw new InvalidOperationException("An order is due on a day before day 1.");
            _ = checked(state.NextBatchId + due.Count);
            var delivered = new List<CommissaryOrder>();
            var refused = new List<CommissaryOrder>();
            state.FeesPaid.RemoveAll(f => f.Day < nowDay - 1 && !due.Any(o => o.DeliverDay == f.Day));
            foreach (var order in due)
            {
                var paidAlready = state.FeesPaid.Any(f => f.CommissaryId == order.CommissaryId && f.Day == order.DeliverDay);
                order.FeeCents = paidAlready ? 0 : rules.DeliveryFeeCents;
                var paid = pay(order);
                state.Orders.Remove(order);
                if (!paid)
                {
                    refused.Add(order);
                    continue;
                }
                if (!paidAlready) state.FeesPaid.Add(new CommissaryFeePaid { CommissaryId = order.CommissaryId, Day = order.DeliverDay });
                CommissarySupply.Delivered(state, order, nowDay);
                Stock(state, order.CommissaryId).Add(new CommissaryStockLine { ItemName = order.ItemName, Units = order.Units,
                    UnitPriceCents = order.UnitPriceCents, BatchId = CommissaryValidation.NextBatch(state),
                    OrderMetadata = CopyExtra(order.ExtensionData) });
                delivered.Add(order);
            }
            return new CommissaryDeliveries(delivered, refused);
        }

        public static IReadOnlyList<CommissaryOrder> Due(CommissaryState state, CommissaryRules rules, int nowDay, int nowMinute)
        {
            Check(state, rules); Time(nowDay, nowMinute);
            return state.Orders.Where(o => o.DeliverDay < nowDay || (o.DeliverDay == nowDay && nowMinute >= rules.DeliveryMinute))
                .OrderBy(o => o.DeliverDay).ThenBy(o => o.Id).ToList();
        }

        public static IReadOnlyList<CommissaryStockLine> Restock(CommissaryState state, string commissaryId, IReadOnlyDictionary<string, int> wanted)
        {
            Check(state); Name(commissaryId, nameof(commissaryId));
            if (wanted == null) throw new ArgumentNullException(nameof(wanted));
            foreach (var pair in wanted)
            {
                Name(pair.Key, nameof(wanted));
                if (pair.Value < 0) throw new ArgumentOutOfRangeException(nameof(wanted));
            }
            var taken = new List<CommissaryStockLine>();
            if (!state.Stock.TryGetValue(commissaryId, out var stock)) return taken;
            var remaining = wanted.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            foreach (var line in stock.OrderBy(l => l.BatchId))
            {
                if (!remaining.TryGetValue(line.ItemName, out var want) || want == 0) continue;
                var units = Math.Min(want, line.Units);
                if (units == 0) continue;
                taken.Add(Copy(line, units));
                line.Units -= units;
                remaining[line.ItemName] -= units;
            }
            stock.RemoveAll(l => l.Units == 0);
            return taken;
        }

        public static void ReturnToStock(CommissaryState state, string commissaryId, IEnumerable<CommissaryStockLine> lines)
        {
            Check(state); Name(commissaryId, nameof(commissaryId));
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            var returned = lines.ToList();
            var stock = state.Stock.TryGetValue(commissaryId, out var existing) ? existing : new List<CommissaryStockLine>();
            var replacements = stock.Select(l => Copy(l, l.Units)).ToList();
            var nextId = state.NextBatchId;
            foreach (var line in returned)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.ItemName) || line.Units < 0 || line.UnitPriceCents < 0 || line.BatchId < 0)
                    throw new ArgumentException("Stock lines must name items with units and prices not negative.", nameof(lines));
                if (line.Units == 0) continue;
                var batchId = line.BatchId == 0 ? nextId : line.BatchId;
                nextId = Math.Max(nextId, checked(batchId + 1));
                if (state.Stock.Any(p => p.Key != commissaryId && p.Value.Any(l => l.BatchId == batchId)))
                    throw new ArgumentException("A batch belongs to another commissary.", nameof(lines));
                var batch = replacements.FirstOrDefault(l => l.BatchId == batchId);
                if (batch == null)
                {
                    batch = Copy(line, line.Units);
                    batch.BatchId = batchId;
                    replacements.Add(batch);
                }
                else
                {
                    if (batch.ItemName != line.ItemName || batch.UnitPriceCents != line.UnitPriceCents)
                        throw new ArgumentException("A batch cannot change item or price.", nameof(lines));
                    batch.Units = checked(batch.Units + line.Units);
                    foreach (var extra in line.ExtensionData) if (!batch.ExtensionData.ContainsKey(extra.Key)) batch.ExtensionData.Add(extra.Key, extra.Value.DeepClone());
                    foreach (var extra in line.OrderMetadata) if (!batch.OrderMetadata.ContainsKey(extra.Key)) batch.OrderMetadata.Add(extra.Key, extra.Value.DeepClone());
                }
            }
            state.Stock[commissaryId] = replacements.OrderBy(l => l.BatchId).ToList();
            state.NextBatchId = nextId;
        }

        public const string ReturnedSourceValue = "vehicle";
        public const string OrderedKey = "ordered";
        public const string DeductedPerUnitKey = "deductedPerUnit";
        public const string LoadedDayKey = "loadedDay";

        public static bool Deducted(CommissaryStockLine line) =>
            line != null && !CommissaryLogistics.FromLogistics(line) && (!FromVehicle(line) || Flag(line, OrderedKey));

        public static double DeductedPerUnit(CommissaryStockLine line)
        {
            if (!Deducted(line)) return 0;
            if (FromVehicle(line) && line.OrderMetadata.TryGetValue(DeductedPerUnitKey, out var cost) &&
                (cost.Type == Newtonsoft.Json.Linq.JTokenType.Float || cost.Type == Newtonsoft.Json.Linq.JTokenType.Integer))
                return Math.Max(0, (double)cost);
            return line.UnitPriceCents;
        }

        public static void TookOnVehicle(CommissaryState state, CommissaryStockLine line, int units)
        {
            Check(state);
            if (line == null) throw new ArgumentNullException(nameof(line));
            if (units <= 0 || !Deducted(line)) return;
            state.OrderedOnVehicles.TryGetValue(line.ItemName, out var before);
            state.OrderedOnVehiclesCents.TryGetValue(line.ItemName, out var cents);
            state.OrderedOnVehicles[line.ItemName] = checked(before + units);
            state.OrderedOnVehiclesCents[line.ItemName] = checked(cents + (long)Math.Round(units * DeductedPerUnit(line)));
        }

        // Units of an item that left a vehicle other than by a return (sold at the window or by a crew, served at a gig):
        // ordered units count as leaving first, at the pool's average cost, so the pool never keeps units the fleet no
        // longer has (review round 2 of the UI flow, Astra and Sol). Paths the mod can't see are bounded by the cut in
        // ReturnFromVehicle.
        public static void LeftVehicle(CommissaryState state, string itemName, int units)
        {
            Check(state);
            if (string.IsNullOrEmpty(itemName) || units <= 0) return;
            TakeFromPool(state, itemName, units);
        }

        private static (int Units, long Cents) TakeFromPool(CommissaryState state, string itemName, int units)
        {
            if (!state.OrderedOnVehicles.TryGetValue(itemName, out var pool) || pool <= 0)
            {
                state.OrderedOnVehicles.Remove(itemName);
                state.OrderedOnVehiclesCents.Remove(itemName);
                return (0, 0);
            }
            state.OrderedOnVehiclesCents.TryGetValue(itemName, out var cents);
            cents = Math.Max(0, cents);
            var taken = Math.Min(units, pool);
            var takenCents = taken == pool ? cents : (long)Math.Round((double)cents * taken / pool);
            if (pool - taken > 0)
            {
                state.OrderedOnVehicles[itemName] = pool - taken;
                state.OrderedOnVehiclesCents[itemName] = Math.Max(0, cents - takenCents);
            }
            else
            {
                state.OrderedOnVehicles.Remove(itemName);
                state.OrderedOnVehiclesCents.Remove(itemName);
            }
            return (taken, takenCents);
        }

        // Units a vehicle gave back at the bay, at what the vehicle paid for them (its cargo's price per unit), marked
        // with the day and the vehicle, last in the commissary's FIFO order. As many as the fleet's pool of ordered units
        // of the item allows go into a batch marked ordered, carrying the pool's average deducted cost per unit for the
        // uninstall's reversal; the pool is first cut to `fleetOnBoard`, the units of the item the food vehicles hold
        // before this return, for paths the mod can't see. The rest is a batch of other origin (a wholesaler, say).
        // `loadedDay`: the oldest day the food was loaded, kept for its age.
        public static IReadOnlyList<CommissaryStockLine> ReturnFromVehicle(CommissaryState state, string commissaryId, string itemName, int units,
            long unitPriceCents, int day, string vehicleId, int fleetOnBoard, int? loadedDay = null)
        {
            Check(state); Name(commissaryId, nameof(commissaryId)); Name(itemName, nameof(itemName)); Name(vehicleId, nameof(vehicleId)); Time(day, 0);
            if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
            if (unitPriceCents < 0) throw new ArgumentOutOfRangeException(nameof(unitPriceCents));
            _ = checked(units * unitPriceCents);
            state.OrderedOnVehicles.TryGetValue(itemName, out var pool);
            if (pool > Math.Max(0, fleetOnBoard)) TakeFromPool(state, itemName, pool - Math.Max(0, fleetOnBoard));
            var (ordered, orderedCents) = TakeFromPool(state, itemName, units);
            var lines = new List<CommissaryStockLine>();
            foreach (var (count, isOrdered) in new[] { (ordered, true), (units - ordered, false) })
            {
                if (count <= 0) continue;
                var metadata = new Dictionary<string, Newtonsoft.Json.Linq.JToken>(StringComparer.Ordinal)
                {
                    [CommissaryLogistics.SourceKey] = ReturnedSourceValue,
                    ["day"] = day,
                    ["vehicle"] = vehicleId
                };
                if (isOrdered)
                {
                    metadata[OrderedKey] = true;
                    metadata[DeductedPerUnitKey] = (double)orderedCents / count;
                }
                if (loadedDay is { } loaded && loaded >= 1) metadata[LoadedDayKey] = loaded;
                var line = new CommissaryStockLine
                {
                    ItemName = itemName, Units = count, UnitPriceCents = unitPriceCents, BatchId = CommissaryValidation.NextBatch(state),
                    OrderMetadata = metadata
                };
                Stock(state, commissaryId).Add(line);
                lines.Add(line);
            }
            return lines;
        }

        public static bool FromVehicle(CommissaryStockLine line) =>
            line?.OrderMetadata != null && line.OrderMetadata.TryGetValue(CommissaryLogistics.SourceKey, out var source) &&
            source.Type == Newtonsoft.Json.Linq.JTokenType.String && (string?)source == ReturnedSourceValue;

        public static int? LoadedDay(CommissaryStockLine line) =>
            line?.OrderMetadata != null && line.OrderMetadata.TryGetValue(LoadedDayKey, out var day) &&
            day.Type == Newtonsoft.Json.Linq.JTokenType.Integer && (int)day >= 1 ? (int)day : (int?)null;

        private static bool Flag(CommissaryStockLine line, string key) =>
            line.OrderMetadata != null && line.OrderMetadata.TryGetValue(key, out var value) &&
            value.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)value;

        public static long ReturnedGoodsCents(CommissaryState state)
        {
            Check(state);
            double cents = 0;
            foreach (var line in state.Stock.Values.SelectMany(l => l))
                if (FromVehicle(line)) cents += (double)line.Units * line.UnitPriceCents - line.Units * DeductedPerUnit(line);
            return (long)Math.Round(cents);
        }

        public static bool PublicParkingCovered(CommissaryState state, string vehicleId, string? kind, string? bayCommissaryId)
        {
            Check(state); Name(vehicleId, nameof(vehicleId));
            return (kind == "cart" || kind == "truck") && !string.IsNullOrEmpty(bayCommissaryId) &&
                   state.Spaces.Any(s => s.VehicleId == vehicleId && s.Kind == kind && s.CommissaryId == bayCommissaryId);
        }

        public static float PublicParkingAfterHour(float before, float after, bool covered) =>
            covered ? Math.Min(before, after) : after;

        public static IReadOnlyList<CommissaryFine> NightCheck(CommissaryState state, CommissaryRules rules,
            int nowDay, int nowMinute, IEnumerable<CommissaryVehicle> vehicles)
        {
            Check(state, rules); Time(nowDay, nowMinute);
            if (vehicles == null) throw new ArgumentNullException(nameof(vehicles));
            var fleet = vehicles.ToList();
            if (fleet.Any(v => v == null || string.IsNullOrWhiteSpace(v.VehicleId)) || fleet.Select(v => v.VehicleId).Distinct(StringComparer.Ordinal).Count() != fleet.Count)
                throw new ArgumentException("Vehicles must have distinct, non-empty IDs.", nameof(vehicles));
            var latest = nowMinute >= rules.NightCheckMinute ? nowDay : nowDay - 1;
            if (Undecided(state, fleet).Count > 0) return Array.Empty<CommissaryFine>();
            var first = state.NightCheckedThroughDay.HasValue ? (long)state.NightCheckedThroughDay.Value + 1 : nowDay;
            var fines = new List<CommissaryFine>();
            foreach (var vehicle in fleet)
            {
                if (vehicle.PlayerInside || vehicle.OnLateStop || vehicle.InTransit || vehicle.AtPlayerProperty ||
                    (vehicle.AtOwnCommissaryBay && state.Spaces.Any(s => s.VehicleId == vehicle.VehicleId))) continue;
                for (var day = first; day <= latest; day++)
                    fines.Add(new CommissaryFine { VehicleId = vehicle.VehicleId, Day = (int)day, Cents = rules.OvernightFineCents });
            }
            state.NightCheckedThroughDay = Math.Max(state.NightCheckedThroughDay ?? 0, latest);
            return fines;
        }

        public static IReadOnlyList<(string VehicleId, int ServiceDay)> CleanedOvernight(CommissaryState state, CommissaryRules rules,
            int nowDay, int nowMinute, IEnumerable<CommissaryVehicle> vehicles)
        {
            Check(state, rules); Time(nowDay, nowMinute);
            if (vehicles == null) throw new ArgumentNullException(nameof(vehicles));
            var fleet = vehicles.Where(v => v != null).ToList();
            var cleaned = new List<(string, int)>();
            if (state.NightCheckedThroughDay == null || Undecided(state, fleet).Count > 0) return cleaned;
            var latest = nowMinute >= rules.NightCheckMinute ? nowDay : nowDay - 1;
            var first = (long)state.NightCheckedThroughDay.Value + 1;
            foreach (var vehicle in fleet)
            {
                if (vehicle.BayGrace || vehicle.InTransit || !state.Spaces.Any(s => s.VehicleId == vehicle.VehicleId)) continue;
                if (!vehicle.AtOwnCommissaryBay && !vehicle.BaySideUnknown) continue;
                for (var day = first; day <= latest; day++)
                {
                    if (day - 1 < 1) continue;
                    if (vehicle.AtOwnCommissaryBay) cleaned.Add((vehicle.VehicleId, (int)(day - 1)));
                    else
                    {
                        if (!state.PendingCleaning.TryGetValue(vehicle.VehicleId, out var days)) state.PendingCleaning[vehicle.VehicleId] = days = new List<int>();
                        if (!days.Contains((int)(day - 1))) days.Add((int)(day - 1));
                    }
                }
            }
            return cleaned;
        }

        public static IReadOnlyList<int> ResolvePendingCleaning(CommissaryState state, string vehicleId, bool inOwnBay)
        {
            Check(state); Name(vehicleId, nameof(vehicleId));
            if (!state.PendingCleaning.TryGetValue(vehicleId, out var days)) return Array.Empty<int>();
            state.PendingCleaning.Remove(vehicleId);
            return inOwnBay ? days.Where(d => d >= 1).Distinct().OrderBy(d => d).ToList() : (IReadOnlyList<int>)Array.Empty<int>();
        }

        public static IReadOnlyList<string> Undecided(CommissaryState state, IEnumerable<CommissaryVehicle> vehicles)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (vehicles == null) throw new ArgumentNullException(nameof(vehicles));
            return vehicles.Where(v => v != null && v.BaySideUnknown && !v.AtOwnCommissaryBay &&
                                       !(v.PlayerInside || v.OnLateStop || v.InTransit || v.AtPlayerProperty) &&
                                       state.Spaces.Any(s => s.VehicleId == v.VehicleId))
                .Select(v => v.VehicleId).ToList();
        }

        public static CommissaryRefund Refund(CommissaryState state)
        {
            Check(state);
            long goods = 0;
            foreach (var line in state.Stock.Values.SelectMany(lines => lines)) goods = checked(goods + checked(line.Units * line.UnitPriceCents));
            var refund = new CommissaryRefund(goods, state.Orders.Count, state.StandingOrders.Count);
            state.Stock.Clear();
            state.Orders.Clear();
            state.StandingOrders.Clear();
            state.WeekBoxes.Clear();
            state.FeesPaid.Clear();
            state.OrderedOnVehicles.Clear();
            state.OrderedOnVehiclesCents.Clear();
            state.PendingCleaning.Clear();
            state.Spaces.Clear();
            state.ReleasedRentPaidThroughDay.Clear();
            return refund;
        }

        private static void Check(CommissaryState state, CommissaryRules rules)
        {
            Check(state);
            if (rules == null) throw new ArgumentNullException(nameof(rules));
        }

        private static void Check(CommissaryState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
        }

        private static void Time(int day, int minute)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            if (minute < 0 || minute > 1439) throw new ArgumentOutOfRangeException(nameof(minute));
        }

        private static void Name(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("ID must not be empty.", parameter);
        }

        private static List<CommissaryStockLine> Stock(CommissaryState state, string id)
        {
            if (!state.Stock.TryGetValue(id, out var lines)) state.Stock[id] = lines = new List<CommissaryStockLine>();
            return lines;
        }

        private static IDictionary<string, Newtonsoft.Json.Linq.JToken> CopyExtra(IDictionary<string, Newtonsoft.Json.Linq.JToken> extra) =>
            extra.ToDictionary(p => p.Key, p => p.Value.DeepClone(), StringComparer.Ordinal);

        private static CommissaryStockLine Copy(CommissaryStockLine line, int units) => new CommissaryStockLine
        {
            ItemName = line.ItemName, Units = units, UnitPriceCents = line.UnitPriceCents, BatchId = line.BatchId,
            ExtensionData = CopyExtra(line.ExtensionData), OrderMetadata = CopyExtra(line.OrderMetadata)
        };
    }
}
