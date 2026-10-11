using System;
using System.Linq;

namespace FoodTrucks.Core.Commissary
{
    // The city's parking lanes as last found (review round 5: 0.9.9 cached an empty first lookup for good, so a bay that
    // waited for the lanes never resolved). Found again when there is no lookup yet or one of the lanes found is gone,
    // and, while none is active, at most every RefreshSeconds until they are.
    public sealed class LaneCache<T> where T : class
    {
        public const double RefreshSeconds = 5;
        private T[]? items;
        private double nextRefresh = double.MinValue;

        public int Lookups { get; private set; }

        // Whether any lane is there and active, finding them again when due. `now` is in seconds.
        public bool Ready(double now, Func<T[]> find, Func<T, bool> alive, Func<T, bool> active)
        {
            if (find == null) throw new ArgumentNullException(nameof(find));
            if (alive == null) throw new ArgumentNullException(nameof(alive));
            if (active == null) throw new ArgumentNullException(nameof(active));
            var stale = items == null || items.Any(i => i == null || !alive(i));
            var none = items == null || !items.Any(i => i != null && alive(i) && active(i));
            if (stale || (none && now >= nextRefresh))
            {
                items = find() ?? Array.Empty<T>();
                Lookups++;
                nextRefresh = now + RefreshSeconds;
            }
            return items!.Any(i => i != null && alive(i) && active(i));
        }

        public void Clear()
        {
            items = null;
            nextRefresh = double.MinValue;
        }
    }
}
