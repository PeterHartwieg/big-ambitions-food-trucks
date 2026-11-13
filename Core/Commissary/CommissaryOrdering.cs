using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Commissary
{
    public sealed class CommissaryOrderRequest
    {
        public CommissaryOrderRequest(string itemName, int boxSize, long unitPriceCents, int boxes, bool capped = true)
        {
            if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("An item name is required.", nameof(itemName));
            if (boxSize <= 0) throw new ArgumentOutOfRangeException(nameof(boxSize));
            if (unitPriceCents < 0) throw new ArgumentOutOfRangeException(nameof(unitPriceCents));
            if (boxes < 0) throw new ArgumentOutOfRangeException(nameof(boxes));
            ItemName = itemName;
            BoxSize = boxSize;
            UnitPriceCents = unitPriceCents;
            Boxes = boxes;
            Capped = capped;
        }

        public string ItemName { get; }
        public int BoxSize { get; }
        public long UnitPriceCents { get; }
        public int Boxes { get; }
        public bool Capped { get; }
    }

    public sealed class CommissaryQuoteLine
    {
        internal CommissaryQuoteLine(CommissaryOrderRequest request)
        {
            ItemName = request.ItemName;
            Boxes = request.Boxes;
            Capped = request.Capped;
            BoxSize = request.BoxSize;
            UnitPriceCents = request.UnitPriceCents;
            Units = checked(request.Boxes * request.BoxSize);
            GoodsCents = checked(Units * request.UnitPriceCents);
        }

        public string ItemName { get; }
        public int Boxes { get; }
        public bool Capped { get; }
        public int CappedBoxes => Capped ? Boxes : 0;
        public int BoxSize { get; }
        public long UnitPriceCents { get; }
        public int Units { get; }
        public long BoxPriceCents => checked(BoxSize * UnitPriceCents);
        public long GoodsCents { get; }
    }

    public enum CommissaryQuoteProblem
    {
        None,
        Empty,
        TooManyBoxes
    }

    public sealed class CommissaryQuote
    {
        internal CommissaryQuote(IReadOnlyList<CommissaryQuoteLine> lines, long feePerDeliveryCents, bool sharedDelivery)
        {
            Lines = lines;
            Boxes = lines.Sum(l => l.Boxes);
            CappedBoxes = lines.Sum(l => l.CappedBoxes);
            GoodsCents = lines.Aggregate(0L, (sum, l) => checked(sum + l.GoodsCents));
            FeePerDeliveryCents = feePerDeliveryCents;
            SharedDelivery = sharedDelivery;
            FeeCents = lines.Count == 0 || sharedDelivery ? 0 : feePerDeliveryCents;
            TotalCents = checked(GoodsCents + FeeCents);
            Problem = lines.Count == 0 ? CommissaryQuoteProblem.Empty
                : Boxes > CommissaryOrdering.MaxBoxesPerOrder ? CommissaryQuoteProblem.TooManyBoxes
                : CommissaryQuoteProblem.None;
        }

        public IReadOnlyList<CommissaryQuoteLine> Lines { get; }
        public int Boxes { get; }
        public int CappedBoxes { get; }
        public long GoodsCents { get; }
        public long FeePerDeliveryCents { get; }
        public bool SharedDelivery { get; }
        public long FeeCents { get; }
        public long TotalCents { get; }
        public CommissaryQuoteProblem Problem { get; }
    }

    public static class CommissaryOrdering
    {
        public const int MaxBoxesPerOrder = 10;

        public static bool CanOrder(CommissaryState state, string commissaryId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(commissaryId)) return false;
            return state.Unlocked.Contains(commissaryId, StringComparer.Ordinal) && state.Spaces.Any(s => s.CommissaryId == commissaryId);
        }

        public static bool SharesDelivery(CommissaryState state, string commissaryId, int day)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.Orders.Any(o => o.CommissaryId == commissaryId && o.DeliverDay == day) ||
                   state.StandingOrders.Any(s => s.CommissaryId == commissaryId && CommissaryStandingOrders.DeliversOn(s, day));
        }

        public static CommissaryQuote Quote(CommissaryRules rules, IEnumerable<CommissaryOrderRequest> requests, bool sharedDelivery = false)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            var merged = new List<CommissaryOrderRequest>();
            foreach (var request in requests)
            {
                if (request == null) throw new ArgumentException("A request is null.", nameof(requests));
                if (request.Boxes == 0) continue;
                var index = merged.FindIndex(m => m.ItemName == request.ItemName);
                if (index < 0)
                {
                    merged.Add(request);
                    continue;
                }
                var same = merged[index];
                if (same.BoxSize != request.BoxSize || same.UnitPriceCents != request.UnitPriceCents || same.Capped != request.Capped)
                    throw new ArgumentException($"{request.ItemName} is listed twice at different box sizes or prices.", nameof(requests));
                merged[index] = new CommissaryOrderRequest(same.ItemName, same.BoxSize, same.UnitPriceCents, checked(same.Boxes + request.Boxes), same.Capped);
            }
            var lines = new List<CommissaryQuoteLine>(merged.Count);
            foreach (var request in merged) lines.Add(new CommissaryQuoteLine(request));
            return new CommissaryQuote(lines, rules.DeliveryFeeCents, sharedDelivery);
        }

        public static IReadOnlyList<CommissaryOrder> Place(CommissaryState state, CommissaryRules rules, string commissaryId,
            CommissaryQuote quote, (int Day, int Minute) now, bool unlimited = false)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (quote == null) throw new ArgumentNullException(nameof(quote));
            if (!CanOrder(state, commissaryId)) throw new InvalidOperationException("Ordering requires an unlocked commissary with a rented space.");
            if (quote.Problem != CommissaryQuoteProblem.None) throw new InvalidOperationException($"The order can't be placed: {quote.Problem}.");
            if (now.Day < 1) throw new ArgumentOutOfRangeException(nameof(now));
            if (!CommissarySupply.Check(state, rules, quote, CommissaryRepeat.Once, now.Day, unlimited).Fits)
                throw new InvalidOperationException("The order goes over a food's weekly limit on phone orders.");
            _ = checked(state.NextOrderId + quote.Lines.Count);
            var orders = new List<CommissaryOrder>(quote.Lines.Count);
            var deliverDay = checked(now.Day + 1);
            foreach (var line in quote.Lines)
                orders.Add(Commissaries.Book(state, rules, commissaryId, line.ItemName, line.Units, line.UnitPriceCents, now, deliverDay, line.CappedBoxes, null));
            return orders;
        }
    }
}
