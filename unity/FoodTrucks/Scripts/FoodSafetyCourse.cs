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
        // Set while the door's own course runs through the patched method, so the prefix lets it pass.
        private static bool passThrough;
        private static bool warnedNoConfirm;
        // The door whose question is up, and the frame in which the question last closed by its cancel path.
        private static EducationDoorController? askingDoor;
        private static int cancelledFrame = -1;
        private static AccessTools.FieldRef<HudConfirmUi, Button>? cancelButtonField;

        // Adds the course data unless the list has it. The game refills the list when its diplomas load, so
        // this runs again at every save load and before the door asks. Returns false without rules data.
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

        // At mod unload: take the course out again. The save keeps the player's progress in PlayerDiplomas.
        public static void Unregister()
        {
            if (data == null) return;
            EducationHelper.AllDiplomas.Remove(data);
            Object.Destroy(data);
            data = null;
        }

        // Prefix on EducationDoorController.StartLearning. Returning false skips the original; it runs
        // later from the player's answer.
        internal static bool StartLearningPrefix(EducationDoorController __instance)
        {
            try
            {
                if (passThrough || __instance == null || __instance.StudyDiploma == null) return true;
                if (__instance.StudyDiploma.diplomaName != HostDoor) return true;
                if (!Register() || EducationHelper.HasCompletedDiploma(Course)) return true;
                // The game's refusals (school closed, hands full, in a vehicle) and an open panel or question:
                // the original shows its own message or does nothing.
                if (!CanStudy() || HudConfirm.isOpen || PlayerActivityUI.IsPanelOpen) return true;
                if (HudConfirm.onShow == null)
                {
                    if (!warnedNoConfirm) Log.Error("Food Safety course: the game's question box isn't there; the door teaches only its own course.");
                    warnedNoConfirm = true;
                    return true;
                }
                var door = __instance;
                // The game closes a question through its cancel action not only from the cancel button but also
                // on Escape, when the phone opens and a few other times (HudConfirm.onClose). So the cancel
                // action only notes the frame, and the door's own course starts from a listener on the cancel
                // button itself. Enter picks the confirm button, as everywhere in the game.
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

        // Adds OnCancelButton to the cancel button of every question box (the game has one for the HUD and one
        // for the phone menu). The button's own ClickCancel is a listener set in the prefab, and Unity runs
        // those before listeners added in code, so OnCancelButton runs right after the cancel action.
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

        // A real press of the "Market Demands" button: the cancel action ran in this same frame.
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

        // The same checks as the start of EducationDoorController.StartLearning [code, 1.0].
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

        // Opens the game's study panel for the course at the door's own chair and computer. A new
        // StudyDiploma, so the door's serialized one stays as it is.
        private static void StudyFoodSafety(EducationDoorController door)
        {
            try
            {
                if (door == null) return;
                if (!CanStudy())
                {
                    // Something changed while the question was open; the original shows the game's message.
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
