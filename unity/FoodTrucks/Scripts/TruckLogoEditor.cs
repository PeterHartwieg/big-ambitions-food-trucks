#nullable enable
using System;
using System.Linq;
using Enums;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using Localizor;
using UI;
using UI.Smartphone.Apps.BizMan;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // A private copy of the shop editor. Only its three business-dependent methods are replaced;
    // the native shape importer, colour pickers and font choices keep their normal behavior.
    internal static class TruckLogoEditor
    {
        private static LogoCustomizer? editor;
        private static SaveState? session;
        private static string? vehicleId, title;
        private static string? savedDesign;
        private static LogoSettings? initial;
        internal static bool Ready { get; private set; }

        internal static void CheckReady()
        {
            Ready = new[] { "Refresh", "UpdateSaveButton", "SaveLogo" }.All(name =>
            {
                var method = AccessTools.Method(typeof(LogoCustomizer), name);
                return method != null && Harmony.GetPatchInfo(method)?.Prefixes.Any(p => p.PatchMethod.DeclaringType == typeof(TruckLogoEditor)) == true;
            });
        }

        internal static LogoCustomizer? Template => InstanceBehavior<UIs>.Instance?.fullMenu?.bizMan?.business?
            .GetComponentsInChildren<LogoCustomizer>(true).FirstOrDefault();
        internal static T Field<T>(object target, string name) => Traverse.Create(target).Field(name).GetValue<T>();
        private static void Set(object target, string field, object value) => Traverse.Create(target).Field(field).SetValue(value);
        internal static string Text(string key) => LocalizorManager.GetLocalization("foodtrucks:" + key, null);
        private static bool Ours(LogoCustomizer instance) => editor != null && instance == editor;
        private static bool Writable => editor != null && editor.gameObject.activeInHierarchy && session != null &&
            ReferenceEquals(session, FoodTrucksSession.CurrentState) && FoodTrucksSession.CanWrite && vehicleId != null &&
            BizManVehiclePage.Instance(vehicleId)?.vehicleTypeName == FoodTrucksMod.TruckTypeName &&
            VehicleLogo.CanEdit(StreetSales.Find(session, vehicleId)?.Logo);

        internal static void Open(string id)
        {
            var source = Template;
            var state = FoodTrucksSession.CurrentState;
            if (!Ready || source == null || state == null || !FoodTrucksSession.CanWrite ||
                BizManVehiclePage.Instance(id)?.vehicleTypeName != FoodTrucksMod.TruckTypeName) return;
            var raw = StreetSales.Find(state, id)?.Logo;
            if (!VehicleLogo.CanEdit(raw)) return;
            var logo = VehicleLogo.Read(raw);
            var names = StreetFoodBooks.VehicleNames();
            var name = logo?.Name ?? (names.TryGetValue(id, out var n) ? n : "Food truck");
            var columns = BizManFleet.ShowVehicleColumns(Text("logo_header"), wide: true);
            if (columns == null) return;
            var parent = columns.Value.First;
            var bar = new GameObject("FoodTrucks.LogoActions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            bar.transform.SetParent(parent, false);
            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 24; layout.childForceExpandWidth = false; layout.childControlHeight = true;
            bar.GetComponent<LayoutElement>().preferredHeight = 52;
            BizManParts.LooseButton(bar.transform, Text("logo_back"), () => BizManVehiclePage.OpenFor(id));
            BizManParts.LooseButton(bar.transform, Text("logo_name"), AskName);
            var remove = BizManParts.LooseButton(bar.transform, Text("logo_remove"), () =>
            {
                if (!Writable) return;
                StreetSales.GetOrAdd(session!, id).Logo = null;
                BizManVehiclePage.OpenFor(id);
            });
            if (remove != null) remove.interactable = logo != null;
            foreach (Transform child in bar.transform)
            {
                var size = child.gameObject.AddComponent<LayoutElement>();
                size.preferredWidth = 260; size.preferredHeight = 52;
            }
            // Keep the native editor inactive until its identity and draft have been registered.
            var holder = new GameObject("FoodTrucks.LogoEditor", typeof(RectTransform), typeof(LayoutElement));
            holder.SetActive(false); holder.transform.SetParent(parent, false);
            holder.GetComponent<LayoutElement>().preferredHeight = 650;
            var copy = Object.Instantiate(source.gameObject, holder.transform);
            editor = copy.GetComponent<LogoCustomizer>();
            session = state; vehicleId = id; title = name;
            initial = logo == null ? LogoHelper.GenerateLogoSetting("ba:businesstype_fastfoodrestaurant") : Settings(logo);
            savedDesign = logo?.Save().ToString();
            var rect = (RectTransform)copy.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            copy.SetActive(true); holder.SetActive(true);
            Log.Info($"Truck logo editor: opened for {id}.");
        }

        private static void AskName()
        {
            if (!Writable) return;
            var owner = editor;
            InstanceBehavior<UIs>.Instance.textInputUi.Show("foodtrucks:logo_name", "foodtrucks:logo_apply_name", value =>
            {
                if (editor != owner || !Writable) return;
                value = value.Trim();
                if (!VehicleLogo.ValidName(value)) { CartSelling.Notify("foodtrucks:logo_invalid_name"); return; }
                title = value;
                BizManParts.SetText(Field<Component>(editor!, "businessName").transform, value);
                UpdateSavePrefix(editor!);
            }, initialText: title ?? "", allowEmpty: false, characterLimit: 80);
        }

        internal static bool RefreshPrefix(LogoCustomizer __instance)
        {
            if (!Ours(__instance)) return true;
            try
            {
                var settings = initial!;
                Set(__instance, "_logoShape", settings.logoShape);
                Set(__instance, "_logoFont", settings.font);
                Set(__instance, "_backgroundColor", (Color)settings.backgroundColor);
                Set(__instance, "_logoColor", (Color)settings.logoColor);
                Set(__instance, "_fontColor", (Color)settings.fontColor);
                var shapes = Field<LogoShapes>(__instance, "logoShapes");
                shapes.SetSelectedShape(settings.logoShape); shapes.RefreshLogoShapes();
                Invoke(__instance, "SetUpColorLists", Type.EmptyTypes);
                BizManParts.SetText(Field<Component>(__instance, "businessName").transform, title!);
                var fonts = Field<Array>(InstanceBehavior<GlobalReferences>.Instance, "signFontAssets");
                var index = fonts.Cast<Object>().ToList().FindIndex(f => f.name == settings.font.ToString());
                Field<UI.Elements.Dropdown>(__instance, "fontDropdown").ResetSelectedOption(Math.Max(0, index));
                Invoke(__instance, "SelectFont", new[] { typeof(int) }, Math.Max(0, index));
                Invoke(__instance, "SelectBackgroundColor", new[] { typeof(Color) }, (Color)settings.backgroundColor);
                Invoke(__instance, "SelectLogoColor", new[] { typeof(Color) }, (Color)settings.logoColor);
                Invoke(__instance, "SelectFontColor", new[] { typeof(Color) }, (Color)settings.fontColor);
                UpdateSavePrefix(__instance);
            }
            catch (Exception ex) { Log.Error("Truck logo editor: " + ex); }
            return false;
        }

        private static void Invoke(object target, string method, Type[] args, params object[] values) =>
            AccessTools.Method(target.GetType(), method, args).Invoke(target, values);

        internal static bool UpdateSavePrefix(LogoCustomizer __instance)
        {
            if (!Ours(__instance)) return true;
            var button = Field<Button>(__instance, "saveLogoChangesButton");
            try { button.interactable = Writable && Draft().Save().ToString() != savedDesign; }
            catch { button.interactable = false; }
            return false;
        }

        internal static bool SavePrefix(LogoCustomizer __instance)
        {
            if (!Ours(__instance)) return true;
            if (!Writable) return false;
            try
            {
                var design = Draft();
                var previous = StreetSales.GetOrAdd(session!, vehicleId!).Logo;
                // Preserve unknown additive fields when editing the supported version.
                var token = previous is Newtonsoft.Json.Linq.JObject old ? (Newtonsoft.Json.Linq.JObject)old.DeepClone() : new Newtonsoft.Json.Linq.JObject();
                token.Merge(design.Save());
                StreetSales.GetOrAdd(session!, vehicleId!).Logo = token;
                savedDesign = design.Save().ToString(); initial = Settings(design);
                UpdateSavePrefix(__instance);
                Log.Info($"Truck logo: saved design for {vehicleId}.");
            }
            catch (Exception ex) { Log.Error("Truck logo save: " + ex); }
            return false;
        }

        private static VehicleLogo Draft() => new VehicleLogo(title!, Field<string>(editor!, "_logoShape"),
            Field<FontFace>(editor!, "_logoFont").ToString(),
            ColorUtility.ToHtmlStringRGBA(Field<Color>(editor!, "_backgroundColor")),
            ColorUtility.ToHtmlStringRGBA(Field<Color>(editor!, "_logoColor")),
            ColorUtility.ToHtmlStringRGBA(Field<Color>(editor!, "_fontColor")));

        internal static Color Colour(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
        internal static LogoSettings Settings(VehicleLogo logo) => new LogoSettings
        {
            logoShape = logo.Shape, font = Enum.TryParse<FontFace>(logo.Font, out var font) && Enum.IsDefined(typeof(FontFace), font) ? font : FontFace.Rubik,
            backgroundColor = Colour(logo.Background), logoColor = Colour(logo.Ink), fontColor = Colour(logo.Text)
        };
    }
}
