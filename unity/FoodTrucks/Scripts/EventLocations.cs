#nullable enable
using System;
using System.Linq;
using System.Globalization;
using Entities;
using System.Collections.Generic;
using UI.Notification;
using Streets;
using UI.Guiders;
using System.Text.RegularExpressions;

namespace FoodTrucks
{
    // Street IDs are data IDs, not translation keys. Use the same building and formatter as CityMapFilters.
    internal static class EventLocations
    {
        internal static bool Ready => AddressHelper.StreetDataNames.Any() && World.AllBuildings().Count > 0;

        internal static string? ReadableAddress(string addressKey)
        {
            try { return ReadableAddress(World.BuildingAt(addressKey)); }
            catch (Exception) { return null; }
        }

        internal static string? ReadableAddress(CityBuildingController? controller)
        {
            var address = controller?.building?.Address;
            if (address == null || address.streetNumber <= 0) return null;
            try
            {
                var street = address.GetStreetNameLocalized();
                if (InvalidStreet(street)) return null;
                return address.ToFormattedString();
            }
            catch (Exception) { return null; }
        }

        internal static string Name(string addressKey) => ReadableAddress(addressKey)
            ?? CityPitches.Text("foodtrucks:event_location_unavailable");

        // A present building with a name still loading is unknown, not a cancelled destination.
        internal static bool CanJudgeLocation(string addressKey)
        {
            try
            {
                var controller = World.BuildingAt(addressKey);
                return controller == null || !InvalidStreet(controller.building?.Address?.GetStreetNameLocalized());
            }
            catch (Exception) { return false; }
        }

        // History can outlive both a gig and its building. StreetData IDs use AddressHelper's
        // streetLocalization lookup, not Localizor's translation-key lookup.
        internal static string? HistoricalAddress(string? value) => HistoricalAddress(value, out _);

        private static string? HistoricalAddress(string? value, out bool fallback, StreetSnapshot? streets = null)
        {
            fallback = false;
            if (string.IsNullOrWhiteSpace(value)) return null;
            var match = Regex.Match(value.Trim(), @"\A(?:(ba:street_[a-z0-9]+)#([0-9]+)|([0-9]+)\s+#?(ba:street_[a-z0-9]+))\z", RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            var streetKey = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[4].Value;
            var numberText = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0) return null;
            string? street = null;
            try { street = streets == null ? AddressHelper.GetStreetNameLocalized(streetKey) : streets.NativeName(streetKey); }
            catch (Exception) { /* Historical text needs no surviving StreetData or building. */ }
            if (InvalidStreet(street))
            {
                fallback = true;
                street = FallbackStreet(streetKey.Substring("ba:street_".Length));
            }
            return street == null ? null : number.ToString(CultureInfo.InvariantCulture) + " " + street;
        }

        private static bool InvalidStreet(string? street) => string.IsNullOrWhiteSpace(street) ||
            street!.IndexOfAny(new[] { ':', '#', '{', '}', '<', '>' }) >= 0;

        // Native names can arrive after buildings/StreetData. Include that change even when gigs do not change.
        internal static string ResolutionState(IEnumerable<string> locations, StreetSnapshot streets) =>
            (streets.HasStreets && World.AllBuildings().Count > 0) + ":" + streets.Fingerprint + ":" + string.Join(",",
            locations.Distinct().OrderBy(p => p, StringComparer.Ordinal).Select(p =>
            {
                var address = HistoricalAddress(p, out var fallback, streets);
                return p + "=" + (fallback ? "?" : address);
            }));

