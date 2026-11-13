#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Buildings;
using Dialogs;
using Entities;
using Extensions;
using FoodTrucks.Core.Fleet;
using HarmonyLib;
using Helpers;
using Localizor;
using Streets;
using UI.Dialog;
using UnityEngine;
using UnityEngine.Events;
using Dropdown = UI.Elements.Dropdown;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    internal static class FleetRecruiting
    {
        private const string FleetNameKey = "foodtrucks:recruitment_fleet";

        private static readonly AccessTools.FieldRef<RecruitmentSettings, List<BuildingRegistration>>? Registrations =
            Field<RecruitmentSettings, List<BuildingRegistration>>("_buildingRegistrations");
        private static readonly AccessTools.FieldRef<RecruitmentSettings, string[]>? AgencySkills =
            Field<RecruitmentSettings, string[]>("_availableSkills");
        private static readonly AccessTools.FieldRef<RecruitmentSettings, Dropdown>? BusinessDropdown =
            Field<RecruitmentSettings, Dropdown>("businessDropdown");
        private static readonly AccessTools.FieldRef<RecruitmentSettings, Dropdown>? SkillsDropdown =
            Field<RecruitmentSettings, Dropdown>("skillsDropdown");
        private static readonly AccessTools.FieldRef<RecruitmentCampaignsList, Transform>? ContractEntry =
            Field<RecruitmentCampaignsList, Transform>("contractEntry");
        private static readonly System.Reflection.MethodInfo? StartMethod = AccessTools.Method(typeof(RecruitmentAgencyDialog), "Start", new[] { typeof(Action) });
        private static readonly System.Reflection.MethodInfo? PropertiesMethod = AccessTools.Method(typeof(RecruitmentAgencyDialog), "CandidateProperties");

        private static bool failed;

        private static AccessTools.FieldRef<T, F>? Field<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception ex)
            {
                Log.Error($"Fleet recruiting: field {typeof(T).Name}.{name} not found ({ex.Message}); the agency recruits for buildings only.");
                return null;
            }
        }

        private sealed class FleetEntry : BuildingRegistration
        {
            public FleetEntry() { BusinessName = FleetName(); }
        }

        internal static bool IsFleet(RecruitmentCampaign? campaign) =>
            campaign != null && (campaign.businessAddress == null || campaign.businessAddress.IsUndefined() || string.IsNullOrEmpty(campaign.businessAddress.streetName));

        internal static string FleetName() => LocalizorManager.GetLocalization(FleetNameKey, null);

        private static int FoodVehicles() =>
            SaveGameManager.Current?.VehicleInstances?.Count(v => v != null && FoodTrucksMod.KindOf(v.vehicleTypeName) != null) ?? 0;

        private static string[]? AgencySkillsHere()
        {
            var address = DialogController.current?.contact?.Address;
            var building = address == null ? null : BuildingHelper.GetBuilding(address);
            return (building?.SpecialService?.settings as RecruitmentAgencySettings)?.availableEmployeeSkills;
        }

        private static bool CanWork => FoodTrucksSession.CurrentState != null && !Commissary.Uninstalled;

        private static void Fail(string what, Exception ex)
        {
            if (!failed) Log.Error($"Fleet recruiting: {what}: {ex}");
            failed = true;
        }

        internal static bool NoBusinessesPrefix(RecruitmentAgencyDialog __instance, ref DialogEntry __result)
        {
            try
            {
                if (!CanWork || StartMethod == null || PropertiesMethod == null) return true;
                if (FleetRecruitment.Skills(FoodVehicles(), AgencySkillsHere()).Count == 0) return true;
                var dialog = __instance;
                Action next = () =>
                {
                    try { DialogController.current.ShowEntry((DialogEntry)PropertiesMethod.Invoke(dialog, null)); }
                    catch (Exception ex) { Fail("the campaign form", ex); }
                };
                __result = (DialogEntry)StartMethod.Invoke(dialog, new object[] { next });
                Log.Info("Fleet recruiting: the agency offers a campaign for the street food fleet (no building business).");
                return false;
            }
            catch (Exception ex)
            {
                Fail("the agency's greeting", ex);
                return true;
            }
        }

        internal static void SettingsEnablePostfix(RecruitmentSettings __instance)
        {
            try
            {
                if (!CanWork || Registrations == null || AgencySkills == null || BusinessDropdown == null || SkillsDropdown == null) return;
                if (FleetRecruitment.Skills(FoodVehicles(), AgencySkills(__instance)).Count == 0) return;
                var list = Registrations(__instance);
                if (list == null || list.Any(r => r is FleetEntry)) return;
                list.Add(new FleetEntry());
                var dropdown = BusinessDropdown(__instance);
                dropdown.SetOptions(list.Select(r => r.GetDisplayName()).ToList(), localize: false);
                dropdown.SetPlaceholder("dialog_select_business");
            }
            catch (Exception ex) { Fail("the campaign form's list", ex); }
        }

        internal static bool SelectBusinessPrefix(RecruitmentSettings __instance, int businessIndex)
        {
            try
            {
                if (Registrations == null || SkillsDropdown == null || AgencySkills == null) return true;
                var list = Registrations(__instance);
                if (list == null || businessIndex < 0 || businessIndex >= list.Count || !(list[businessIndex] is FleetEntry fleet)) return true;
                var skills = CanWork ? FleetRecruitment.Skills(FoodVehicles(), AgencySkills(__instance)) : Array.Empty<string>();
                var dropdown = SkillsDropdown(__instance);
                if (skills.Count == 0)
                {
                    UI.Notification.Notifications.Show(UI.Notification.NotificationType.Warning, "dialog_recruitment_no_skills_available_notification", null, 4f,
                        "dialog_recruitment_no_skills_available_notification");
                    __instance.selectedBusiness = null;
                    __instance.hasSelectedSkill = false;
                    dropdown.ResetSelectedOption();
                    dropdown.SetInteractable(interactable: false);
                    return false;
                }
                __instance.selectedBusiness = fleet;
                __instance.businessSkills = skills.ToList();
                if (__instance.hasSelectedSkill)
                {
                    __instance.hasSelectedSkill = false;
                    dropdown.ResetSelectedOption();
                }
                dropdown.SetOptions(__instance.businessSkills.ToList());
                dropdown.SetInteractable(interactable: true);
                return false;
            }
            catch (Exception ex)
            {
                Fail("picking the fleet", ex);
                return true;
            }
        }

        internal static bool SettingsSetPrefix(ref DialogEntry? __result)
        {
            try
            {
                var form = DialogController.current?.GetInputTransform<RecruitmentSettings>(null);
                if (form == null || !(form.selectedBusiness is FleetEntry) || CanWork) return true;
                UI.Notification.Notifications.ShowError("common_notification_select_business");
                __result = null;
                return false;
            }
            catch (Exception ex)
            {
                Fail("checking the form", ex);
                return true;
            }
        }

        internal static void SettingsSetPostfix(DialogEntry __result)
        {
            try
            {
                if (__result == null) return;
                var campaign = SaveGameManager.Current?.RecruitmentCampaigns?.LastOrDefault();
                if (!IsFleet(campaign)) return;
                Log.Info($"Fleet recruiting: campaign for {campaign.amountOfCandidates} {campaign.skillRequirement?.skillName} candidates for the street food fleet, " +
                         $"${campaign.price:F2}, last candidate by day {campaign.candidateFindTimes?.Select(t => t.Day).DefaultIfEmpty(0).Max()}.");
            }
            catch (Exception ex) { Fail("the campaign's log line", ex); }
        }

        internal static bool ContractPrefix(RecruitmentCampaignsList __instance, RecruitmentCampaign recruitmentCampaign)
        {
            try
            {
                if (!IsFleet(recruitmentCampaign) || ContractEntry == null) return true;
                var template = ContractEntry(__instance);
                if (template == null) return true;
                var entry = Object.Instantiate(template, template.parent);
                var skillName = recruitmentCampaign.skillRequirement?.skillName ?? "";
                var times = recruitmentCampaign.candidateFindTimes ?? new List<BigAmbitions.DayNightCycle.Timestamp>();
                var lastDay = times.Count == 0 ? SaveGameManager.Current.Day : times.OrderBy(t => t.GetTotalMinutes()).Last().Day;
                entry.GetLanguageChangeEventByName("Info").SetData(LocalizorManager.Localize("dialog_recruitment_campaigns_list_info", new
                {
                    businessName = FleetName(),
                    skillKey = LocalizorManager.GetLocalization(skillName, null),
                    amountOfCandidates = recruitmentCampaign.amountOfCandidates,
                    scheduleTypes = recruitmentCampaign.GetScheduleTypesInfo(),
                    daysLeft = Mathf.Max(1, lastDay - SaveGameManager.Current.Day),
                    amountOfCandidatesLeft = recruitmentCampaign.amountOfCandidates - recruitmentCampaign.candidatesFound,
                }));
                entry.gameObject.SetActive(true);
                var campaign = recruitmentCampaign;
                entry.GetButtonByName("Buttons/CancelCampaignButton").onClick.AddListener(() =>
                    ((RecruitmentAgencyDialog)DialogController.current.dialog).OnCancelCampaign(campaign).ShowEntry());
                return false;
            }
            catch (Exception ex)
            {
                Fail("the campaign list", ex);
                return true;
            }
        }

        internal static (long Refunded, int Left) EndForUninstall()
        {
            var campaigns = SaveGameManager.Current?.RecruitmentCampaigns;
            if (campaigns == null) return (0, 0);
            long refunded = 0;
            var left = 0;
            foreach (var campaign in campaigns.Where(IsFleet).ToList())
            {
                var cents = FleetRecruitment.RefundCents((long)Math.Round(campaign.price * 100.0), campaign.amountOfCandidates, campaign.candidatesFound);
                if (cents > 0)
                {
                    var info = new TransactionInfo("ba:transaction_recruitmentcampaign", new Dictionary<string, string> { { "businessName", FleetName() } });
                    if (!Payments.PayIn(cents, info))
                    {
                        Log.Error($"Uninstall: the refund of {CartSelling.Money(cents)} for a fleet recruitment campaign was refused; the campaign stays.");
                        left++;
                        continue;
                    }
                    refunded += cents;
                }
                campaigns.Remove(campaign);
                Log.Info($"Uninstall: fleet recruitment campaign ended ({campaign.candidatesFound} of {campaign.amountOfCandidates} found), {CartSelling.Money(cents)} back.");
            }
            if (left > 0) CartSelling.Notify("foodtrucks:notification_uninstall_fleet_campaigns_left", ("count", left.ToString()));
            return (refunded, left);
        }
    }
}
