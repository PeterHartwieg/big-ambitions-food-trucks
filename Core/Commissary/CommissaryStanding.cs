using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;

namespace FoodTrucks.Core.Commissary
{
    // The weekly limit on boxes of each food a commissary delivers from phone orders (Peter, 14 October 2026: phone
    // orders are limited, so a growing fleet builds its own supply chain with a warehouse and a logistics plan). It
    // mirrors the game's wholesale contracts, which cap each item's units a week (Item.maxWholesaleOrderAmount against
    // DeliveryContractItem.amountOrderedThisWeek, reset when the weekday is 1, that is on days 1, 8, 15 ...; lifted by
    // the gameVariables.disableWholesaleAndImportLimits setting) [code, 1.0: BusinessHelper.DeliverWholesaleContract,
    // BusinessHelper.HandleWholesaleDeliveries, TimeHelper.GetDayOfWeek]. Here the limit is in boxes per commissary and
    // food, from rules.json. One-off and standing orders count together by their delivery day's week: boxes delivered,
    // boxes booked and not yet delivered, and the deliveries standing orders still have to book that week, at full
    // size. Equipment and logistics plan deliveries don't count.
    public static class CommissarySupply
    {
        // The game's week: days 1 to 7, 8 to 14 and so on, starting on the weekday the game resets its weekly limits.
        public static int WeekStart(int day)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            return day - (day - 1) % 7;
        }