        private static string? FallbackStreet(string key)
        {
            var prefix = "";
            foreach (var direction in new[] { "west", "east", "north", "south" })
                if (key.StartsWith(direction, StringComparison.Ordinal) &&
                    Regex.IsMatch(key.Substring(direction.Length), @"\A(?:broadway(?:street)?|(?:[0-9]+(?:st|nd|rd|th)|first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|thirteenth|fourteenth|fifteenth|sixteenth|seventeenth|eighteenth|nineteenth|twentieth|(?:twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?:first|second|third|fourth|fifth|sixth|seventh|eighth|ninth))(?:avenue|street|road|drive|lane))\z"))
                { prefix = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(direction) + " "; key = key.Substring(direction.Length); break; }
            if (key == "broadway" || key == "broadwaystreet") return prefix + "Broadway";
            var ordinals = new[] { "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth",
                "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth", "eighteenth", "nineteenth", "twentieth" };
            foreach (var ending in new[] { "avenue", "street", "road", "drive", "lane" })
            {
                if (!key.EndsWith(ending, StringComparison.Ordinal) || key.Length <= ending.Length) continue;
                var name = key.Substring(0, key.Length - ending.Length);
                var n = Array.IndexOf(ordinals, name) + 1;
                if (n == 0)
                {
                    var numeric = Regex.Match(name, @"\A([0-9]+)(?:st|nd|rd|th)\z");
                    if (numeric.Success) int.TryParse(numeric.Groups[1].Value, out n);
                    // Common written compound ordinals, e.g. twentyfirst / fortysecond.
                    foreach (var tens in new[] { ("twenty", 20), ("thirty", 30), ("forty", 40), ("fifty", 50), ("sixty", 60), ("seventy", 70), ("eighty", 80), ("ninety", 90) })
                        if (name.StartsWith(tens.Item1, StringComparison.Ordinal))
                        { var unit = Array.IndexOf(ordinals, name.Substring(tens.Item1.Length)) + 1; if (unit > 0 && unit < 10) n = tens.Item2 + unit; }
                }
                if (n > 0)
                {
                    var suffix = n % 100 >= 11 && n % 100 <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th";
                    name = n.ToString(CultureInfo.InvariantCulture) + suffix;
                }
                else if (!Regex.IsMatch(name, @"\A[a-z]+\z")) return null;
                else name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
                return prefix + name + " " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(ending);
            }
            return null;
        }

