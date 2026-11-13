using System;

namespace FoodTrucks.Core.Common
{
    // SplitMix64: a small generator whose whole state is one 64-bit number, so it can live in a save.
    // System.Random can't be saved. The integer draws are the same on every runtime; Poisson uses
    // Math.Exp, which Mono and .NET may round differently in the last bit, so a draw can rarely differ
    // between the tests and the game. That changes which customers come, never what a save holds.
    public sealed class SeededRandom
    {
        private ulong state;

        public SeededRandom(long state) => this.state = unchecked((ulong)state);

        public long State => unchecked((long)state);

        public ulong NextULong()
        {
            unchecked
            {
                var z = state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        public bool Chance(double probability) => probability > 0 && NextDouble() < probability;

        public int Poisson(double mean)
        {
            if (double.IsNaN(mean) || mean <= 0) return 0;
            if (mean > 60) throw new ArgumentOutOfRangeException(nameof(mean), "Poisson draws are for means of 60 or less.");
            var limit = Math.Exp(-mean);
            var count = 0;
            var product = NextDouble();
            while (product > limit)
            {
                count++;
                product *= NextDouble();
            }
            return count;
        }

        public static long SeedFor(string text, long number)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            unchecked
            {
                var hash = 0xCBF29CE484222325UL;
                foreach (var c in text)
                {
                    hash = (hash ^ c) * 0x100000001B3UL;
                }
                for (var i = 0; i < 8; i++)
                {
                    hash = (hash ^ (byte)(number >> (8 * i))) * 0x100000001B3UL;
                }
                return (long)hash;
            }
        }
    }
}