        // Boxes of the item counted in the week of `day` at the commissary.
        public static int Used(CommissaryState state, string commissaryId, string itemName, int day)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var week = WeekStart(day);
            long used = state.WeekBoxes.Where(w => w.CommissaryId == commissaryId && w.ItemName == itemName && w.WeekStart == week).Sum(w => (long)w.Boxes);
            used += state.Orders.Where(o => o.CommissaryId == commissaryId && o.ItemName == itemName && WeekStart(o.DeliverDay) == week).Sum(o => (long)o.CappedBoxes);
            foreach (var standing in state.StandingOrders.Where(s => s.CommissaryId == commissaryId))
            {
                var boxes = standing.Lines.Where(l => l.Capped && l.ItemName == itemName).Sum(l => (long)l.Boxes);
                if (boxes > 0) used += boxes * CommissaryStandingOrders.DeliveriesIn(standing, week, week + 6);
            }
            return (int)Math.Min(int.MaxValue, used);
        }

        // Boxes of the item still free in the week of `day`, never below 0; int.MaxValue with the limits lifted.
        public static int Left(CommissaryState state, CommissaryRules rules, string commissaryId, string itemName, int day, bool unlimited = false)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (unlimited) return int.MaxValue;
            return Math.Max(0, rules.FoodBoxLimit(itemName) - Used(state, commissaryId, itemName, day));
        }

        // Whether a quote's food fits under each item's weekly limit in every week it delivers in: tomorrow's week for a
        // one-off order; for a standing order its deliveries in the next 14 days, so both the rest of the first week and
        // a whole week are checked. The first item and week that don't fit, with the boxes left there and the boxes the
        // order needs there.
        public static CommissaryCapCheck Check(CommissaryState state, CommissaryRules rules, string commissaryId,
            CommissaryQuote quote, CommissaryRepeat repeat, int today, bool unlimited = false)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (quote == null) throw new ArgumentNullException(nameof(quote));
            if (today < 1) throw new ArgumentOutOfRangeException(nameof(today));
            var first = checked(today + 1);
            if (unlimited) return CommissaryCapCheck.Fine(first);
            var interval = repeat == CommissaryRepeat.Once ? 0 : CommissaryStandingOrders.Interval(repeat);
            var days = new List<int> { first };
            if (interval > 0)
                for (var day = first + interval; day < first + 14; day += interval) days.Add(day);
            foreach (var line in quote.Lines.Where(l => l.CappedBoxes > 0))
                foreach (var week in days.Select(WeekStart).Distinct())
                {
                    var needed = (long)line.CappedBoxes * days.Count(d => WeekStart(d) == week);
                    var left = Left(state, rules, commissaryId, line.ItemName, week);
                    if (needed > left) return new CommissaryCapCheck(false, line.ItemName, week, left, (int)Math.Min(int.MaxValue, needed));
                }
            return CommissaryCapCheck.Fine(first);
        }

        // A paid delivery's food boxes go into its week's count; weeks before the current one are dropped.
        internal static void Delivered(CommissaryState state, CommissaryOrder order, int today)
        {
            var current = WeekStart(today);
            state.WeekBoxes.RemoveAll(w => w.WeekStart < current);
            if (order.CappedBoxes <= 0) return;
            var week = WeekStart(order.DeliverDay);
            if (week < current) return;
            var entry = state.WeekBoxes.FirstOrDefault(w => w.CommissaryId == order.CommissaryId && w.ItemName == order.ItemName && w.WeekStart == week);
            if (entry == null)
                state.WeekBoxes.Add(entry = new CommissaryWeekBoxes { CommissaryId = order.CommissaryId, ItemName = order.ItemName, WeekStart = week });
            entry.Boxes = checked(entry.Boxes + order.CappedBoxes);
        }
    }

    public sealed class CommissaryCapCheck
    {
        internal CommissaryCapCheck(bool fits, string? itemName, int weekStart, int left, int needed)
        {
            Fits = fits;
            ItemName = itemName;
            WeekStart = weekStart;
            Left = left;
            Needed = needed;
        }

        internal static CommissaryCapCheck Fine(int day) => new CommissaryCapCheck(true, null, CommissarySupply.WeekStart(day), int.MaxValue, 0);

        public bool Fits { get; }
        // The item over its limit, and the week (its first day) it is over in; null when it fits.
        public string? ItemName { get; }
        public int WeekStart { get; }
        // Boxes of it left that week, and the boxes the order needs there.
        public int Left { get; }
        public int Needed { get; }
    }

    // A wholesale price on the day a standing order books: the item's box size and price per unit.
    public readonly struct CommissaryPrice
    {
        public CommissaryPrice(int boxSize, long unitPriceCents)
        {
            if (boxSize <= 0) throw new ArgumentOutOfRangeException(nameof(boxSize));
            if (unitPriceCents < 0) throw new ArgumentOutOfRangeException(nameof(unitPriceCents));
            BoxSize = boxSize;
            UnitPriceCents = unitPriceCents;
        }

        public int BoxSize { get; }
        public long UnitPriceCents { get; }
    }

    // What one standing order did on a booking run.
    public sealed class CommissaryStandingBooking
    {
        internal CommissaryStandingBooking(CommissaryStandingOrder standing, int day)
        {
            Standing = standing;
            Day = day;
        }

        public CommissaryStandingOrder Standing { get; }
        // The delivery day it booked for (or skipped).
        public int Day { get; }
        // The orders booked for the day's delivery, one per item, as a phone order books them.
        public List<CommissaryOrder> Orders { get; } = new List<CommissaryOrder>();
        // Food lines cut by the weekly limit: the item, the boxes wanted and those booked (0 when left out).
        public List<(string ItemName, int Wanted, int Booked)> Cut { get; } = new List<(string, int, int)>();
        // Items the commissary doesn't sell that day (no game item or no price): left out.
        public List<string> NotSold { get; } = new List<string>();
        // No vehicle has its space at the commissary any more: nothing booked that day. TellNoSpace: the first such day in
        // a row, the one to text.
        public bool NoSpace { get; internal set; }
        public bool TellNoSpace { get; internal set; }
        // Of NotSold, those not texted yet while they stay unsold.
        public List<string> TellNotSold { get; } = new List<string>();
        // Delivery days that passed without a booking run (the game wasn't running): skipped, not delivered late.
        public int Missed { get; internal set; }
    }

    // Standing orders (Peter, 14 October 2026): a phone order can repeat every day or every week on the weekday of its
    // first delivery. Each due day, at the delivery minute, it books that day's delivery at that day's prices, goods
    // plus the fee, and Commissaries.Deliver pays for it on delivery like any order. A refused payment cancels that one
    // delivery; the standing order stays. The game side shows the conversation and moves the money.
    public static class CommissaryStandingOrders
    {
        public static int Interval(CommissaryRepeat repeat) => repeat == CommissaryRepeat.Weekly ? 7 : 1;

        public static bool DeliversOn(CommissaryStandingOrder standing, int day)
        {
            if (standing == null) throw new ArgumentNullException(nameof(standing));
            return day >= standing.NextDay && (day - standing.NextDay) % Interval(standing.Repeat) == 0;
        }

        // Deliveries it has still to book on days first to last.
        public static int DeliveriesIn(CommissaryStandingOrder standing, int first, int last)
        {
            if (standing == null) throw new ArgumentNullException(nameof(standing));
            var interval = Interval(standing.Repeat);
            var from = Math.Max(first, standing.NextDay);
            if (last < from) return 0;
            var start = standing.NextDay + (from - standing.NextDay + interval - 1) / interval * interval;
            return start > last ? 0 : (last - start) / interval + 1;
        }

        // The weekday a weekly order delivers on.
        public static DayOfWeek Weekday(CommissaryStandingOrder standing) => GameDays.Weekday(standing.NextDay);

        // A confirmed order that repeats becomes a standing order; its first delivery is tomorrow, as a one-off order's,
        // or `firstDay` when the phone named that day in its question and midnight passed since (then on that weekday, the
        // first such day from today). The same checks as a one-off order: the commissary takes orders, the quote has no
        // problem, and its food fits under each item's weekly limit (CommissarySupply.Check).
        public static CommissaryStandingOrder Create(CommissaryState state, CommissaryRules rules, string commissaryId,
            CommissaryQuote quote, CommissaryRepeat repeat, (int Day, int Minute) now, bool unlimited = false, int? firstDay = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (quote == null) throw new ArgumentNullException(nameof(quote));
            if (repeat == CommissaryRepeat.Once || !Enum.IsDefined(typeof(CommissaryRepeat), repeat)) throw new ArgumentOutOfRangeException(nameof(repeat));
            if (now.Day < 1 || now.Minute < 0 || now.Minute > 1439) throw new ArgumentOutOfRangeException(nameof(now));
            if (!CommissaryOrdering.CanOrder(state, commissaryId)) throw new InvalidOperationException("Ordering requires an unlocked commissary with a rented space.");
            if (quote.Problem != CommissaryQuoteProblem.None) throw new InvalidOperationException($"The order can't be placed: {quote.Problem}.");
            var first = FirstDay(repeat, now.Day, firstDay);
            if (!CommissarySupply.Check(state, rules, commissaryId, quote, repeat, first - 1, unlimited).Fits)
                throw new InvalidOperationException("The order goes over a food's weekly limit at the commissary.");
            var standing = new CommissaryStandingOrder
            {
                Id = state.NextStandingId,
                CommissaryId = commissaryId,
                Repeat = repeat,
                CreatedDay = now.Day,
                NextDay = first,
                Lines = quote.Lines.Select(l => new CommissaryStandingLine { ItemName = l.ItemName, Boxes = l.Boxes, Capped = l.Capped }).ToList()
            };
            state.NextStandingId = checked(state.NextStandingId + 1);
            state.StandingOrders.Add(standing);
            return standing;
        }

        // The first delivery day: `named` (the day the phone's question named) moved on by whole intervals to today at the
        // earliest, so a weekly order keeps the weekday the player chose; tomorrow without one.
        public static int FirstDay(CommissaryRepeat repeat, int today, int? named)
        {
            if (today < 1) throw new ArgumentOutOfRangeException(nameof(today));
            if (named == null) return checked(today + 1);
            if (named.Value < 1) throw new ArgumentOutOfRangeException(nameof(named));
            var day = named.Value;
            var interval = Interval(repeat);
            while (day < today) day = checked(day + interval);
            return day;
        }

        public static bool Cancel(CommissaryState state, long id)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.StandingOrders.RemoveAll(s => s.Id == id) > 0;
        }

        public static IReadOnlyList<CommissaryStandingOrder> At(CommissaryState state, string commissaryId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.StandingOrders.Where(s => s.CommissaryId == commissaryId).OrderBy(s => s.Id).ToList();
        }

        // Books every standing order due by now: its delivery day has come and the delivery minute has passed. Call it
        // before Commissaries.Deliver in the same step, so the booked orders arrive and are paid at once. Earliest-created
        // first; each sees the week's boxes of each food already delivered or booked (one-off orders, earlier standing
        // orders, its own earlier days) and takes whole boxes of its food lines while that food's weekly limit allows;
        // equipment always goes. Prices come from `price` (null: not sold that day, left out). Delivery days that passed
        // without a run are skipped.
        public static IReadOnlyList<CommissaryStandingBooking> Book(CommissaryState state, CommissaryRules rules, int nowDay, int nowMinute,
            Func<string, CommissaryPrice?> price, bool unlimited = false)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (price == null) throw new ArgumentNullException(nameof(price));
            if (nowDay < 1 || nowMinute < 0 || nowMinute > 1439) throw new ArgumentOutOfRangeException(nameof(nowDay));
            var runs = new List<CommissaryStandingBooking>();
            foreach (var standing in state.StandingOrders.OrderBy(s => s.Id).ToList())
            {
                if (standing.NextDay > nowDay) continue;
                var missed = 0;
                var interval = Interval(standing.Repeat);
                while (standing.NextDay < nowDay)
                {
                    standing.NextDay = checked(standing.NextDay + interval);
                    missed++;
                }
                if (standing.NextDay > nowDay || nowMinute < rules.DeliveryMinute)
                {
                    if (missed > 0) runs.Add(new CommissaryStandingBooking(standing, nowDay) { Missed = missed });
                    continue;
                }
                var run = new CommissaryStandingBooking(standing, nowDay) { Missed = missed };
                runs.Add(run);
                BookOne(state, rules, standing, run, nowDay, nowMinute, price, unlimited);
            }
            return runs;
        }

        // Ctrl+Alt+F3 (debug): the next delivery of a standing order due by tomorrow is booked now, for delivery today, and
        // its schedule goes on from its own day as if it had come then: no extra delivery, no change to its days.
        public static CommissaryStandingBooking BookEarly(CommissaryState state, CommissaryRules rules, CommissaryStandingOrder standing,
            int nowDay, int nowMinute, Func<string, CommissaryPrice?> price, bool unlimited = false)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (standing == null) throw new ArgumentNullException(nameof(standing));
            if (price == null) throw new ArgumentNullException(nameof(price));
            if (nowDay < 1 || nowMinute < 0 || nowMinute > 1439) throw new ArgumentOutOfRangeException(nameof(nowDay));
            if (standing.NextDay < nowDay || standing.NextDay > nowDay + 1) throw new InvalidOperationException("Only a delivery due today or tomorrow is brought forward.");
            var run = new CommissaryStandingBooking(standing, nowDay);
            BookOne(state, rules, standing, run, nowDay, nowMinute, price, unlimited);
            return run;
        }

        // One delivery of a standing order, for nowDay: its next day moves on by one interval. With no space, nothing is
        // booked; the run says to text it only the first time in a row (TellNoSpace), and items not sold only once each
        // while they stay unsold (TellNotSold).
        private static void BookOne(CommissaryState state, CommissaryRules rules, CommissaryStandingOrder standing, CommissaryStandingBooking run,
            int nowDay, int nowMinute, Func<string, CommissaryPrice?> price, bool unlimited)
        {
            standing.NextDay = checked(standing.NextDay + Interval(standing.Repeat));
            if (!CommissaryOrdering.CanOrder(state, standing.CommissaryId))
            {
                run.NoSpace = true;
                run.TellNoSpace = !standing.NoSpaceTold;
                standing.NoSpaceTold = true;
                return;
            }
            standing.NoSpaceTold = false;
            var requests = new List<CommissaryOrderRequest>();
            foreach (var line in standing.Lines)
            {
                var today = price(line.ItemName);
                if (today == null)
                {
                    run.NotSold.Add(line.ItemName);
                    if (!standing.NotSoldTold.Contains(line.ItemName))
                    {
                        run.TellNotSold.Add(line.ItemName);
                        standing.NotSoldTold.Add(line.ItemName);
                    }
                    continue;
                }
                standing.NotSoldTold.Remove(line.ItemName);
                var boxes = line.Boxes;
                if (line.Capped && !unlimited)
                {
                    // Only boxes delivered or booked count: deliveries not booked yet (this order's later days, other
                    // standing orders) don't hold back today's, so the earliest booking gets the boxes.
                    var booked = Booked(state, standing.CommissaryId, line.ItemName, nowDay)
                                 + requests.Where(r => r.ItemName == line.ItemName && r.Capped).Sum(r => r.Boxes);
                    boxes = Math.Max(0, Math.Min(boxes, rules.FoodBoxLimit(line.ItemName) - booked));
                    if (boxes < line.Boxes) run.Cut.Add((line.ItemName, line.Boxes, boxes));
                }
                if (boxes > 0) requests.Add(new CommissaryOrderRequest(line.ItemName, today.Value.BoxSize, today.Value.UnitPriceCents, boxes, line.Capped));
            }
            // Each line becomes one order, priced as the phone form prices it (one fee per line).
            var quote = CommissaryOrdering.Quote(rules, requests);
            foreach (var line in quote.Lines)
                run.Orders.Add(Commissaries.Book(state, rules, standing.CommissaryId, line.ItemName, line.Units, line.UnitPriceCents,
                    (nowDay, nowMinute), nowDay, line.CappedBoxes, standing.Id));
        }

        // Boxes of the item delivered or booked (not yet delivered) in the week of `day`.
        private static int Booked(CommissaryState state, string commissaryId, string itemName, int day)
        {
            var week = CommissarySupply.WeekStart(day);
            return state.WeekBoxes.Where(w => w.CommissaryId == commissaryId && w.ItemName == itemName && w.WeekStart == week).Sum(w => w.Boxes)
                   + state.Orders.Where(o => o.CommissaryId == commissaryId && o.ItemName == itemName && CommissarySupply.WeekStart(o.DeliverDay) == week).Sum(o => o.CappedBoxes);
        }
    }
}
