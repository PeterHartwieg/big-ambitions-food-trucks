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
        // Text fields (a bank loan's amount, BankLoanSettings.amountInput): TMP_InputField, by reflection too.
        private static readonly Type? TmpInput = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
        private static readonly PropertyInfo? TmpInputText = TmpInput?.GetProperty("text");
        private static readonly PropertyInfo? TmpInputInteractable = TmpInput?.GetProperty("interactable");
        // Typing runs every character through the field's own check (its content type and character limit), as a key press does.
        private static readonly MethodInfo? TmpInputValidate = TmpInput == null ? null
            : AccessTools.Method(TmpInput, "Validate", new[] { typeof(string), typeof(int), typeof(char) });
        private static readonly PropertyInfo? TmpInputLimit = TmpInput?.GetProperty("characterLimit");
        private static readonly PropertyInfo? TmpInputOnValidate = TmpInput?.GetProperty("onValidateInput");
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
            // A slider (the activity panel's hours, a campaign's days) or a toggle (part time, full time): set and
            // clicked as a drag or a click on them does.
            public Slider? Slider;
            public Toggle? Toggle;
            // A text field: typed into with ui type.
            public Component? Input;
        }

        // The entries of the last list, for click by id.
        private static List<Entry> last = new List<Entry>();

        internal static JToken Run(JObject args)
        {
            var op = TestBridgeCommands.Need(args, "op", "list, click, set, escape or tree");
            switch (op)
            {
                case "list":
                    return List();
                case "tree":
                    return Tree(args);
                case "click":
                    RequireFtCity(op);
                    return Click(args);
                case "set":
                    RequireFtCity(op);
                    return Set(args);
                case "type":
                    RequireFtCity(op);
                    return TypeText(args);
                case "escape":
                    RequireFtCity(op);
                    return Escape();
                default:
                    throw new BridgeError($"Unknown ui op '{op}': list, click, set, type, escape or tree.");
            }
        }

        // `tree`: reads only. The objects whose hierarchy path ends with `path` (inactive ones too), each with its children
        // down to `depth`: name, active, size, components, and the colour of each Graphic and the alpha of a CanvasGroup.
        // For comparing a copied game part with the game's own (15 October 2026: our WhiteBox copies showed no background).
        private static JToken Tree(JObject args)
        {
            var path = TestBridgeCommands.Need(args, "path", "the end of an object's hierarchy path, e.g. \"Content/WhiteBox (Template)(Clone)\"");
            var depth = Math.Max(0, Math.Min(8, args.Value<int?>("depth") ?? 3));
            var limit = Math.Max(1, Math.Min(10, args.Value<int?>("max") ?? 3));
            var found = new JArray();
            foreach (var t in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (found.Count >= limit) break;
                if (t == null || t.gameObject.scene.name == null) continue;
                var full = FullPath(t);
                if (!full.EndsWith(path, StringComparison.Ordinal)) continue;
                found.Add(new JObject { ["path"] = full, ["node"] = Node(t, depth) });
            }
            return new JObject { ["found"] = found };
        }

        private static string FullPath(Transform t)
        {
            var parts = new List<string>();
            for (var n = t; n != null; n = n.parent) parts.Add(n.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static JObject Node(Transform t, int depth)
        {
            var rect = t as RectTransform;
            var node = new JObject
            {
                ["name"] = t.name,
                ["active"] = t.gameObject.activeSelf,
                ["size"] = rect == null ? null : $"{rect.rect.width:F0}x{rect.rect.height:F0}",
                ["components"] = new JArray(t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name)),
            };
            foreach (var g in t.GetComponents<Graphic>())
                node["graphic_" + g.GetType().Name] = $"enabled {g.enabled}, colour {ColorUtility.ToHtmlStringRGBA(g.color)}" +
                                                       (g is Image image ? $", sprite {(image.sprite == null ? "none" : image.sprite.name)}, material {(image.material == null ? "none" : image.material.name)}" : "");
            var group = t.GetComponent<CanvasGroup>();
            if (group != null) node["canvas_group_alpha"] = group.alpha;
            if (depth > 0 && t.childCount > 0)
                node["children"] = new JArray(t.Cast<Transform>().Select(c => Node(c, depth - 1)));
            return node;
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
            var target = Target(entry);
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
            if (entry.Slider != null) return ControlLabel(entry.Slider.transform);
            if (entry.Toggle != null) return ControlLabel(entry.Toggle.transform);
            if (entry.Input != null) return ControlLabel(entry.Input.transform);
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
                ["buttons"] = new JArray(last.Select(Json)),
                ["texts"] = new JArray(Texts()),
            };
        }

        private static JObject Json(Entry e)
        {
            var json = new JObject { ["id"] = e.Id, ["text"] = e.Text, ["kind"] = e.Kind, ["path"] = e.Path };
            if (e.Slider != null)
            {
                json["value"] = Math.Round(e.Slider.value, 2);
                json["min"] = Math.Round(e.Slider.minValue, 2);
                json["max"] = Math.Round(e.Slider.maxValue, 2);
                json["whole_numbers"] = e.Slider.wholeNumbers;
            }
            if (e.Toggle != null) json["on"] = e.Toggle.isOn;
            if (e.Input != null) json["value"] = TmpInputText?.GetValue(e.Input) as string ?? "";
            return json;
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
            foreach (var slider in Object.FindObjectsOfType<Slider>())
            {
                if (slider == null || !slider.IsActive() || !slider.IsInteractable() || !Visible(slider.transform, out var at)) continue;
                found.Add((at, new Entry { Slider = slider, Kind = "slider", Text = ControlLabel(slider.transform), Path = PathOf(slider.transform) }));
            }
            foreach (var toggle in Object.FindObjectsOfType<Toggle>())
            {
                if (toggle == null || !toggle.IsActive() || !toggle.IsInteractable() || !Visible(toggle.transform, out var at)) continue;
                found.Add((at, new Entry { Toggle = toggle, Kind = "toggle", Text = ControlLabel(toggle.transform), Path = PathOf(toggle.transform) }));
            }
            if (TmpInput != null)
                foreach (var input in Object.FindObjectsOfType(TmpInput).OfType<Component>())
                {
                    if (input == null || !(input is Behaviour b) || !b.isActiveAndEnabled || !(TmpInputInteractable?.GetValue(input) is bool on && on) ||
                        !Visible(input.transform, out var at)) continue;
                    found.Add((at, new Entry { Input = input, Kind = "text field", Text = ControlLabel(input.transform), Path = PathOf(input.transform) }));
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
                var target = Target(entry);
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

            if (entry.Slider != null) throw new BridgeError($"\"{entry.Text}\" is a slider: ui set with its id and a value.");
            if (entry.Input != null) throw new BridgeError($"\"{entry.Text}\" is a text field: ui type with its id and the text.");
            if (entry.Button != null)
            {
                if (!entry.Button.IsInteractable()) throw new BridgeError($"\"{entry.Text}\" isn't clickable now.");
                entry.Button.onClick.Invoke();
            }
            else if (entry.Toggle != null)
            {
                if (!entry.Toggle.IsInteractable()) throw new BridgeError($"\"{entry.Text}\" isn't clickable now.");
                // What a click on a toggle does (Toggle.InternalToggle): flip it; one in a group that allows none off stays on.
                if (entry.Toggle.isOn && entry.Toggle.group != null && !entry.Toggle.group.allowSwitchOff) { }
                else entry.Toggle.isOn = !entry.Toggle.isOn;
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
            // Furniture being placed (a right click on it, or place's start): Esc cancels it.
            if (BigAmbitions.PlacementSystem.PlacementSystem.IsInPlacementMode) open.Add("placing furniture");
            return open;
        }

        private static Component? Target(Entry entry) => (Component?)entry.Button ?? (Component?)entry.Dropdown ?? (Component?)entry.Slider ?? (Component?)entry.Toggle ?? entry.Input;

        // { "id", "value" }: a slider from the last list moved to the value, as dragging it does (its onValueChanged runs:
        // the activity panel's hours, a campaign's days). The value is held to the slider's range and, for a slider of
        // whole numbers, rounded.
        private static JToken Set(JObject args)
        {
            var id = TestBridgeCommands.Num(args, "id") ?? throw new BridgeError("ui set needs \"id\" (a slider from the last list) and \"value\".");
            var value = TestBridgeCommands.Num(args, "value") ?? throw new BridgeError("ui set needs \"value\": a number.");
            var entry = last.FirstOrDefault(e => e.Id == (int)id) ?? throw new BridgeError($"No entry {id} in the last ui list; list again.");
            var slider = entry.Slider ?? throw new BridgeError($"Entry {id} ({entry.Text}) isn't a slider.");
            if (!slider.IsActive() || !slider.IsInteractable() || !Visible(slider.transform, out _))
                throw new BridgeError($"Entry {id} ({entry.Text}) is no longer on screen; list again.");
            if (SaveOrLoadControl(entry)) throw new BridgeError(SaveLoadText);
            var wanted = Mathf.Clamp((float)value, slider.minValue, slider.maxValue);
            if (slider.wholeNumbers) wanted = Mathf.Round(wanted);
            slider.value = wanted;
            Log.Info($"Test bridge: set \"{entry.Text}\" to {slider.value} ({entry.Path}).");
            return new JObject { ["set"] = entry.Text, ["value"] = Math.Round(slider.value, 2), ["min"] = slider.minValue, ["max"] = slider.maxValue, ["texts"] = new JArray(Texts()) };
        }

        // { "id", "text" }: a text field from the last list given that text, as typing it does (its onValueChanged runs:
        // the bank's loan amount updates the daily payment). Nothing is confirmed: the screen's own buttons do that.
        private static JToken TypeText(JObject args)
        {
            var id = TestBridgeCommands.Num(args, "id") ?? throw new BridgeError("ui type needs \"id\" (a text field from the last list) and \"text\".");
            var text = TestBridgeCommands.Str(args, "text") ?? throw new BridgeError("ui type needs \"text\".");
            var entry = last.FirstOrDefault(e => e.Id == (int)id) ?? throw new BridgeError($"No entry {id} in the last ui list; list again.");
            var input = entry.Input ?? throw new BridgeError($"Entry {id} ({entry.Text}) isn't a text field.");
            if (!(input is Behaviour b) || !b.isActiveAndEnabled || !Visible(input.transform, out _))
                throw new BridgeError($"Entry {id} ({entry.Text}) is no longer on screen; list again.");
            if (SaveOrLoadControl(entry)) throw new BridgeError(SaveLoadText);
            TmpInputText!.SetValue(input, Typed(input, text));
            var now = TmpInputText.GetValue(input) as string ?? "";
            Log.Info($"Test bridge: typed \"{now}\" into \"{entry.Text}\" ({entry.Path}).");
            return new JObject { ["typed"] = entry.Text, ["value"] = now, ["texts"] = new JArray(Texts()) };
        }

        // What the field keeps of the text when it is typed key by key: each character through the field's own
        // onValidateInput (if set) or its Validate (the content type: digits only for the bank's loan amount), up to its
        // character limit. "$20,000" in a number field becomes "20000", which BankDialog.LoanRequest can parse.
        private static string Typed(Component input, string text)
        {
            var kept = "";
            var limit = TmpInputLimit?.GetValue(input) is int l ? l : 0;
            var custom = TmpInputOnValidate?.GetValue(input) as Delegate;
            foreach (var ch in text)
            {
                if (limit > 0 && kept.Length >= limit) break;
                var result = custom != null ? custom.DynamicInvoke(kept, kept.Length, ch)
                    : TmpInputValidate != null ? TmpInputValidate.Invoke(input, new object[] { kept, kept.Length, ch }) : ch;
                if (result is char c && c != '\0') kept += c;
            }
            return kept;
        }

        // A slider's or toggle's own text, else the nearest text of its parents (the label beside it).
        private static string ControlLabel(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                foreach (var (_, text) in TextBlocks(p)) return text;
            return t.name;
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
                // The overlays over a clicked object or building, the shop's purchase panel and the map's building card.
                Add(uis?.overlayUI);
                if (uis?.playerHUD?.purchaseUI != null && uis.playerHUD.purchaseUI.isActiveAndEnabled) Add(uis.playerHUD.purchaseUI);
                if (CityMap.IsOpen) Add(uis?.buildingResume);
                foreach (var notices in Object.FindObjectsOfType<NotificationsUI>()) Add(notices.container);
            }
            var texts = new List<string>();
            var seen = new HashSet<Component>();
            foreach (var root in roots.Distinct())
            {
                foreach (var (component, text) in TextBlocks(root))
                {
                    // Roots can overlap (for example a dialog under the open phone). Read each label once.
                    if (!seen.Add(component) || !Visible(component.transform, out _)) continue;
                    if (texts.Count == 0 || texts[texts.Count - 1] != text) texts.Add(text);
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

        // Both native TMP labels and the mod's Unity UI Text labels, in hierarchy order.
        // Traverse once so a mixed hierarchy neither duplicates labels nor groups them by text implementation.
        private static IEnumerable<(Component Component, string Text)> TextBlocks(Transform root)
        {
            foreach (var component in root.GetComponentsInChildren<Behaviour>(false))
            {
                if (!component.isActiveAndEnabled) continue;
                string? value;
                if (component is Text legacy)
                {
                    if (legacy.color.a < 0.05f) continue;
                    value = legacy.text;
                }
                else if (TmpText != null && TmpTextProperty != null && TmpText.IsInstanceOfType(component))
                {
                    if (TmpAlphaProperty?.GetValue(component) is float alpha && alpha < 0.05f) continue;
                    value = TmpTextProperty.GetValue(component) as string;
                }
                else continue;
                var text = Clean(value);
                if (text.Length > 0) yield return (component, text);
            }
        }

        private static string Label(Transform t)
        {
            foreach (var (_, text) in TextBlocks(t)) return text;
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
