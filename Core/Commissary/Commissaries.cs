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

        // RentDue covers the rental day; Rent itself doesn't take money. Moving preserves the uninterrupted
        // rental's starting day and cursor, so unprocessed days and same-day rent aren't lost or charged twice.
        // Renting after release starts a new rental, but preserves any rent already processed for that day.
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

        // The adapter should settle RentDue before releasing a space. Its stock and orders remain available.
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
                // Retain unknown future kinds with their cursor untouched; do not invent a rent for them.
                if (!rules.RentPerMonthCents.ContainsKey(space.Kind))
                {
                    // Anchor its unprocessed days before the fleet marker advances.
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
            string itemName, int units, long wholesaleUnitPriceCents, (int Day, int Minute) now)
        {
            Check(state, rules); Name(commissaryId, nameof(commissaryId)); Name(itemName, nameof(itemName)); Time(now.Day, now.Minute);
            if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
            if (wholesaleUnitPriceCents < 0) throw new ArgumentOutOfRangeException(nameof(wholesaleUnitPriceCents));
            if (!state.Unlocked.Contains(commissaryId, StringComparer.Ordinal) || !state.Spaces.Any(s => s.CommissaryId == commissaryId))
                throw new InvalidOperationException("Ordering requires an unlocked commissary with a rented space.");
            var order = new CommissaryOrder { Id = state.NextOrderId, CommissaryId = commissaryId, ItemName = itemName,
                Units = units, UnitPriceCents = wholesaleUnitPriceCents, OrderedDay = now.Day, DeliverDay = checked(now.Day + 1) };
            order.FeeCents = rules.FeeCents(order.GoodsCents);
            _ = order.TotalCents; // Refuse an overflowing price before changing state.
            var nextId = checked(state.NextOrderId + 1);
            state.Orders.Add(order);
            state.NextOrderId = nextId;
            return order;
        }

        // Failed order payments hold goods out of stock. The adapter calls Deliver after adding a failed
        // charge, and again after removing it on payment: on observing settlement we schedule the next 08:00 (or configured
        // delivery minute), today if still before it, tomorrow otherwise. Overdue paid orders still catch up.
        public static IReadOnlyList<CommissaryOrder> Deliver(CommissaryState state, CommissaryRules rules, int nowDay, int nowMinute)
        {
            Check(state, rules); Time(nowDay, nowMinute);
            var unpaid = PendingOrderIds(state);
            foreach (var order in state.Orders)
            {
                if (unpaid.Contains(order.Id)) order.AwaitingPayment = true;
                else if (order.AwaitingPayment)
                {
                    order.DeliverDay = Math.Max(order.DeliverDay, nowMinute < rules.DeliveryMinute ? nowDay : checked(nowDay + 1));
                    order.AwaitingPayment = false;
                }
            }
            var due = state.Orders.Where(o => !unpaid.Contains(o.Id) &&
                    (o.DeliverDay < nowDay || (o.DeliverDay == nowDay && nowMinute >= rules.DeliveryMinute)))
                .OrderBy(o => o.DeliverDay).ThenBy(o => o.Id).ToList();
            _ = checked(state.NextBatchId + due.Count);
            foreach (var order in due)
            {
                Stock(state, order.CommissaryId).Add(new CommissaryStockLine { ItemName = order.ItemName, Units = order.Units,
                    UnitPriceCents = order.UnitPriceCents, BatchId = CommissaryValidation.NextBatch(state),
                    OrderMetadata = CopyExtra(order.ExtensionData) });
                state.Orders.Remove(order);
            }
            return due;
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

        // Returned lines carry their original BatchId; partial or whole rejected loads regain their FIFO place.
        // Lines without a BatchId are new batches. No stock-limit or cargo-slot decisions belong in Core here.
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

        // Across skipped nights, today's flags stand in for each skipped check. The first call starts tracking
        // now: before 04:00 it sets the baseline to yesterday, after 04:00 it checks only today's night.
        public static IReadOnlyList<CommissaryFine> NightCheck(CommissaryState state, CommissaryRules rules,
            int nowDay, int nowMinute, IEnumerable<CommissaryVehicle> vehicles)
        {
            Check(state, rules); Time(nowDay, nowMinute);
            if (vehicles == null) throw new ArgumentNullException(nameof(vehicles));
            var fleet = vehicles.ToList();
            if (fleet.Any(v => v == null || string.IsNullOrWhiteSpace(v.VehicleId)) || fleet.Select(v => v.VehicleId).Distinct(StringComparer.Ordinal).Count() != fleet.Count)
                throw new ArgumentException("Vehicles must have distinct, non-empty IDs.", nameof(vehicles));
            var latest = nowMinute >= rules.NightCheckMinute ? nowDay : nowDay - 1;
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

        // Cancel unpaid orders and their charges without paying a refund. Rent/fine debts remain to settle.
        // The adapter refunds Total in cash and books -FeeCents via SalesLedger.RecordDeliveryFees; goods
        // have not been expensed. Delivered stock refunds goods only, since its delivery has been fulfilled.
        public static CommissaryRefund Refund(CommissaryState state)
        {
            Check(state);
            var unpaid = PendingOrderIds(state);
            var cancelled = new HashSet<long>(state.Orders.Select(o => o.Id));
            long goods = 0, fees = 0;
            foreach (var line in state.Stock.Values.SelectMany(lines => lines)) goods = checked(goods + checked(line.Units * line.UnitPriceCents));
            foreach (var order in state.Orders.Where(o => !unpaid.Contains(o.Id)))
            {
                goods = checked(goods + order.GoodsCents);
                fees = checked(fees + order.FeeCents);
            }
            var refund = new CommissaryRefund(goods, fees);
            state.Stock.Clear();
            state.Orders.Clear();
            state.Spaces.Clear();
            state.ReleasedRentPaidThroughDay.Clear();
            state.Charges.RemoveAll(c => c.OrderId.HasValue && cancelled.Contains(c.OrderId.Value));
            return refund;
        }

        private static HashSet<long> PendingOrderIds(CommissaryState state) =>
            new HashSet<long>(state.Charges.Where(c => c.OrderId.HasValue).Select(c => c.OrderId!.Value));

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
