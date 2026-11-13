using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;

namespace FoodTrucks.Core.Commissary
{
    public static class CommissarySupply
    {
        public static int WeekStart(int day)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            return day - (day - 1) % 7;
        }

        public static int Used(CommissaryState state, string itemName, int day)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var week = WeekStart(day);
            long used = state.WeekBoxes.Where(w => w.ItemName == itemName && w.WeekStart == week).Sum(w => (long)w.Boxes);
            used += state.Orders.Where(o => o.ItemName == itemName && WeekStart(o.LimitDay) == week).Sum(o => (long)o.CappedBoxes);
            foreach (var standing in state.StandingOrders)
            {
                var boxes = standing.Lines.Where(l => l.Capped && l.ItemName == itemName).Sum(l => (long)l.Boxes);
                if (boxes > 0) used += boxes * CommissaryStandingOrders.DeliveriesIn(standing, week, week + 6);
            }
            return (int)Math.Min(int.MaxValue, used);
        }

        public static int Left(CommissaryState state, CommissaryRules rules, string itemName, int day, bool unlimited = false)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (unlimited) return int.MaxValue;
            return Math.Max(0, rules.FoodBoxLimit(itemName) - Used(state, itemName, day));
        }

        public static CommissaryCapCheck Check(CommissaryState state, CommissaryRules rules,
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
                    var left = Left(state, rules, line.ItemName, week);
                    if (needed > left) return new CommissaryCapCheck(false, line.ItemName, week, left, (int)Math.Min(int.MaxValue, needed));
                }
            return CommissaryCapCheck.Fine(first);
        }

        internal static void Delivered(CommissaryState state, CommissaryOrder order, int today)
        {
            var current = WeekStart(today);
            state.WeekBoxes.RemoveAll(w => w.WeekStart < current);
            if (order.CappedBoxes <= 0) return;
            var week = WeekStart(order.LimitDay);
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
        public string? ItemName { get; }
        public int WeekStart { get; }
        public int Left { get; }
        public int Needed { get; }
    }

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

    public sealed class CommissaryStandingBooking
    {
        internal CommissaryStandingBooking(CommissaryStandingOrder standing, int day)
        {
            Standing = standing;
            Day = day;
        }

        public CommissaryStandingOrder Standing { get; }
        public int Day { get; }
        public List<CommissaryOrder> Orders { get; } = new List<CommissaryOrder>();
        public List<(string ItemName, int Wanted, int Booked)> Cut { get; } = new List<(string, int, int)>();
        public List<string> NotSold { get; } = new List<string>();
        public bool NoSpace { get; internal set; }
        public bool TellNoSpace { get; internal set; }
        public List<string> TellNotSold { get; } = new List<string>();
        public int Missed { get; internal set; }
    }

    public static class CommissaryStandingOrders
    {
        public static int Interval(CommissaryRepeat repeat) => repeat == CommissaryRepeat.Weekly ? 7 : 1;

        public static bool DeliversOn(CommissaryStandingOrder standing, int day)
        {
            if (standing == null) throw new ArgumentNullException(nameof(standing));
            return day >= standing.NextDay && (day - standing.NextDay) % Interval(standing.Repeat) == 0;
        }

        public static int DeliveriesIn(CommissaryStandingOrder standing, int first, int last)
        {
            if (standing == null) throw new ArgumentNullException(nameof(standing));
            var interval = Interval(standing.Repeat);
            var from = Math.Max(first, standing.NextDay);
            if (last < from) return 0;
            var start = standing.NextDay + (from - standing.NextDay + interval - 1) / interval * interval;
            return start > last ? 0 : (last - start) / interval + 1;
        }

        public static DayOfWeek Weekday(CommissaryStandingOrder standing) => GameDays.Weekday(standing.NextDay);

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
            if (!CommissarySupply.Check(state, rules, quote, repeat, first - 1, unlimited).Fits)
                throw new InvalidOperationException("The order goes over a food's weekly limit on phone orders.");
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
            BookOne(state, rules, standing, run, nowDay, nowMinute, price, unlimited, countDay: standing.NextDay);
            return run;
        }

        private static void BookOne(CommissaryState state, CommissaryRules rules, CommissaryStandingOrder standing, CommissaryStandingBooking run,
            int nowDay, int nowMinute, Func<string, CommissaryPrice?> price, bool unlimited, int? countDay = null)
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
                    var booked = Booked(state, line.ItemName, countDay ?? nowDay)
                                 + requests.Where(r => r.ItemName == line.ItemName && r.Capped).Sum(r => r.Boxes);
                    boxes = Math.Max(0, Math.Min(boxes, rules.FoodBoxLimit(line.ItemName) - booked));
                    if (boxes < line.Boxes) run.Cut.Add((line.ItemName, line.Boxes, boxes));
                }
                if (boxes > 0) requests.Add(new CommissaryOrderRequest(line.ItemName, today.Value.BoxSize, today.Value.UnitPriceCents, boxes, line.Capped));
            }
            var quote = CommissaryOrdering.Quote(rules, requests);
            foreach (var line in quote.Lines)
                run.Orders.Add(Commissaries.Book(state, rules, standing.CommissaryId, line.ItemName, line.Units, line.UnitPriceCents,
                    (nowDay, nowMinute), nowDay, line.CappedBoxes, standing.Id, countDay));
        }

        private static int Booked(CommissaryState state, string itemName, int day)
        {
            var week = CommissarySupply.WeekStart(day);
            return state.WeekBoxes.Where(w => w.ItemName == itemName && w.WeekStart == week).Sum(w => w.Boxes)
                   + state.Orders.Where(o => o.ItemName == itemName && CommissarySupply.WeekStart(o.LimitDay) == week).Sum(o => o.CappedBoxes);
        }
    }
}
