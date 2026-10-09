#nullable enable
using System;
using BigAmbitions.InputSystem;
using Helpers;
using PlayerActivity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The Food Safety course the vendor licence needs. The game has the course's name
    // (DiplomaName.FoodSafetyCourse) but no course data and no door for it [code, 1.0], so the mod adds the
    // data at load and offers the course at the business school's Market Demands door. The game's own study
    // panel runs it, so price, time skip and completion are the game's, and the result goes into the save's
    // PlayerDiplomas, where the licence check reads it. Nothing is paid before the panel's Start button.
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
                // Escape closes the question through its cancel action; only the button itself picks the
                // door's own course. Enter picks the confirm button, as everywhere in the game.
                HudConfirm.Show(HeaderKey, BodyKey,
                    () => StudyFoodSafety(door),
                    () => { if (!PlayerAction.Cancel.Pressed()) StudyOwnCourse(door); },
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
