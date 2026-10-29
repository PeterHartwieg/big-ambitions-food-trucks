using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace AgentRigInput
{
    // A software Mouse for the Input System, fed from the same per-frame sample as the legacy shim and the UI module.
    // While active, this process's hardware mice are disabled, focus is ignored and cursor warps move only the
    // virtual pointer; everything is put back on deactivation. Not yet seen working in a live run: reported as
    // "experimental" until a rig run checks Mouse.current and the game's action helpers.
    internal sealed class InputSystemMouse
    {
        private Mouse device;
        private readonly List<InputDevice> disabled = new List<InputDevice>();
        private InputSettings.BackgroundBehavior? background;
        private readonly List<Action> restrictions = new List<Action>();
        private Vector2 last;
        private float scrollScale = 120f;
        internal int Events { get; private set; }
        internal int Warps;
        internal string Backend { get; private set; } = "unavailable: not probed";
        internal string LastError { get; private set; }
        internal static InputSystemMouse Instance;

        internal string Probe(Harmony harmony)
        {
            Instance = this;
            try
            {
                var settings = InputSystem.settings;
                if (settings.updateMode != InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
                    return Backend = "unavailable: Input System update mode is " + settings.updateMode;
                // 1.8+ can normalise wheel steps to ±1; otherwise a step is the platform's 120.
                var behaviour = typeof(InputSettings).GetProperty("scrollDeltaBehavior")?.GetValue(settings)?.ToString();
                if (behaviour == "UniformAcrossAllPlatforms") scrollScale = 1f;
                var warp = typeof(Mouse).GetMethod(nameof(Mouse.WarpCursorPosition), new[] { typeof(Vector2) });
                if (warp == null) Log.Info("Patch Mouse.WarpCursorPosition: Not active (method missing).");
                else
                {
                    harmony.Patch(warp, prefix: new HarmonyMethod(typeof(InputSystemMouse).GetMethod(nameof(WarpPrefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
                    Log.Info("Patch Mouse.WarpCursorPosition: Active.");
                }
                return Backend = "experimental";
            }
            catch (Exception ex) { return Backend = "unavailable: " + ex.GetType().Name + ": " + ex.Message; }
        }

        internal bool Ready => Backend == "experimental" || Backend == "active";

        internal void Activate()
        {
            if (!Ready) return;
            try
            {
                device = InputSystem.AddDevice<Mouse>("AgentRigMouse");
                foreach (var d in InputSystem.devices.ToArray())
                    if (d is Mouse && d != device && d.enabled) { InputSystem.DisableDevice(d); disabled.Add(d); }
                var settings = InputSystem.settings;
                background = settings.backgroundBehavior;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                AdmitToRestrictedMaps();
                device.MakeCurrent();
                last = Vector2.zero;
                Backend = "active";
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                Deactivate();
                Backend = "unavailable: " + LastError;
            }
        }

        // Action maps limited to particular devices would otherwise ignore the virtual mouse.
        private void AdmitToRestrictedMaps()
        {
            var maps = new HashSet<InputActionMap>();
            foreach (var action in InputSystem.ListEnabledActions())
                if (action.actionMap != null) maps.Add(action.actionMap);
            var assets = new HashSet<InputActionAsset>(maps.Where(m => m.asset != null).Select(m => m.asset));
            foreach (var map in maps)
            {
                var old = map.devices;
                if (old == null || old.Value.Contains(device)) continue;
                map.devices = new ReadOnlyArray<InputDevice>(old.Value.Concat(new InputDevice[] { device }).ToArray());
                restrictions.Add(() => map.devices = old);
            }
            foreach (var asset in assets)
            {
                var old = asset.devices;
                if (old == null || old.Value.Contains(device)) continue;
                asset.devices = new ReadOnlyArray<InputDevice>(old.Value.Concat(new InputDevice[] { device }).ToArray());
                restrictions.Add(() => asset.devices = old);
            }
        }

        // Before the Input System's dynamic update of this frame, so its consumers see this frame's sample.
        internal void Feed(VirtualMouseState s)
        {
            if (device == null) return;
            var position = new Vector2((float)s.X, (float)s.Y);
            var state = new MouseState
            {
                position = position,
                delta = position - last,
                scroll = new Vector2((float)s.ScrollX * scrollScale, (float)s.ScrollY * scrollScale),
            };
            state = state.WithButton(MouseButton.Left, s.IsHeld(0)).WithButton(MouseButton.Right, s.IsHeld(1)).WithButton(MouseButton.Middle, s.IsHeld(2));
            last = position;
            InputSystem.QueueStateEvent(device, state);
            Events++;
        }

        internal void Deactivate()
        {
            foreach (var undo in restrictions)
            {
                try { undo(); }
                catch (Exception ex) { LastError = "restoring a device restriction: " + ex.Message; }
            }
            restrictions.Clear();
            if (device != null)
            {
                try { InputSystem.RemoveDevice(device); }
                catch (Exception ex) { LastError = "removing the virtual mouse: " + ex.Message; }
                device = null;
            }
            foreach (var d in disabled)
                if (d.added) InputSystem.EnableDevice(d);
            disabled.Clear();
            if (background != null) InputSystem.settings.backgroundBehavior = background.Value;
            background = null;
            if (Backend == "active") Backend = "experimental";
        }

        internal JObject Notes()
        {
            var notes = new JObject { ["events_queued"] = Events, ["warps_intercepted"] = Warps, ["last_error"] = LastError };
            try
            {
                notes["update_mode"] = InputSystem.settings.updateMode.ToString();
                notes["current_mouse"] = Mouse.current == null ? null : Mouse.current.name;
            }
            catch (Exception ex) { notes["read_error"] = ex.Message; }
            return notes;
        }

        // A warp while the rig drives the pointer moves the virtual pointer only, never the desktop cursor.
        private static bool WarpPrefix(Vector2 position)
        {
            if (!LegacyInputShim.Active) return true;
            LegacyInputShim.State?.Warp(position.x, position.y);
            if (Instance != null) Instance.Warps++;
            return false;
        }
    }
}
