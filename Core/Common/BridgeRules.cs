using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Common
{
    public static class GroundPick
    {
        public const double ExplicitReach = 1.5;

        public static (int Index, string Why) Choose(IReadOnlyList<(double Y, bool Walkable)> surfaces, double referenceY, double? explicitY,
            bool walkableOnly = false)
        {
            if (surfaces == null || surfaces.Count == 0) throw new ArgumentException("No ground surfaces to choose from.", nameof(surfaces));
            var all = Enumerable.Range(0, surfaces.Count).ToList();
            string Heights() => string.Join(", ", surfaces.Select(s =>
                s.Y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + (s.Walkable ? " walkable" : " not walkable")));
            int Nearest(IEnumerable<int> indexes, double y) => indexes.OrderBy(i => Math.Abs(surfaces[i].Y - y)).ThenBy(i => surfaces[i].Y).First();
            if (explicitY != null)
            {
                var y = explicitY.Value;
                var near = all.Where(i => Math.Abs(surfaces[i].Y - y) <= ExplicitReach && (!walkableOnly || surfaces[i].Walkable)).ToList();
                var what = walkableOnly ? "walkable surface" : "surface";
                if (near.Count == 0)
                    return (-1, FormattableString.Invariant($"no {what} within {ExplicitReach:F1} m of the y asked for ({y:F2}); surfaces there: {Heights()}"));
                return (Nearest(near, y), FormattableString.Invariant($"the {what} nearest the y asked for ({y:F2}) of {surfaces.Count}: {Heights()}"));
            }
            var walkable = all.Where(i => surfaces[i].Walkable).ToList();
            // The player goes only where people can walk: with no walkable surface the move is refused, even for the
            // only surface there (review round 10, Astra and Sol: a disconnected deck slipped through as the fallback).
            if (walkableOnly && walkable.Count == 0)
                return (-1, FormattableString.Invariant($"no walkable surface of {surfaces.Count}: {Heights()}"));
            if (surfaces.Count == 1) return (0, surfaces[0].Walkable ? "the only ground surface, walkable" : "the only ground surface, not walkable");
            if (walkable.Count > 0)
                return (Nearest(walkable, referenceY), FormattableString.Invariant($"the walkable surface nearest the mover's height ({referenceY:F2}) of {surfaces.Count}: {Heights()}"));
            return (Nearest(all, referenceY), FormattableString.Invariant($"the surface nearest the mover's height ({referenceY:F2}) of {surfaces.Count}, none walkable: {Heights()}"));
        }
    }

    public static class NeedsTopUp
    {
        public const double Below = 40;

        public static bool Due(double energy, double hunger) => energy < Below || hunger < Below;
    }

    public static class ShotClearance
    {
        public const double Margin = 0.4;

        public static double? Fit(double wanted, double? firstHit, double minimum)
        {
            if (firstHit == null || firstHit.Value >= wanted + Margin) return wanted;
            var room = firstHit.Value - Margin;
            return room >= minimum ? room : (double?)null;
        }
    }
}
