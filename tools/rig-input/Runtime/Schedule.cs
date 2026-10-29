using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentRigInput
{
    // Read-only knowledge of the BizMan Schedule: a descriptor for its resize handles, and observers that record when
    // the game's own resize lifecycle starts and ends. Nothing here calls or replaces game schedule code.
    internal static class Schedule
    {
        private const string Ns = "UI.Smartphone.Apps.BizMan.Schedule.";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        internal static Type Handle, Helper;
        private static FieldInfo isLeft, workShift, slider, range;
        private static PropertyInfo movableArea;
        private static FieldInfo hourWidth;
        private static MemberInfo currentDay;
        internal static long LastStartFrame = -1, LastEndFrame = -1;
        internal static int LastEndInstance, CoroutineSteps, FrameSteps;
        internal static string Problem;

        internal static void Bind(Harmony harmony, MethodBase resizeCoroutine)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BigAmbitions");
            Handle = asm?.GetType(Ns + "WorkShiftSliderHandle");
            Helper = asm?.GetType(Ns + "WorkShiftHelper");
            if (Handle == null) { Problem = "WorkShiftSliderHandle not found"; Log.Info("Patch WorkShiftSliderHandle.OnSlideEnd: Not active (type missing)."); return; }
            isLeft = Handle.GetField("_isLeft", Fields);
            workShift = Handle.GetField("_workShift", Fields);
            slider = Handle.GetField("_sliderRectTransform", Fields);
            range = Handle.GetField("_range", Fields);
            movableArea = Helper?.GetProperty("MovableArea", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            hourWidth = Helper?.GetField("HourWidth", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var scheduleHelper = Util.Types(asm).FirstOrDefault(t => t.Name == "ScheduleHelper");
            currentDay = (MemberInfo)scheduleHelper?.GetProperty("CurrentScheduleDay", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? scheduleHelper?.GetField("CurrentScheduleDay", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Observe(harmony, "OnSlideStart", nameof(SlideStarted));
            Observe(harmony, "OnSlideEnd", nameof(SlideEnded));
            if (resizeCoroutine != null)
            {
                harmony.Patch(resizeCoroutine, prefix: new HarmonyMethod(typeof(Schedule).GetMethod(nameof(CoroutineStep), BindingFlags.Static | BindingFlags.NonPublic)));
                Log.Info($"Patch {resizeCoroutine.DeclaringType?.Name}.MoveNext (step counter): Active.");
            }
            else Log.Info("Patch WorkShiftSliderHandle DraggingCoroutine.MoveNext (step counter): Not active (coroutine not found).");
        }

        private static void Observe(Harmony harmony, string method, string postfix)
        {
            var target = Handle.GetMethod(method, Fields, null, Type.EmptyTypes, null);
            if (target == null) { Problem = method + " not found"; Log.Info($"Patch WorkShiftSliderHandle.{method}: Not active (method missing)."); return; }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Schedule).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic)));
            Log.Info($"Patch WorkShiftSliderHandle.{method} (release observer): Active.");
        }

        private static void SlideStarted() => LastStartFrame = Time.frameCount;

        // Runs after the native release returned: records only, never changes the result.
        private static void SlideEnded(MonoBehaviour __instance)
        {
            LastEndFrame = Time.frameCount;
            LastEndInstance = __instance != null ? __instance.GetInstanceID() : 0;
        }

        private static void CoroutineStep() { CoroutineSteps++; FrameSteps++; }

        internal static bool Observes(GameObject hit) =>
            Handle != null && Problem == null && hit != null && hit.GetComponentInParent(Handle) != null;

        internal static JObject TakeFrame()
        {
            var o = new JObject
            {
                ["resize_steps"] = FrameSteps,
                ["slide_start"] = LastStartFrame == Time.frameCount,
                ["slide_end"] = LastEndFrame == Time.frameCount,
            };
            FrameSteps = 0;
            return o;
        }

        // Which bar, side, employee and day a handle edits, and the track's scale in screen pixels per hour.
        internal static JObject Describe(Component handle, Func<RectTransform, JObject> project, Func<RectTransform, Vector2, Vector2> point)
        {
            var o = new JObject();
            try
            {
                o["handle"] = isLeft?.GetValue(handle) is bool left ? (left ? "left" : "right") : null;
                var shift = workShift?.GetValue(handle);
                if (shift != null)
                {
                    var t = shift.GetType();
                    o["start"] = JToken.FromObject(t.GetField("startingHour")?.GetValue(shift));
                    o["end"] = JToken.FromObject(t.GetField("endingHour")?.GetValue(shift));
                    o["employee_id"] = t.GetField("employeeId")?.GetValue(shift) as string;
                    o["workstation"] = t.GetField("itemInstanceId")?.GetValue(shift) as string;
                    o["shift_type"] = t.GetField("type")?.GetValue(shift)?.ToString();
                }
                o["day"] = Day();
                if (slider?.GetValue(handle) is RectTransform bar) o["bar"] = project(bar);
                if (range?.GetValue(handle) is Vector2 r) o["range_canvas"] = new JObject { ["min"] = r.x, ["max"] = r.y };
                if (movableArea?.GetValue(null) is RectTransform area && area != null)
                {
                    o["track"] = project(area);
                    if (hourWidth?.GetValue(null) is float width && width > 0)
                    {
                        var rect = area.rect;
                        var a = point(area, new Vector2(rect.xMin, rect.center.y));
                        var b = point(area, new Vector2(rect.xMin + width, rect.center.y));
                        o["hour_width_canvas"] = width;
                        o["pixels_per_hour"] = new JObject { ["x"] = Math.Round(b.x - a.x, 3), ["y"] = Math.Round(b.y - a.y, 3) };
                    }
                }
            }
            catch (Exception ex) { o["error"] = ex.GetType().Name + ": " + ex.Message; }
            return o;
        }

        private static string Day()
        {
            object value = currentDay is PropertyInfo p ? p.GetValue(null) : (currentDay as FieldInfo)?.GetValue(null);
            if (value == null) return null;
            var t = value.GetType();
            var day = (object)t.GetField("day")?.GetValue(value) ?? t.GetProperty("day")?.GetValue(value);
            return day?.ToString();
        }
    }
}
