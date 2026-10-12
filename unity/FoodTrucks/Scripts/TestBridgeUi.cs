#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Localizor;
using Newtonsoft.Json.Linq;
using PlayerActivity;
using UI;
using UI.Dialog;
using UI.InteriorDesigner;
using UI.Notification;
using UI.Smartphone;
using UnityEngine;
using UnityEngine.UI;
using Dropdown = UI.Elements.Dropdown;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The bridge's `ui`: what a person would click. `list` walks the open canvases for visible, interactable buttons (the
    // game's buttons are all UnityEngine.UI.Button; its own button classes wrap one) and the options of each visible
    // dropdown (the game's own UI.Elements.Dropdown), and reads the visible text of open dialogs and the phone. `click`
    // invokes a button's onClick, or selects a dropdown option as the dropdown's own option row does. `escape` runs the
    // game's Esc handling (CancelButtonHandler.HandleEscapeClick) for whatever is open.
    internal static class TestBridgeUi
    {
        // TextMeshPro isn't among the mod's compile references; its text is read by reflection.
        private static readonly Type? TmpText = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
        private static readonly PropertyInfo? TmpTextProperty = TmpText?.GetProperty("text");
        private static readonly PropertyInfo? TmpAlphaProperty = TmpText?.GetProperty("alpha");
        private static readonly FieldInfo? OptionsField = AccessTools.Field(typeof(Dropdown), "_options");
        private static readonly FieldInfo? LocalizeField = AccessTools.Field(typeof(Dropdown), "_localizeOptions");

        private sealed class Entry
        {
            public int Id;
            public string Text = "";
            public string Path = "";
            public string Kind = "button";
            public Button? Button;
            public Dropdown? Dropdown;
            public int Option;
        }

        // The entries of the last list, for click by id.
        private static List<Entry> last = new List<Entry>();

        internal static JToken Run(JObject args)
        {
            var op = TestBridgeCommands.Need(args, "op", "list, click or escape");
            switch (op)
            {
                case "list":
                    return List();
                case "click":
                    RequireFtCity(op);
                    return Click(args);
                case "escape":
                    RequireFtCity(op);
                    return Escape();
                default:
                    throw new BridgeError($"Unknown ui op '{op}': list, click or escape.");
            }
        }

        internal const string NotFtCityText = "ui works only in a loaded FT city; use load from the main menu.";
        internal const string SaveLoadText = "ui click refuses the game's save and load controls; use the save and load commands.";

        private static void RequireFtCity(string op)
        {
            if (TestBridgeCommands.Scene() != "city" || !TestBridgeCommands.IsFt(SaveGameManager.Current?.SaveGameName)) throw new BridgeError(NotFtCityText);
            TestBridgeCommands.RequireFtCity("ui " + op);
        }

        internal static void Forget() => last = new List<Entry>();

        // The game's own save, load, delete and character controls, and the confirmations the pause menu opens, are
        // left to the save and load commands: a click there could overwrite or delete a save that isn't FT.
        private static readonly HashSet<string> SaveLoadOwners = new HashSet<string>(StringComparer.Ordinal)
        {
            "LoadGame", "LoadGameCharacterEntryView", "LoadGameSaveEntryView", "SaveGameCompatibilityUI", "FuneralUI",
        };

        private static bool SaveOrLoadControl(Entry entry)
        {
            var target = (Component?)entry.Button ?? entry.Dropdown;
            if (target == null) return false;
            for (var t = target.transform; t != null; t = t.parent)
                foreach (var c in t.GetComponents<MonoBehaviour>())
                    if (c != null && SaveLoadOwners.Contains(c.GetType().Name)) return true;
            var inPauseMenu = target.GetComponentInParent<UI.MiniMenu.MiniMenu>() != null;
            // A confirmation while the pause menu is open is one of its own (overwrite a save, leave unsaved).
            if (HudConfirm.isOpen && UI.MiniMenu.MiniMenu.IsOpen && target.GetComponentInParent<HudConfirmUi>() != null) return true;
            if (entry.Button != null)
            {
                var click = entry.Button.onClick;
                for (var i = 0; i < click.GetPersistentEventCount(); i++)
                {
                    var owner = click.GetPersistentTarget(i)?.GetType().Name ?? "";
                    var method = click.GetPersistentMethodName(i) ?? "";
                    if (SaveLoadOwners.Contains(owner)) return true;
                    if (owner == "MiniMenu" && (method.IndexOf("Save", StringComparison.OrdinalIgnoreCase) >= 0 || method.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }
            if (inPauseMenu)
            {
                var words = (target.name + " " + entry.Text).ToLowerInvariant();
                if (words.Contains("save") || words.Contains("load")) return true;
            }
            return false;
        }

        // The control's label now, to check a click by id against the list it came from.
        private static string CurrentText(Entry entry)
        {
            if (entry.Button != null) return Label(entry.Button.transform);
            if (entry.Dropdown != null && OptionsField?.GetValue(entry.Dropdown) is List<string> options && entry.Option < options.Count)
            {
                var localize = LocalizeField?.GetValue(entry.Dropdown) is bool b && b;
                return Clean(localize ? LocalizorManager.GetLocalization(options[entry.Option], null) : options[entry.Option]);
            }
            return "";
        }

        private static JToken List()
        {
            last = Entries();
            return new JObject
            {
                ["buttons"] = new JArray(last.Select(e => new JObject { ["id"] = e.Id, ["text"] = e.Text, ["kind"] = e.Kind, ["path"] = e.Path })),
                ["texts"] = new JArray(Texts()),
            };
        }

        private static List<Entry> Entries()
        {
            var found = new List<(Vector2 At, Entry Entry)>();
            foreach (var button in Object.FindObjectsOfType<Button>())
            {
                if (button == null || !button.IsActive() || !button.IsInteractable() || !Visible(button.transform, out var at)) continue;
                found.Add((at, new Entry { Button = button, Text = Label(button.transform), Path = PathOf(button.transform) }));
            }
            foreach (var dropdown in Object.FindObjectsOfType<Dropdown>())
            {
                if (dropdown == null || !dropdown.isActiveAndEnabled || !Visible(dropdown.transform, out var at)) continue;
                if (!(OptionsField?.GetValue(dropdown) is List<string> options)) continue;
                var localize = LocalizeField?.GetValue(dropdown) is bool b && b;
                for (var i = 0; i < options.Count; i++)
                {
                    var text = localize ? LocalizorManager.GetLocalization(options[i], null) : options[i];
                    found.Add((at - new Vector2(0f, i + 1), new Entry { Dropdown = dropdown, Option = i, Kind = "option", Text = Clean(text), Path = PathOf(dropdown.transform) + $"/option[{i}]" }));
                }
            }
            // Top to bottom, then left to right, as a person reads the screen.
            var list = found.OrderByDescending(f => Mathf.Round(f.At.y)).ThenBy(f => f.At.x).Select(f => f.Entry).ToList();
            for (var i = 0; i < list.Count; i++) list[i].Id = i + 1;
            return list;
        }

        private static JToken Click(JObject args)
        {
            Entry entry;
            var id = TestBridgeCommands.Num(args, "id");
            var text = TestBridgeCommands.Str(args, "text");
            if (id != null)
            {
                entry = last.FirstOrDefault(e => e.Id == (int)id.Value) ?? throw new BridgeError($"No entry {id} in the last ui list; list again.");
                var target = (Component?)entry.Button ?? entry.Dropdown;
                if (target == null || !Visible(target.transform, out _)) throw new BridgeError($"Entry {id} ({entry.Text}) is no longer on screen; list again.");
                var now = CurrentText(entry);
                if (now != entry.Text) throw new BridgeError($"the control with id {entry.Id} now reads '{now}'; list again.");
            }
            else if (text != null)
            {
                var entries = Entries();
                last = entries;
                var wanted = Clean(text);
                var matches = entries.Where(e => string.Equals(e.Text, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    var near = entries.Where(e => e.Text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0).Take(8).Select(e => $"{e.Id} \"{e.Text}\"").ToList();
                    throw new BridgeError($"No button or option reads \"{text}\"{(near.Count == 0 ? "" : "; close ones: " + string.Join(", ", near))}.");
                }
                if (matches.Count > 1)
                    throw new BridgeError($"{matches.Count} entries read \"{text}\": {string.Join(", ", matches.Select(e => $"{e.Id} ({e.Path})"))}; click by id.");
                entry = matches[0];
            }
            else throw new BridgeError("ui click needs \"id\" (from the last list) or \"text\".");
            if (SaveOrLoadControl(entry)) throw new BridgeError(SaveLoadText);

            if (entry.Button != null)
            {
                if (!entry.Button.IsInteractable()) throw new BridgeError($"\"{entry.Text}\" isn't clickable now.");
                entry.Button.onClick.Invoke();
            }
            else entry.Dropdown!.SelectOption(entry.Option);
            Log.Info($"Test bridge: clicked \"{entry.Text}\" ({entry.Path}).");
            return new JObject { ["clicked"] = entry.Text };
        }

        // What is open, in the order the game's Esc handling closes things (CancelButtonHandler).
        internal static List<string> Open()
        {
            var open = new List<string>();
            if (HudConfirm.isOpen) open.Add("confirmation");
            if (AcknowledgeWarning.isOpen) open.Add("warning");
            if (UI.MiniMenu.MiniMenu.IsOpen) open.Add("pause menu");
            if (FullMenu.IsOpen) open.Add("phone");
            if (CityMap.IsOpen) open.Add("map");
            var dialog = InstanceBehavior<UIs>.Instance?.playerHUD?.dialogUI;
            if (dialog != null && dialog.isPanelOpen) open.Add("call");
            if (PlayerActivityUI.IsPanelOpen) open.Add("activity panel");
            return open;
        }

        private static JToken Escape()
        {
            // The game's list picker (the commissary's choices) isn't in its Esc handling; its Cancel closes it.
            if (DropdownSelector.isOpen)
            {
                var picker = Object.FindObjectOfType<DropdownSelectorUI>();
                if (picker == null) throw new BridgeError("The list picker is open but its window wasn't found.");
                picker.OnCancel();
                Log.Info("Test bridge: escape closed the list picker.");
                return new JObject { ["closed"] = "list picker" };
            }
            var before = Open();
            if (before.Count == 0) throw new BridgeError("Nothing is open to close (Esc would open the pause menu).");
            CancelButtonHandler.HandleEscapeClick();
            var after = Open();
            var closed = before.Where(o => !after.Contains(o)).ToList();
            var text = closed.Count > 0 ? string.Join(", ", closed) : $"Esc handled; still open: {string.Join(", ", after)}";
            Log.Info($"Test bridge: escape: {text}.");
            return new JObject { ["closed"] = text };
        }

        // ---- Text ----

        // The visible text blocks of open dialogs, the phone and the notices on screen; at the main menu, of every canvas.
        private static List<string> Texts()
        {
            var roots = new List<Transform>();
            if (TestBridgeCommands.Scene() != "city")
            {
                roots.AddRange(Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.isActiveAndEnabled).Select(c => c.transform));
            }
            else
            {
                void Add(Component? c)
                {
                    if (c != null && c.gameObject.activeInHierarchy) roots.Add(c.transform);
                }
                var uis = InstanceBehavior<UIs>.Instance;
                if (FullMenu.IsOpen) Add(uis?.fullMenu);
                if (uis?.playerHUD?.dialogUI != null && uis.playerHUD.dialogUI.isPanelOpen) Add(uis.playerHUD.dialogUI);
                if (HudConfirm.isOpen) Add(Object.FindObjectOfType<HudConfirmUi>());
                if (AcknowledgeWarning.isOpen) Add(Object.FindObjectOfType<AcknowledgeWarningUi>());
                if (DropdownSelector.isOpen) Add(Object.FindObjectOfType<DropdownSelectorUI>());
                if (UI.MiniMenu.MiniMenu.IsOpen) Add(uis?.miniMenuUI);
                if (PlayerActivityUI.IsPanelOpen) Add(Object.FindObjectOfType<PlayerActivityUI>());
                foreach (var notices in Object.FindObjectsOfType<NotificationsUI>()) Add(notices.container);
            }
            var texts = new List<string>();
            if (TmpText == null || TmpTextProperty == null) return texts;
            foreach (var root in roots.Distinct())
            {
                foreach (var component in root.GetComponentsInChildren(TmpText, false))
                {
                    if (!(component is Behaviour behaviour) || !behaviour.isActiveAndEnabled) continue;
                    if (TmpAlphaProperty?.GetValue(component) is float alpha && alpha < 0.05f) continue;
                    if (!Visible(component.transform, out _)) continue;
                    var text = Clean(TmpTextProperty.GetValue(component) as string);
                    if (text.Length > 0 && (texts.Count == 0 || texts[texts.Count - 1] != text)) texts.Add(text);
                }
            }
            return texts;
        }

        // ---- Helpers ----

        // On an enabled canvas, not faded out by a CanvasGroup, with its middle on the screen; `at` is that middle.
        private static bool Visible(Transform t, out Vector2 at)
        {
            at = default;
            if (!t.gameObject.activeInHierarchy) return false;
            var canvas = t.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.isActiveAndEnabled) return false;
            foreach (var group in t.GetComponentsInParent<CanvasGroup>())
            {
                if (group.alpha < 0.05f) return false;
                if (group.ignoreParentGroups) break;
            }
            if (!(t is RectTransform rect)) return false;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var root = canvas.rootCanvas;
            var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            if (Mathf.Abs(b.x - a.x) < 1f || Mathf.Abs(b.y - a.y) < 1f) return false;
            at = (a + b) / 2f;
            return at.x >= 0 && at.y >= 0 && at.x <= Screen.width && at.y <= Screen.height;
        }

        private static string Label(Transform t)
        {
            if (TmpText != null && TmpTextProperty != null)
            {
                foreach (var component in t.GetComponentsInChildren(TmpText, false))
                {
                    if (component is Behaviour b && !b.isActiveAndEnabled) continue;
                    var text = Clean(TmpTextProperty.GetValue(component) as string);
                    if (text.Length > 0) return text;
                }
            }
            return t.name;
        }

        // Rich-text tags out, whitespace to single spaces.
        private static string Clean(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
            return System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
