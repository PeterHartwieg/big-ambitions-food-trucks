using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Common
{
    // Choices the test bridge makes (docs/test-bridge.md), kept here so they are tested without the game.

    // Which ground surface a teleport lands on, of the ones stacked above a point (a street under an elevated road).
    // Live session 4 (13 October 2026): the highest surface put the player on the elevated highway.
    public static class GroundPick
    {
        // How far an explicit y may be from the surface it asks for.
        public const double ExplicitReach = 1.5;

        // `surfaces`: each surface's height and whether people can walk there (NavMesh near it). An explicit y takes
        // the surface nearest it within ExplicitReach, walkable when `walkableOnly` (the player); otherwise a walkable
        // surface nearest the mover's own height, then (not for `walkableOnly`) any surface nearest it. Returns the index,
        // or -1 when there is no such surface, and a few words on why, for the reply. Live session 5: the reply said "nearest the
        // mover's height (0.09) of 2: 3.22, 0.20" and took 3.22, because the street at 0.20 read as not walkable; the
        // heights are now listed with which are walkable, and an explicit y with nothing walkable near it is refused.
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

    // Keeping the player's needs up while the bridge runs the clock fast (live session 4: after two game days of
    // run_until the character collapsed, the clock jumped to 08:00 the next day and the hospital charged $2,000).
    public static class NeedsTopUp
    {
        // Topped up when energy or hunger falls below this (the game's scale is 0 to 100; it hospitalises at -20).
        public const double Below = 40;

        public static bool Due(double energy, double hunger) => energy < Below || hunger < Below;
    }

    // How far out a shot's camera may stand along its line from the subject, given the first solid thing on that line.
    public static class ShotClearance
    {
        // Metres kept between the camera and the first hit.
        public const double Margin = 0.4;

        // `wanted`: the pose's distance; `firstHit`: the distance to the first solid thing from the subject, null when
        // the line is clear; `minimum`: the nearest the pose still shows its subject. The distance to use, or null when
        // the line is blocked nearer than that (then the next way round is tried).
        public static double? Fit(double wanted, double? firstHit, double minimum)
        {
            if (firstHit == null || firstHit.Value >= wanted + Margin) return wanted;
            var room = firstHit.Value - Margin;
            return room >= minimum ? room : (double?)null;
        }
    }
}
