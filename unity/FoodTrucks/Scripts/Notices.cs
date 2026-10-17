#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using Localizor;
using UI.Notification;

namespace FoodTrucks
{
    // Every notice and phone text the mod shows goes through here, so each is also a log line with its final text:
    // "Notice: <text>" and "Phone: <contact>: <text>". The mod's notices aren't kept in the game's bell list
    // (trackOnSaveGame: false), and they fade after a few seconds, so the log is where a tester finds them afterwards.
    // The text is resolved as the game resolves it for the screen (NotificationsUI.ShowNotification and
    // ContactsApp.ShowMessage both use LocalizorManager.Localize(key, data)); a key the game can't resolve is logged
    // with its values.
    internal static class Notices
    {
        // The last notice texts, newest last, for the test bridge's state.
        private const int RecentCount = 20;
        private static readonly Queue<string> recent = new Queue<string>();
        private static readonly object gate = new object();

        internal static List<string> Recent()
        {
            lock (gate) return recent.ToList();
        }

        // The notice texts shown while a test bridge command's window is open (the action and a few frames after it).
        // Main thread only.
        private static readonly List<List<string>> collectors = new List<List<string>>();

        internal static List<string> BeginCapture()
        {
            var list = new List<string>();
            collectors.Add(list);
            return list;
        }

        internal static void EndCapture(List<string> list) => collectors.Remove(list);

        // Notifications.Show with the same arguments, logged first.
        internal static void Show(NotificationType type, string key, Dictionary<string, string>? data, float seconds,
            string? duplicateIdentifier = null, bool notificationSound = true, bool trackOnSaveGame = true)
        {
            var text = Text(key, data);
            lock (gate)
            {
                recent.Enqueue(text);
                while (recent.Count > RecentCount) recent.Dequeue();
            }
            foreach (var list in collectors) list.Add(text);
            Log.Event("notice", "Notice: " + text);
            Notifications.Show(type, key, data, seconds, duplicateIdentifier, null, notificationSound, trackOnSaveGame);
        }

        // GameManager.SendTextMessage, logged first.
        internal static void SendText(Contact contact, string key, Dictionary<string, string>? data = null)
        {
            LogPhone(contact, key, data, fromPlayer: false);
            GameManager.SendTextMessage(contact, key, data);
        }

        // Contact.SendMessage (a text during a call), logged first.
        internal static void Send(Contact contact, TextMessage message)
        {
            LogPhone(contact, message.messageKey, message.messageData, fromPlayer: false);
            contact.SendMessage(message);
        }

        // Contact.ReceivePlayerMessage (the player's own line in a call), logged first.
        internal static void SendFromPlayer(Contact contact, TextMessage message)
        {
            LogPhone(contact, message.messageKey, message.messageData, fromPlayer: true);
            contact.ReceivePlayerMessage(message);
        }

        private static void LogPhone(Contact contact, string key, Dictionary<string, string>? data, bool fromPlayer)
        {
            try
            {
                var name = contact?.id == null ? "(no contact)"
                    : LocalizorManager.IsLocalizedKey(contact.id) ? LocalizorManager.GetLocalization(contact.id, null) : contact.id;
                Log.Event("phone", $"Phone: {name}: {(fromPlayer ? "[player] " : "")}{Text(key, data)}");
            }
            catch (Exception ex) { Log.Error("Phone log: " + ex.Message); }
        }

        // A count with its noun, "1 space" or "3 spaces": the key's "_one" text for one, else the key's, with {n}.
        internal static string Counted(long n, string key) =>
            Text(n == 1 ? key + "_one" : key, new Dictionary<string, string> { { "n", n.ToString() } });

        // The text as the game shows it, or the key and its values when the game has no text for the key.
        internal static string Text(string key, Dictionary<string, string>? data)
        {
            try
            {
                if (LocalizorManager.IsLocalizedKey(key)) return LocalizorManager.Localize(key, data).ToString();
            }
            catch (Exception ex) { return $"{key} {Values(data)} (not resolved: {ex.Message})"; }
            return $"{key} {Values(data)}";
        }

        private static string Values(Dictionary<string, string>? data) =>
            data == null || data.Count == 0 ? "{}" : "{" + string.Join(", ", data.Select(p => $"{p.Key}={p.Value}")) + "}";
    }
}