        internal static void RefreshCateringHistory(Contact contact, IEnumerable<Dictionary<string, string>> activeGigs,
            IEnumerable<Dictionary<string, string>>? acceptedGigs = null, IEnumerable<Dictionary<string, string>>? openOffers = null,
            int day = 0, bool repairAddresses = true, StreetSnapshot? streets = null)
        {
            if (!FoodTrucksSession.CanWrite) return;
            streets ??= CaptureStreets();
            var changed = false;
            List<Dictionary<string, string>>? active = null, accepted = null, open = null;
            try
            {
                foreach (var message in contact.messagesQueue)
                {
                    // Native contact payloads are not validated by the Core save codec. Isolate each one.
                    try
                    {
                        var data = message?.messageData;
                        if (data == null || (message!.messageKey != "foodtrucks:message_bookings_offer" &&
                            message.messageKey != "foodtrucks:message_bookings_booked")) continue;
                        // Enumerate descriptions inside the record boundary too. A native failure must not
                        // prevent independent address/radius repairs on other records.
                        active ??= activeGigs.ToList();
                        accepted ??= (acceptedGigs ?? active).Concat(SavedConfirmations(contact)).ToList();
                        open ??= (openOffers ?? Enumerable.Empty<Dictionary<string, string>>()).ToList();
                        data.TryGetValue("place", out var old);
                        var original = data.TryGetValue("legacyHistoryPlace", out var saved) ? saved : old;
                        if (repairAddresses && (UnreadablePlace(old) ||
                            data.TryGetValue("addressFallback", out var fallbackValue) && fallbackValue == old))
                        {
                            data.TryGetValue("locationId", out var id);
                            var source = id;
                            var place = HistoricalAddress(source, out var fallback, streets);
                            if (place == null) { source = original; place = HistoricalAddress(source, out fallback, streets); }
                            place ??= CityPitches.Text("foodtrucks:event_location_unavailable");
                            if (place != old)
                            {
                                if (!data.ContainsKey("legacyHistoryPlace") && !string.IsNullOrWhiteSpace(old))
                                { data["legacyHistoryPlace"] = old!; changed = true; }
                                data["place"] = place; changed = true;
                            }
                            // Only our own fallback can be upgraded on a later load. Preserve readable saved text.
                            if (fallback && place != CityPitches.Text("foodtrucks:event_location_unavailable"))
                            {
                                if (!data.TryGetValue("addressFallback", out var prior) || prior != place)
                                { data["addressFallback"] = place; changed = true; }
                            }
                            else if (data.Remove("addressFallback")) changed = true;
                        }
                        var booked = message.messageKey == "foodtrucks:message_bookings_booked";
                        if (booked && (!data.TryGetValue("metres", out var metres) || string.IsNullOrWhiteSpace(metres)))
                        { data["metres"] = Catering.PlaceMetres.ToString("F0", CultureInfo.InvariantCulture); changed = true; }
                        var matches = active.Select(d => BookingMatchWithStreets(data, original, d, streets)).ToList();
                        var key = booked
                            ? (matches.Contains(true) ? "foodtrucks:booking_map_hint" :
                                !matches.Contains(null) && HasBookingIdentity(data, original, streets) ? "foodtrucks:booking_inactive" : null)
                            : (HasBookingIdentity(data, original, streets) && day > 0 && data.TryGetValue("reply", out var reply) && int.TryParse(reply, out var replyDay) && replyDay > 0 && day > replyDay &&
                                open.All(d => BookingMatchWithStreets(data, original, d, streets) == false) && accepted.All(d => BookingMatchWithStreets(data, original, d, streets) == false)
                                ? "foodtrucks:offer_inactive" : "foodtrucks:offer_map_hint");
                        // 0.10.7 kept this sentence inline in the template. Unknown identity preserves
                        // existing wording, but an absent argument must inherit that original sentence.
                        if (key == null)
                        {
                            if (data.ContainsKey("guidance")) continue;
                            key = "foodtrucks:booking_map_hint";
                        }
                        var guidance = CityPitches.Text(key);
                        if (!data.TryGetValue("guidance", out var existing) || existing != guidance)
                        { data["guidance"] = guidance; changed = true; }
                    }
                    catch (Exception) { /* A corrupt history record must not stop payments or other records. */ }
                }
            }
            finally { if (changed && FoodTrucksSession.CanWrite) FoodTrucksSession.MarkChanged(); }
        }

        private static IEnumerable<Dictionary<string, string>> SavedConfirmations(Contact contact)
        {
            foreach (var message in contact.messagesQueue)
            {
                Dictionary<string, string>? data = null;
                try { if (message?.messageKey == "foodtrucks:message_bookings_booked") data = message.messageData; }
                catch (Exception) { data = new Dictionary<string, string> { { "identityUnknown", "true" } }; }
                if (data != null) yield return data;
            }
        }

        // Enough payload to identify a booking row; this does not require resolving its street.
        // With no candidates, inactivity is proven even for an unregistered readable street.
        private static bool HasBookingIdentity(Dictionary<string, string> data, string? original, StreetSnapshot streets) =>
            new[] { "day", "start", "kind" }.All(k => data.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) &&
            (data.TryGetValue("bookingId", out var id) && long.TryParse(id, out var number) && number > 0 ||
             data.TryGetValue("locationId", out var location) && HistoricalAddress(location, out _, streets) != null ||
             HistoricalAddress(original, out _, streets) != null || data.TryGetValue("place", out var place) && !UnreadablePlace(place) &&
                Regex.IsMatch(place, @"\A[1-9][0-9]*\s+\S"));

