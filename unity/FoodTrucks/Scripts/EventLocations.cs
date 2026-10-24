#nullable enable
using System;
using System.Linq;
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
            var address = World.BuildingAt(addressKey)?.building?.Address;
            if (address == null || address.streetNumber <= 0) return null;
            var street = address.GetStreetNameLocalized();
            if (string.IsNullOrWhiteSpace(street) || street.Contains(":") || street.Contains("#") ||
                street.Contains("{") || street.Contains("}") || street.Contains("<")) return null;
            return address.ToFormattedString();
        }

        internal static string Name(string addressKey) => ReadableAddress(addressKey)
            ?? CityPitches.Text("foodtrucks:event_location_unavailable");

        // Old phone messages store their rendered address. Repair only an unambiguous live booking/offer;
        // day, start and event kind must all match, so two simultaneous locations are never guessed.
        internal static void RefreshMessages(Contact contact, IEnumerable<Dictionary<string, string>> descriptions)
        {
            var live = descriptions.ToList();
            var changed = false;
            foreach (var message in contact.messagesQueue)
            {
                var data = message?.messageData;
                if (data == null || message == null ||
                    !(message.messageKey?.StartsWith("foodtrucks:message_", StringComparison.Ordinal) ?? false) ||
                    !data.ContainsKey("day") || !data.ContainsKey("start")) continue;
                var kind = data.ContainsKey("event") ? "event" : "kind";
                if (!data.ContainsKey(kind)) continue;
                var matches = live.Where(d => d["day"] == data["day"] && d["start"] == data["start"] &&
                    d.TryGetValue(kind, out var value) && value == data[kind]).Select(d => d["place"]).Distinct().Take(2).ToList();
                if (matches.Count != 1 || data.TryGetValue("place", out var old) && old == matches[0]) continue;
                data["place"] = matches[0];
                changed = true;
            }
            if (changed) FoodTrucksSession.MarkChanged();
        }

        internal static void SetDestination(string addressKey)
        {
            if (ReadableAddress(addressKey) == null) return;
            var address = World.BuildingAt(addressKey)!.building.Address;
            GuidersManager.SetGuiderTarget(address, DirectionGuiderType.Destination);
            Notices.Show(NotificationType.Success, "notification_destination_set",
                new Dictionary<string, string> { { "address", address.ToFormattedString() } }, 4f);
        }
    }
}
