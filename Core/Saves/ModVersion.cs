using System;
using System.Text.RegularExpressions;

namespace FoodTrucks.Core.Saves
{
    public static class ModVersion
    {
        private static readonly Regex pattern = new Regex(
            @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
            RegexOptions.CultureInvariant);

        public static int Compare(string? a, string? b)
        {
            var first = Parse(a);
            var second = Parse(b);
            if (first == null) return second == null ? 0 : -1;
            if (second == null) return 1;

            for (var i = 1; i <= 3; i++)
            {
                var comparison = CompareNumeric(first.Groups[i].Value, second.Groups[i].Value);
                if (comparison != 0) return comparison;
            }

            var firstPrerelease = first.Groups[4];
            var secondPrerelease = second.Groups[4];
            if (!firstPrerelease.Success) return secondPrerelease.Success ? 1 : 0;
            if (!secondPrerelease.Success) return -1;

            var firstIdentifiers = firstPrerelease.Value.Split('.');
            var secondIdentifiers = secondPrerelease.Value.Split('.');
            for (var i = 0; i < Math.Min(firstIdentifiers.Length, secondIdentifiers.Length); i++)
            {
                var firstIdentifier = firstIdentifiers[i];
                var secondIdentifier = secondIdentifiers[i];
                var firstNumeric = IsNumeric(firstIdentifier);
                var secondNumeric = IsNumeric(secondIdentifier);
                var comparison = firstNumeric && secondNumeric
                    ? CompareNumeric(firstIdentifier, secondIdentifier)
                    : firstNumeric != secondNumeric
                        ? (firstNumeric ? -1 : 1)
                        : string.CompareOrdinal(firstIdentifier, secondIdentifier);
                if (comparison != 0) return comparison;
            }
            return firstIdentifiers.Length.CompareTo(secondIdentifiers.Length);
        }

        public static bool IsValid(string? version) => Parse(version) != null;

        public static bool IsNewerThan(string? candidate, string current) => Compare(candidate, current) > 0;

        private static Match? Parse(string? version)
        {
            if (string.IsNullOrEmpty(version)) return null;
            var match = pattern.Match(version);
            if (!match.Success) return null;
            if (match.Groups[4].Success)
            {
                foreach (var identifier in match.Groups[4].Value.Split('.'))
                {
                    if (IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0')
                        return null;
                }
            }
            return match;
        }

        private static int CompareNumeric(string a, string b)
        {
            var comparison = a.Length.CompareTo(b.Length);
            return comparison != 0 ? comparison : string.CompareOrdinal(a, b);
        }

        private static bool IsNumeric(string identifier)
        {
            foreach (var character in identifier)
            {
                if (character < '0' || character > '9') return false;
            }
            return true;
        }
    }
}