        // true: same identity; false: positively different; null: insufficient resolution/evidence.
        private static bool? BookingMatch(Dictionary<string, string> data, string? original, Dictionary<string, string> candidate)
            => BookingMatchWithStreets(data, original, candidate, CaptureStreets());

        private static bool? BookingMatchWithStreets(Dictionary<string, string> data, string? original,
            Dictionary<string, string> candidate, StreetSnapshot streets)
        {
            if (candidate.ContainsKey("identityUnknown"))
            {
                if (new[] { "day", "bookingId" }.Any(k => data.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) &&
                    candidate.TryGetValue(k, out var other) && !string.IsNullOrWhiteSpace(other) && v != other)) return false;
                return null;
            }
            if (SameBooking(data, original, candidate, streets)) return true;
            if (!new[] { "day", "start", "kind" }.All(k => data.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) &&
                candidate.TryGetValue(k, out var other) && !string.IsNullOrWhiteSpace(other))) return null;
            if (!new[] { "day", "start", "kind" }.All(k => data[k] == candidate[k]) || !SameDetails(data, candidate)) return false;
            if (candidate.TryGetValue("locationId", out var location))
            {
                var address = HistoricalAddress(location, out _, streets);
                var match = ReadableLocationMatch(data, original, address, streets);
                if (match.HasValue) return match;
            }
            return candidate.TryGetValue("place", out var rendered) ? ReadableLocationMatch(data, original, rendered, streets) : null;
        }

        private static bool? ReadableLocationMatch(Dictionary<string, string> data, string? original, string? candidate, StreetSnapshot streets)
        {
            var stored = !UnreadablePlace(original) ? original : data.TryGetValue("place", out var place) ? place : null;
            if (UnreadablePlace(stored) || UnreadablePlace(candidate)) return null;
            if (stored == candidate) return true;
            var left = Regex.Match(stored!, @"\A([1-9][0-9]*)\s+(.+)\z");
            var right = Regex.Match(candidate!, @"\A([1-9][0-9]*)\s+(.+)\z");
            if (!left.Success || !right.Success) return null;
            var aliases = streets.Aliases;
            if (!aliases.TryGetValue(left.Groups[2].Value, out var leftId) || leftId == null ||
                !aliases.TryGetValue(right.Groups[2].Value, out var rightId) || rightId == null) return null;
            if (leftId != rightId || left.Groups[1].Value != right.Groups[1].Value) return false;
            // Aliases prove a mismatch, never a positive match from spelling alone.
            return null;
        }

        // Exact whole names only. Conflicting aliases stay unknown; never erase spaces,
        // hyphens, directions or street types to manufacture an identity.
        internal sealed class StreetSnapshot
        {
            private readonly Dictionary<string, string?> native;
            internal readonly Dictionary<string, string?> Aliases;
            internal readonly string Fingerprint;
            internal bool HasStreets => native.Count > 0;
            internal string? NativeName(string id) => native.TryGetValue(id, out var name) ? name : null;
            internal StreetSnapshot(Dictionary<string, string?> native, Dictionary<string, string?> aliases)
            {
                this.native = native; Aliases = aliases;
                // Length prefixes distinguish missing/invalid names and ambiguous aliases without
                // delimiter collisions. Both input membership/names and resolved ambiguity count.
                string Part(string? value) => value == null ? "-1:" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
                string Table(Dictionary<string, string?> table) => string.Concat(table.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => Part(p.Key) + Part(p.Value)));
                Fingerprint = Part(Table(native)) + Part(Table(aliases));
            }
        }

        // A fresh catalog snapshot per reconciliation poll, shared by its signature and comparisons.
        // No names or aliases survive into the next poll.
        internal static StreetSnapshot CaptureStreets()
        {
            var nativeNames = new Dictionary<string, string?>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string?>(StringComparer.Ordinal);
            void Add(string id, string? name)
            {
                if (InvalidStreet(name)) return;
                if (aliases.TryGetValue(name!, out var prior) && prior != id) aliases[name!] = null;
                else if (!aliases.ContainsKey(name!)) aliases[name!] = id;
            }
            foreach (var id in AddressHelper.StreetDataNames.Distinct().OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!Regex.IsMatch(id, @"\Aba:street_[a-z0-9]+\z")) continue;
                string? native = null;
                try { native = AddressHelper.GetStreetNameLocalized(id); } catch (Exception) { }
                nativeNames[id] = native;
                var fallback = FallbackStreet(id.Substring("ba:street_".Length));
                // Keep fallback alongside native; Add makes names shared by IDs ambiguous.
                foreach (var name in new[] { native, fallback })
                {
                    Add(id, name);
                    if (name == null) continue;
                    var ordinal = Regex.Match(name, @"\A((?:West |East |North |South )?)([1-9][0-9]?)(?:st|nd|rd|th)( Avenue| Street| Road| Drive| Lane)\z");
                    if (!ordinal.Success) continue;
                    var n = int.Parse(ordinal.Groups[2].Value, CultureInfo.InvariantCulture);
                    var words = new[] { "", "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth", "Eleventh", "Twelfth",
                        "Thirteenth", "Fourteenth", "Fifteenth", "Sixteenth", "Seventeenth", "Eighteenth", "Nineteenth" };
                    var tens = new[] { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };
                    var decades = new[] { "", "", "Twentieth", "Thirtieth", "Fortieth", "Fiftieth", "Sixtieth", "Seventieth", "Eightieth", "Ninetieth" };
                    var word = n < 20 ? words[n] : n % 10 == 0 ? decades[n / 10] : tens[n / 10] + "-" + words[n % 10];
                    Add(id, ordinal.Groups[1].Value + word + ordinal.Groups[3].Value);
                    Add(id, ordinal.Groups[1].Value + word.Replace('-', ' ') + ordinal.Groups[3].Value);
                }
            }
            return new StreetSnapshot(nativeNames, aliases);
        }

        private static bool SameBooking(Dictionary<string, string> data, string? original, Dictionary<string, string> candidate, StreetSnapshot streets) =>
            new[] { "day", "start", "kind" }.All(k => data.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) && candidate.TryGetValue(k, out var other) && v == other) &&
            SameDetails(data, candidate) && (SameLocation(data, original, candidate, streets) ||
                !string.IsNullOrWhiteSpace(original) && (candidate.TryGetValue("legacyHistoryPlace", out var legacy) && original == legacy ||
                    candidate.TryGetValue("place", out var originalCandidate) && !UnreadablePlace(originalCandidate) && original == originalCandidate) ||
                data.TryGetValue("place", out var place) && !UnreadablePlace(place) && candidate.TryGetValue("place", out var otherPlace) && place == otherPlace);

        // Backfill saved confirmations independently of address readiness and surviving gigs.
        internal static void RefreshBookingRadius(Contact contact)
        {
            if (!FoodTrucksSession.CanWrite) return;
            var changed = false;
            foreach (var message in contact.messagesQueue)
            {
                var data = message?.messageData;
                if (message?.messageKey != "foodtrucks:message_bookings_booked" || data == null || data.ContainsKey("metres")) continue;
                data["metres"] = Catering.PlaceMetres.ToString("F0", CultureInfo.InvariantCulture);
                changed = true;
            }
            if (changed) FoodTrucksSession.MarkChanged();
        }

        // Repair broken locations and exact legacy city labels. Legacy messages must prove their location from
        // its known old rendering; a sole surviving live offer is not proof of a historical message's identity.
        internal static void RefreshMessages(Contact contact, IEnumerable<Dictionary<string, string>> descriptions)
        {
            if (!FoodTrucksSession.CanWrite) return;
            var live = descriptions.ToList();
            var changed = false;
            foreach (var message in contact.messagesQueue)
            {
                var data = message?.messageData;
                if (data == null || message == null || !RepairableKey(message.messageKey) ||
                    !data.TryGetValue("place", out var old) ||
                    !data.ContainsKey("start")) continue;
                var day = data.TryGetValue("day", out var storedDay) ? storedDay :
                    message.messageKey == "foodtrucks:message_city_event_today" && message.timestamp?.Day > 0
                        ? message.timestamp.Day.ToString(CultureInfo.InvariantCulture) : null;
                if (day == null) continue;
                var kind = data.ContainsKey("event") ? "event" : "kind";
                if (!data.ContainsKey(kind)) continue;
                var matches = live.Where(d => d.TryGetValue("day", out var candidateDay) && candidateDay == day &&
                    d.TryGetValue("start", out var start) && start == data["start"] &&
                    d.TryGetValue(kind, out var value) && value == data[kind] &&
                    SameDetails(data, d) && SameLocation(data, old, d) &&
                    (UnreadablePlace(old) || CityKey(message.messageKey) && d.TryGetValue("legacyPlace", out var legacy) && old == legacy))
                    .Take(2).ToList();
                if (matches.Count != 1 || !matches[0].TryGetValue("place", out var place) || UnreadablePlace(place)) continue;
                if (old == place) continue;
                data["place"] = place;
                changed = true;
            }
            if (changed) FoodTrucksSession.MarkChanged();
        }

        private static bool RepairableKey(string? key) => key == "foodtrucks:message_bookings_offer" ||
            key == "foodtrucks:message_bookings_booked" || CityKey(key);

        private static bool CityKey(string? key) => key == "foodtrucks:message_city_event" ||
            key == "foodtrucks:message_city_event_booked" || key == "foodtrucks:message_city_event_today";

        private static readonly char[] UnreadableCharacters = { ':', '#', '{', '<' };
        private static bool UnreadablePlace(string? place) => string.IsNullOrWhiteSpace(place) || place!.IndexOfAny(UnreadableCharacters) >= 0 ||
            place == CityPitches.Text("foodtrucks:event_location_unavailable");

        private static bool SameDetails(Dictionary<string, string> stored, Dictionary<string, string> candidate) =>
            new[] { "bookingId", "locationId", "guests", "end", "fee", "food" }.All(key =>
                !stored.TryGetValue(key, out var value) || string.IsNullOrEmpty(value) || candidate.TryGetValue(key, out var other) && value == other);

        private static bool SameLocation(Dictionary<string, string> stored, string? old, Dictionary<string, string> candidate, StreetSnapshot? streets = null)
        {
            if (stored.TryGetValue("bookingId", out var booking) && !string.IsNullOrEmpty(booking) || stored.TryGetValue("locationId", out var location) && !string.IsNullOrEmpty(location)) return true; // SameDetails matched it.
            if (candidate.TryGetValue("legacyPlace", out var legacy) && old == legacy) return true;
            if (!candidate.TryGetValue("locationId", out var addressKey) || string.IsNullOrEmpty(addressKey)) return false;
            var hash = addressKey.LastIndexOf('#');
            if (hash < 0) return false;
            var street = addressKey.Substring(0, hash);
            var number = addressKey.Substring(hash + 1);
            // Localizor versions returned either the raw street ID or that ID prefixed by '#'.
            return old == addressKey || old == number + " " + street || old == number + " #" + street ||
                !string.IsNullOrWhiteSpace(old) && old == HistoricalAddress(addressKey, out _, streets);
        }

        internal static void SetDestination(string addressKey)
        {
            if (ReadableAddress(addressKey) == null) return;
            var address = World.BuildingAt(addressKey)!.building.Address;
            // Native Destination guider persists customDestination before setting its target.
            GuidersManager.SetGuiderTarget(address, DirectionGuiderType.Destination);
            Notices.Show(NotificationType.Success, "notification_destination_set",
                new Dictionary<string, string> { { "address", address.ToFormattedString() } }, 4f);
        }
    }
}
