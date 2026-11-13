#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Localizor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FoodTrucks
{
    internal static class TestBridgeHelp
    {
        internal static JToken Run(JObject args)
        {
            return new JObject { ["game"] = GameHelp(), ["rlo"] = RloGuide() };
        }

        private static JArray GameHelp()
        {
            var pages = new JArray();
            var path = Path.Combine(Application.streamingAssetsPath, "helpstructure.json");
            if (!File.Exists(path)) return pages;
            foreach (var category in JArray.Parse(File.ReadAllText(path)))
            {
                var categoryKey = (string?)category["CategoryLocalizorKey"] ?? "";
                var categoryName = LocalizorManager.GetLocalization(categoryKey, null);
                foreach (var page in (JArray?)category["pages"] ?? new JArray())
                {
                    var prefix = (string?)page["pageLocalizorKeyPrefix"];
                    if (prefix == null) continue;
                    var key = "help_" + prefix + "_content";
                    var content = LocalizorManager.GetLocalization(key, null);
                    if (string.IsNullOrEmpty(content) || content == key) continue;
                    pages.Add(new JObject
                    {
                        ["category"] = categoryName,
                        ["slug"] = (string?)page["slug"],
                        ["title"] = LocalizorManager.GetLocalization(prefix, null),
                        ["content"] = content,
                    });
                }
            }
            return pages;
        }

        private static JArray RloGuide()
        {
            var pages = new JArray();
            var guide = AccessTools.TypeByName("RealLifeOverhaul.Meta.Guide");
            var topics = guide?.GetMethod("Topics", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null) as IEnumerable;
            var fill = guide?.GetMethod("Fill", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
            if (topics == null) return pages;
            foreach (var topic in topics)
            {
                var type = topic.GetType();
                string? text = null;
                try
                {
                    var body = (type.GetField("Body")?.GetValue(topic) as Func<string>)?.Invoke();
                    text = body == null || fill == null ? body : fill.Invoke(null, new object[] { body }) as string;
                }
                catch (Exception ex) { text = "(this page couldn't be read: " + (ex.InnerException ?? ex).Message + ")"; }
                pages.Add(new JObject
                {
                    ["id"] = type.GetField("Id")?.GetValue(topic) as string,
                    ["title"] = type.GetField("Title")?.GetValue(topic) as string,
                    ["content"] = text,
                });
            }
            return pages;
        }
    }
}
