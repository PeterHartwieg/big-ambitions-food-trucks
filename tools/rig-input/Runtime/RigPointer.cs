using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AgentRigInput
{
    // The UI module's input source while the rig drives the pointer: the virtual state, never the OS.
    public sealed class RigBaseInput : BaseInput
    {
        internal VirtualMouseState State;
        public override Vector2 mousePosition => new Vector2((float)State.X, (float)State.Y);
        public override Vector2 mouseScrollDelta => new Vector2((float)State.ScrollX, (float)State.ScrollY);
        public override bool mousePresent => true;
        public override bool GetMouseButtonDown(int button) => State.IsDown(button);
        public override bool GetMouseButtonUp(int button) => State.IsUp(button);
        public override bool GetMouseButton(int button) => State.IsHeld(button);
        public override bool touchSupported => false;
        public override int touchCount => 0;
        public override float GetAxisRaw(string axisName) => 0f;
        public override bool GetButtonDown(string buttonName) => false;
        public override string compositionString => "";
    }

    // Unity's own mouse processing (raycasts, enter/exit, press, drag threshold, begin/drag/end, drop, click) without
    // the focus gate that stops StandaloneInputModule in an unfocused or background player.
    public sealed class RigPointerInputModule : StandaloneInputModule
    {
        public override bool IsModuleSupported() => true;
        public override bool ShouldActivateModule() => isActiveAndEnabled;
        public override void UpdateModule() { }

        public override void Process()
        {
            SendUpdateEventToSelectedObject();
            ProcessMouseEvent(0);
        }

        internal PointerEventData Pointer(int id)
        {
            GetPointerData(id, out var data, false);
            return data;
        }

        // Cleanup that cannot wait for a release frame: the captured objects get pointer up and end drag now.
        internal void ForceRelease()
        {
            foreach (var id in new[] { kMouseLeftId, kMouseRightId, kMouseMiddleId })
            {
                var d = Pointer(id);
                if (d == null) continue;
                if (d.pointerPress != null) ExecuteEvents.Execute(d.pointerPress, d, ExecuteEvents.pointerUpHandler);
                if (d.pointerDrag != null && d.dragging) ExecuteEvents.Execute(d.pointerDrag, d, ExecuteEvents.endDragHandler);
                d.eligibleForClick = false;
                d.pointerPress = null;
                d.rawPointerPress = null;
                d.pointerDrag = null;
                d.dragging = false;
            }
        }

        internal void ClearAll() => ClearSelection();
    }

    // Swaps the EventSystem's input modules for the rig module while an interaction is active, and puts them back.
    internal sealed class RigPointer
    {
        private EventSystem system;
        private readonly List<(BaseInputModule Module, bool Enabled)> originals = new List<(BaseInputModule, bool)>();
        private RigPointerInputModule module;
        private RigBaseInput input;
        internal GameObject TrackedPress { get; private set; }
        internal string OriginalModules { get; private set; }

        internal static string Backend => EventSystem.current != null ? "ready" : "unavailable: no EventSystem in the scene";

        internal static string CurrentModule()
        {
            var es = EventSystem.current;
            if (es == null) return null;
            var m = es.currentInputModule;
            return m == null ? null : m.GetType().FullName;
        }

        internal void Activate(VirtualMouseState state)
        {
            system = EventSystem.current;
            if (system == null) throw new MouseError("rig_mouse_unavailable", "no EventSystem in the scene");
            originals.Clear();
            var names = new List<string>();
            foreach (var m in system.GetComponents<BaseInputModule>())
            {
                originals.Add((m, m.enabled));
                names.Add(m.GetType().FullName);
                m.enabled = false;
            }
            OriginalModules = string.Join(", ", names);
            input = system.gameObject.AddComponent<RigBaseInput>();
            input.State = state;
            module = system.gameObject.AddComponent<RigPointerInputModule>();
            module.inputOverride = input;
            TrackedPress = null;
        }

        internal void ForceRelease()
        {
            if (module != null) module.ForceRelease();
        }

        internal void Deactivate()
        {
            if (module != null)
            {
                module.ClearAll();
                module.enabled = false;
                Object.Destroy(module);
            }
            if (input != null) Object.Destroy(input);
            module = null; input = null;
            foreach (var (m, enabled) in originals)
                if (m != null) m.enabled = enabled;
            originals.Clear();
            system = null;
            TrackedPress = null;
        }

        // The press target's destruction while held is a cancellation, not a gesture result.
        internal bool PressTargetLost(bool held)
        {
            var d = module == null ? null : module.Pointer(PointerInputModule.kMouseLeftId);
            if (d?.pointerPress != null) TrackedPress = d.pointerPress;
            else if (!held) TrackedPress = null;
            return !ReferenceEquals(TrackedPress, null) && TrackedPress == null;
        }

        internal JObject Observe()
        {
            if (module == null) return new JObject { ["module"] = false };
            var d = module.Pointer(PointerInputModule.kMouseLeftId);
            return new JObject
            {
                ["over"] = Name(d?.pointerCurrentRaycast.gameObject),
                ["press"] = Name(d?.pointerPress),
                ["drag"] = Name(d?.pointerDrag),
                ["dragging"] = d != null && d.dragging,
                ["over_ui"] = system != null && system.IsPointerOverGameObject(PointerInputModule.kMouseLeftId),
            };
        }

        private static JToken Name(GameObject go) => go == null ? JValue.CreateNull() : (JToken)$"{go.name}#{go.GetInstanceID()}";
    }
}
