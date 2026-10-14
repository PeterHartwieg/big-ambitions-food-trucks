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
            string itemName, int units, long wholesaleUnitPriceCents, (int Day, int Minute) now) =>
            Book(state, rules, commissaryId, itemName, units, wholesaleUnitPriceCents, now, checked(now.Day + 1), 0, null);

        // One order for delivery on deliverDay: tomorrow for a phone order, today for a standing order's delivery.
        // cappedBoxes are its food boxes, counted towards the item's weekly limit (CommissarySupply).
        internal static CommissaryOrder Book(CommissaryState state, CommissaryRules rules, string commissaryId, string itemName,
            int units, long wholesaleUnitPriceCents, (int Day, int Minute) now, int deliverDay, int cappedBoxes, long? standingOrderId)
        {
            Check(state, rules); Name(commissaryId, nameof(commissaryId)); Name(itemName, nameof(itemName)); Time(now.Day, now.Minute);
            if (units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
            if (wholesaleUnitPriceCents < 0) throw new ArgumentOutOfRangeException(nameof(wholesaleUnitPriceCents));
            if (cappedBoxes < 0) throw new ArgumentOutOfRangeException(nameof(cappedBoxes));
            if (deliverDay < now.Day || (deliverDay == now.Day && standingOrderId == null)) throw new ArgumentOutOfRangeException(nameof(deliverDay));
            if (!state.Unlocked.Contains(commissaryId, StringComparer.Ordinal) || !state.Spaces.Any(s => s.CommissaryId == commissaryId))
                throw new InvalidOperationException("Ordering requires an unlocked commissary with a rented space.");
            var order = new CommissaryOrder { Id = state.NextOrderId, CommissaryId = commissaryId, ItemName = itemName,
                Units = units, UnitPriceCents = wholesaleUnitPriceCents, OrderedDay = now.Day, DeliverDay = deliverDay,
                CappedBoxes = cappedBoxes, StandingOrderId = standingOrderId };
            order.FeeCents = rules.FeeCents(order.GoodsCents);
            _ = order.TotalCents; // Refuse an overflowing price before changing state.
            var nextId = checked(state.NextOrderId + 1);
            state.Orders.Add(order);
            state.NextOrderId = nextId;
            return order;
        }

        // Orders are paid on delivery, as the game's own wholesale and furniture deliveries are [code, 1.0:
        // BusinessHelper.DeliverWholesaleContract, FurnitureDeliveryHelper.DeliverFurnitureContract]: each order due by
        // now (its delivery day at the delivery minute, or an earlier day) is offered to pay, oldest first. Paid, its
        // goods go into the commissary's stock; refused, the order is cancelled and nothing arrives, as the game drops a
        // one-off delivery the player can't pay for. Orders not yet due are left alone.
        public static CommissaryDeliveries Deliver(CommissaryState state, CommissaryRules rules, int nowDay, int nowMinute,
            Func<CommissaryOrder, bool> pay)
        {
            Check(state, rules); Time(nowDay, nowMinute);
            if (pay == null) throw new ArgumentNullException(nameof(pay));
            var due = Due(state, rules, nowDay, nowMinute);
            // Checked before any payment: nothing is paid for an order that can't then be delivered.
            if (due.Any(o => o.DeliverDay < 1)) throw new InvalidOperationException("An order is due on a day before day 1.");
            _ = checked(state.NextBatchId + due.Count);
            var delivered = new List<CommissaryOrder>();
            var refused = new List<CommissaryOrder>();
            foreach (var order in due)
            {
                // Each order leaves the list as soon as it is decided, so a payment that throws keeps the ones before it.
                var paid = pay(order);
                state.Orders.Remove(order);
                if (!paid)
                {
                    refused.Add(order);
                    continue;
                }
                CommissarySupply.Delivered(state, order, nowDay);
                Stock(state, order.CommissaryId).Add(new CommissaryStockLine { ItemName = order.ItemName, Units = order.Units,
                    UnitPriceCents = order.UnitPriceCents, BatchId = CommissaryValidation.NextBatch(state),
                    OrderMetadata = CopyExtra(order.ExtensionData) });
                delivered.Add(order);
            }
            return new CommissaryDeliveries(delivered, refused);
        }

        // The orders due by now, oldest delivery day first, then in the order they were placed.
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

        // Rent covers hourly public parking only for this food vehicle in the bay of its active space.
        // The adapter supplies a bay ID only after the usual InBay test passes.
        public static bool PublicParkingCovered(CommissaryState state, string vehicleId, string? kind, string? bayCommissaryId)
        {
            Check(state); Name(vehicleId, nameof(vehicleId));
            return (kind == "cart" || kind == "truck") && !string.IsNullOrEmpty(bayCommissaryId) &&
                   state.Spaces.Any(s => s.VehicleId == vehicleId && s.Kind == kind && s.CommissaryId == bayCommissaryId);
        }

        // Remove only this hour's increment in a rented bay; earlier street charges stay due at midnight.
        public static float PublicParkingAfterHour(float before, float after, bool covered) =>
            covered ? Math.Min(before, after) : after;

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
            // A vehicle whose standing can't be decided yet holds the whole check: nothing is fined and the night isn't
            // marked checked, so it runs again once the street's side can be read (Undecided).
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

        // The vehicles the night check can't decide yet: one that would be fined unless it is in its own bay, has a space,
        // and whose street side at that bay couldn't be read. Any other exemption decides it without the bay.
        public static IReadOnlyList<string> Undecided(CommissaryState state, IEnumerable<CommissaryVehicle> vehicles)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (vehicles == null) throw new ArgumentNullException(nameof(vehicles));
            return vehicles.Where(v => v != null && v.BaySideUnknown && !v.AtOwnCommissaryBay &&
                                       !(v.PlayerInside || v.OnLateStop || v.InTransit || v.AtPlayerProperty) &&
                                       state.Spaces.Any(s => s.VehicleId == v.VehicleId))
                .Select(v => v.VehicleId).ToList();
        }

        // The uninstall step: delivered stock (paid on delivery) is refunded at its goods price; open orders haven't been
        // paid, so they are cancelled with nothing to refund, and standing orders end. Spaces are given up; rent and fine debts remain to settle.
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
