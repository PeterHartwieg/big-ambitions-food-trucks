#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using Localizor;
using UI.Notification;

namespace FoodTrucks
{
    internal static class Notices
    {
        private const int RecentCount = 20;
        private static readonly Queue<string> recent = new Queue<string>();
        private static readonly object gate = new object();

        internal static List<string> Recent()
        {
            lock (gate) return recent.ToList();
        }

        private static readonly List<List<string>> collectors = new List<List<string>>();

        internal static List<string> BeginCapture()
        {
            var list = new List<string>();
            collectors.Add(list);
            return list;
        }

        internal static void EndCapture(List<string> list) => collectors.Remove(list);

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
            showing = true;
            try { Notifications.Show(type, key, data, seconds, duplicateIdentifier, null, notificationSound, trackOnSaveGame); }
            finally { showing = false; }
        }

        [ThreadStatic] private static bool showing;
        internal static bool Showing => showing;

        internal static void SendText(Contact contact, string key, Dictionary<string, string>? data = null)
        {
            LogPhone(contact, key, data, fromPlayer: false);
            GameManager.SendTextMessage(contact, key, data);
        }

        internal static void Send(Contact contact, TextMessage message)
        {
            LogPhone(contact, message.messageKey, message.messageData, fromPlayer: false);
            contact.SendMessage(message);
        }

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

        internal static string Counted(long n, string key) =>
            Text(n == 1 ? key + "_one" : key, new Dictionary<string, string> { { "n", n.ToString() } });

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
