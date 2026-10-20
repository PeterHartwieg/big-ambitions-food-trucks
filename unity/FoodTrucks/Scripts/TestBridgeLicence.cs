#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using Helpers;
using Newtonsoft.Json.Linq;

namespace FoodTrucks
{
    // Real Life Overhaul's driving licences, for agent playtests: its driven lesson and test can't be played through the
    // bridge, so a licence is sold outright for what the school's whole path costs in person (car: theory, lesson and
    // driven test; commercial: van lesson and driven test, a valid car licence first). A lesson or test under way is
    // stopped with its fee returned. Read and written by reflection [RLO 0.40.x: Licences/LicenceState.cs LicenceLaw,
    // LicenceState.Grant and Standing; Licences/Licences.cs Pay; Licences/LicenceDirector.cs _practical;
    // Licences/Driving/PracticalTest.cs Abort]. RLO saves the licence with the game's next save.
    internal static class TestBridgeLicence
    {
        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static JToken Run(JObject args)
        {
            var state = Type("LicenceState");
            var law = Type("LicenceLaw");
            var classes = Type("LicenceClass");
            var name = (args["class"]?.ToString() ?? "").Trim().ToLowerInvariant();
            if (name != "car" && name != "commercial")
                throw new BridgeError("licence needs \"class\": car or commercial.");
            var kind = Enum.Parse(classes, name == "car" ? "Car" : "Commercial");
            var car = Enum.Parse(classes, "Car");
            int day = TimeHelper.CurrentDay;
            int price = name == "car"
                ? Fee(law, "TheoryFee") + Fee(law, "LessonFee") + Fee(law, "CarTestFee")
                : Fee(law, "LessonFee") + Fee(law, "CommercialTestFee");
            var standing = Standing(state, kind, day);
            var reply = new JObject { ["class"] = name, ["standing"] = standing, ["price"] = price };
            if (args["buy"]?.ToObject<bool>() != true) return reply;

            if (standing == "Valid") throw new BridgeError($"Your {name} licence is already valid.");
            if (Static(state, "SuspendedUntil") is int until && until > day)
                throw new BridgeError($"Your licences are suspended until day {until}.");
            if (name == "commercial" && Standing(state, car, day) != "Valid")
                throw new BridgeError("A commercial licence needs a valid car licence first.");
            var practical = Practical();
            var money = (SaveGameManager.Current?.Money ?? 0f) + Refund(practical);
            if (money < price) throw new BridgeError($"The {name} licence costs ${price}; you have ${money:F2}.");

            StopPractical(practical);
            var pay = AccessTools.Method(Type("Licences"), "Pay", new[] { typeof(int), typeof(string) })
                      ?? throw new BridgeError("RLO's Licences.Pay isn't there.");
            if (!(bool)pay.Invoke(null, new object[] { price, "reallife_transaction_licence_fee" }))
                throw new BridgeError($"The ${price} fee could not be paid.");
            var grant = AccessTools.Method(state, "Grant", new[] { classes, typeof(int) })
                        ?? throw new BridgeError("RLO's LicenceState.Grant isn't there.");
            grant.Invoke(null, new[] { kind, day });
            SaveGameManager.MarkChange();
            Log.Info($"Test bridge: {name} licence bought for ${price} on day {day}.");
            reply["standing"] = Standing(state, kind, day);
            reply["bought"] = true;
            reply["money"] = Math.Round(SaveGameManager.Current?.Money ?? 0f, 2);
            return reply;
        }

        private static Type Type(string name) =>
            AccessTools.TypeByName("RealLifeOverhaul." + name) ?? throw new BridgeError($"RLO's {name} isn't there (is Real Life Overhaul loaded?).");

        private static int Fee(Type law, string name) =>
            (int)(AccessTools.Property(law, name)?.GetValue(null) ?? throw new BridgeError($"RLO's LicenceLaw.{name} isn't there."));

        private static object? Static(Type type, string name) => AccessTools.Field(type, name)?.GetValue(null);

        private static string Standing(Type state, object kind, int day) =>
            AccessTools.Method(state, "Standing")?.Invoke(null, new[] { kind, day })?.ToString()
            ?? throw new BridgeError("RLO's LicenceState.Standing isn't there.");

        // The lesson or test under way, if any.
        private static object? Practical()
        {
            var director = AccessTools.Property(Type("LicenceDirector"), "Instance")?.GetValue(null);
            var practical = director == null ? null : AccessTools.Field(director.GetType(), "_practical")?.GetValue(director);
            return practical != null && AccessTools.Property(practical.GetType(), "Active")?.GetValue(practical) is true ? practical : null;
        }

        // What calling it off gives back: its fee, unless it was free (from the settings' test button).
        private static int Refund(object? practical)
        {
            if (practical == null || AccessTools.Field(practical.GetType(), "_free")?.GetValue(practical) is true) return 0;
            return AccessTools.Field(practical.GetType(), "_feePaid")?.GetValue(practical) is int fee ? fee : 0;
        }

        // A lesson or test under way is given up, its fee returned, as the school does when one is called off.
        private static void StopPractical(object? practical)
        {
            if (practical != null)
                AccessTools.Method(practical.GetType(), "Abort", new[] { typeof(string), typeof(bool) })
                    ?.Invoke(practical, new object[] { "reallife_lic_test_stop", true });
        }
    }
}
