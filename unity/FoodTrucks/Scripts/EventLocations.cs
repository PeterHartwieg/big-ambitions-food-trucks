#nullable enable
using System;
using System.Linq;
using System.Globalization;
using Entities;
using System.Collections.Generic;
using UI.Notification;
using Streets;
using UI.Guiders;

namespace FoodTrucks
{
    // Street IDs are data IDs, not translation keys. Use the same building and formatter as CityMapFilters.
    internal static class EventLocations
    {
        internal static bool Ready => AddressHelper.StreetDataNames.Any() && World.AllBuildings().Count > 0;

        internal static string? ReadableAddress(string addressKey)
        {
            return ReadableAddress(World.BuildingAt(addressKey));
        }

        internal static string? ReadableAddress(CityBuildingController? controller)
        {
            var address = controller?.building?.Address;
            if (address == null || address.streetNumber <= 0) return null;
            var street = address.GetStreetNameLocalized();
            if (string.IsNullOrWhiteSpace(street) || street.Contains(":") || street.Contains("#") ||
                street.Contains("{") || street.Contains("}") || street.Contains("<")) return null;
            return address.ToFormattedString();
        }

        internal static string Name(string addressKey) => ReadableAddress(addressKey)
            ?? CityPitches.Text("foodtrucks:event_location_unavailable");

        // Backfill saved confirmations independently of address readiness and surviving gigs.
        internal static void RefreshBookingRadius(Contact contact)
        {
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
        private static bool UnreadablePlace(string place) => place.IndexOfAny(UnreadableCharacters) >= 0 ||
            place == CityPitches.Text("foodtrucks:event_location_unavailable");

        private static bool SameDetails(Dictionary<string, string> stored, Dictionary<string, string> candidate) =>
            new[] { "bookingId", "locationId", "guests", "end", "fee", "food" }.All(key =>
                !stored.TryGetValue(key, out var value) || candidate.TryGetValue(key, out var other) && value == other);

        private static bool SameLocation(Dictionary<string, string> stored, string old, Dictionary<string, string> candidate)
        {
            if (stored.ContainsKey("bookingId") || stored.ContainsKey("locationId")) return true; // SameDetails matched it.
            if (candidate.TryGetValue("legacyPlace", out var legacy) && old == legacy) return true;
            if (!candidate.TryGetValue("locationId", out var addressKey)) return false;
            var hash = addressKey.LastIndexOf('#');
            if (hash < 0) return false;
            var street = addressKey.Substring(0, hash);
            var number = addressKey.Substring(hash + 1);
            // Localizor versions returned either the raw street ID or that ID prefixed by '#'.
            return old == addressKey || old == number + " " + street || old == number + " #" + street;
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
