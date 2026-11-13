#nullable enable
using System;
using HarmonyLib;
using Helpers;
using PlayerActivity;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The Food Safety course the vendor licence needs. The game has the course's name
    // (DiplomaName.FoodSafetyCourse) but no course data and no door for it [code, 1.0], so the mod adds the
    // data at load and offers the course at the business school's Market Demands door. The game's own study
    // panel runs it, so price, time skip and completion are the game's, and the result goes into the save's
    // PlayerDiplomas, where the licence check reads it. Nothing is paid before the panel's Start button.
    //
    // The course name's locale key: the game asks for DiplomaName.GetLocalizeKey(), "diplomaname_" +
    // ToStringFast(), so "diplomaname_FoodSafetyCourse" (Helpers/LocalizationHelper.cs). en.json has it in
    // lower case, which matches because Localizor.LocalizorManager (HGPlugins.dll, not in the decompile
    // folder; read with ilspycmd from build/game-dlls) lowercases both sides [code, 1.0]:
    // ParseAndMergeTable stores every key as key.ToLower().Trim(), for the game's tables and for mods'
    // Locales files (isExternal) alike, and GetLocalization(this string label) looks up label.ToLower().
    // The game's own mixed-case keys such as "diplomaname_MarketDemands" rely on the same thing.
    internal static class FoodSafetyCourse
    {
        private const DiplomaName Course = DiplomaName.FoodSafetyCourse;
        private const DiplomaName HostDoor = DiplomaName.MarketDemands;
        private const string HeaderKey = "foodtrucks:course_choice_header";
        private const string BodyKey = "foodtrucks:course_choice_body";

        private static DiplomaData? data;
        private static bool passThrough;
        private static bool warnedNoConfirm;
        private static EducationDoorController? askingDoor;
        private static int cancelledFrame = -1;
        private static AccessTools.FieldRef<HudConfirmUi, Button>? cancelButtonField;

        public static bool Register()
        {
            var existing = EducationHelper.GetDiplomaData(Course);
            if (existing != null) return true;
            var rules = DemandData.Rules?.Fleet;
            if (rules == null) return false;
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<DiplomaData>();
                data.name = "FoodTrucks.FoodSafetyCourse";
                data.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            data.diplomaName = Course;
            data.pricePerHour = rules.CoursePricePerHour;
            data.requiredDiploma = DiplomaName.Undefined;
            data.requiredMinutes = rules.CourseMinutes;
            EducationHelper.AllDiplomas.Add(data);
            Log.Info($"Food Safety course: added at the Market Demands door, {data.requiredMinutes} minutes at ${data.pricePerHour} an hour.");
            return true;
        }

        public static void Unregister()
        {
            if (data == null) return;
            EducationHelper.AllDiplomas.Remove(data);
            Object.Destroy(data);
            data = null;
        }

        internal static bool StartLearningPrefix(EducationDoorController __instance)
        {
            try
            {
                if (passThrough || __instance == null || __instance.StudyDiploma == null) return true;
                if (__instance.StudyDiploma.diplomaName != HostDoor) return true;
                if (!Register() || EducationHelper.HasCompletedDiploma(Course)) return true;
                if (!CanStudy() || HudConfirm.isOpen || PlayerActivityUI.IsPanelOpen) return true;
                if (HudConfirm.onShow == null)
                {
                    if (!warnedNoConfirm) Log.Error("Food Safety course: the game's question box isn't there; the door teaches only its own course.");
                    warnedNoConfirm = true;
                    return true;
                }
                var door = __instance;
                if (!ListenToCancelButtons())
                {
                    Log.Error("Food Safety course: the question box's cancel button wasn't found; the door teaches only its own course.");
                    return true;
                }
                askingDoor = door;
                cancelledFrame = -1;
                HudConfirm.Show(HeaderKey, BodyKey,
                    () => { Forget(); StudyFoodSafety(door); },
                    () => cancelledFrame = Time.frameCount,
                    Course.GetLocalizeKey(), HostDoor.GetLocalizeKey(),
                    allowConfirmationSkip: false);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("Food Safety course: " + ex);
                return true;
            }
        }

        private static bool ListenToCancelButtons()
        {
            cancelButtonField ??= AccessTools.FieldRefAccess<HudConfirmUi, Button>("cancelButton");
            var found = false;
            foreach (var box in Object.FindObjectsOfType<HudConfirmUi>(true))
            {
                var button = cancelButtonField(box);
                if (button == null) continue;
                button.onClick.RemoveListener(OnCancelButton);
                button.onClick.AddListener(OnCancelButton);
                found = true;
            }
            return found;
        }

        private static void OnCancelButton()
        {
            var door = askingDoor;
            var pressed = door != null && cancelledFrame == Time.frameCount;
            Forget();
            if (pressed) StudyOwnCourse(door!);
        }

        private static void Forget()
        {
            askingDoor = null;
            cancelledFrame = -1;
            if (cancelButtonField == null) return;
            foreach (var box in Object.FindObjectsOfType<HudConfirmUi>(true))
                cancelButtonField(box)?.onClick.RemoveListener(OnCancelButton);
        }

        private static bool CanStudy()
        {
            var building = InstanceBehavior<BuildingManager>.Instance;
            if (building == null || !building.isOpen) return false;
            if (PlayerHelper.ItemInstanceInHands != null && !PlayerHelper.IsHoldingBag) return false;
            return string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId);
        }

        private static void StudyOwnCourse(EducationDoorController door)
        {
            try
            {
                if (door == null) return;
                passThrough = true;
                door.StartLearning();
            }
            catch (Exception ex) { Log.Error("Food Safety course: " + ex); }
            finally { passThrough = false; }
        }

        private static void StudyFoodSafety(EducationDoorController door)
        {
            try
            {
                if (door == null) return;
                if (!CanStudy())
                {
                    StudyOwnCourse(door);
                    return;
                }
                var own = door.StudyDiploma;
                if (own?.chair == null || !Register())
                {
                    Log.Error("Food Safety course: the Market Demands door has no chair or no course data.");
                    return;
                }
                var study = new StudyDiploma
                {
                    diplomaName = Course,
                    chair = own.chair,
                    exitPosition = own.exitPosition,
                    computer = own.chair.GetComponent<ItemController>()?.parentItemController,
                };
                PlayerActivityUI.Show(study, door);
            }
            catch (Exception ex) { Log.Error("Food Safety course: " + ex); }
        }
    }
}
