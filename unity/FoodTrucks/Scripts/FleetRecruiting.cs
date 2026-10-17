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
    // The game's recruitment agency recruits for the street food fleet (Peter, 17 October 2026). In 1.0 the agency wants a
    // building business with a name (RecruitmentAgencyDialog's constructor), so a player with only carts and trucks
    // couldn't hire at all. The game's own Basic HR course check stays: it comes first in the constructor.
    //
    // The fleet is one more entry in the campaign form's business list: a stand-in BuildingRegistration with no street,
    // never in the save, named "Street food fleet". Its Address is null (BuildingRegistration.Address reads its building,
    // and an empty street has none), so the game's own campaign code books the price unattributed to a business, saves the
    // campaign with no business address, and generates candidates with no assigned address: they arrive unassigned, as the
    // game's own debug candidates do (RecruitmentCommands), and HireCandidate keeps them so. Everything else is the game's:
    // the price, the deadline, the candidates' hours and wages, the hiring in My Employees.
    //
    // A campaign with no business address is the fleet's. The agency's campaign list reads that address's business name,
    // so it shows the fleet's name for one; without the mod the list would fail on it, so the uninstall step ends the
    // fleet's campaigns and refunds the candidates not found yet.
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

        // The stand-in for the fleet in the form's business list. One per form opening; recognised by its name and empty street.
        private sealed class FleetEntry : BuildingRegistration
        {
            public FleetEntry() { BusinessName = FleetName(); }
        }

        // A campaign without a business: the fleet's (a save may bring the missing address back as an empty one).
        internal static bool IsFleet(RecruitmentCampaign? campaign) =>
            campaign != null && (campaign.businessAddress == null || campaign.businessAddress.IsUndefined() || string.IsNullOrEmpty(campaign.businessAddress.streetName));

        internal static string FleetName() => LocalizorManager.GetLocalization(FleetNameKey, null);

        private static int FoodVehicles() =>
            SaveGameManager.Current?.VehicleInstances?.Count(v => v != null && FoodTrucksMod.KindOf(v.vehicleTypeName) != null) ?? 0;

        // The agency's skills for this conversation's agency, or null.
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

        // Prefix on RecruitmentAgencyDialog.NoBusinesses, which the constructor shows after the Basic HR check when the player
        // has no named building business. With a food vehicle and an agency that recruits customer service, the
        // conversation goes on as for a business: the agency's greeting, then the campaign form.
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

        // Postfix on RecruitmentSettings.OnEnable: the fleet as the last entry of the form's business list.
        internal static void SettingsEnablePostfix(RecruitmentSettings __instance)
        {
            try
            {
                if (!CanWork || Registrations == null || AgencySkills == null || BusinessDropdown == null) return;
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

        // Prefix on RecruitmentSettings.SelectBusiness: the fleet picked. Its skill is customer service; the rest is the
        // game's form as for a business (the schedule toggles follow the skill when it is picked).
        internal static bool SelectBusinessPrefix(RecruitmentSettings __instance, int businessIndex)
        {
            try
            {
                if (Registrations == null || SkillsDropdown == null || AgencySkills == null) return true;
                var list = Registrations(__instance);
                if (list == null || businessIndex < 0 || businessIndex >= list.Count || !(list[businessIndex] is FleetEntry fleet)) return true;
                // The stand-in never reaches the game's own SelectBusiness, which reads its building's type (review round 2).
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

        // Prefix on RecruitmentAgencyDialog.OnRecruitmentSettingsSet: a form still open on the fleet after the uninstall step
        // starts nothing (review round 1): the game's own "select a business", and the form stays.
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

        // Postfix on RecruitmentAgencyDialog.OnRecruitmentSettingsSet: a log line for a fleet campaign the game started.
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

        // Prefix on RecruitmentCampaignsList.SetUpContract: a fleet campaign (no business address) listed with the fleet's
        // name. The rest is the game's own entry, line for line.
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

        // The uninstall step: the fleet's campaigns end, and the price of the candidates not found yet comes back, so no
        // campaign the game can't show is left in the save. A campaign whose refund is refused stays, and the player is
        // told to run the step again before removing the mod. Returns the refund in cents and the campaigns left.
        internal static (long Refunded, int Left) EndForUninstall()
        {
            var campaigns = SaveGameManager.Current?.RecruitmentCampaigns;
            if (campaigns == null) return (0, 0);
            long refunded = 0;
            var left = 0;
            foreach (var campaign in campaigns.Where(IsFleet).ToList())
            {
                var cents = FleetRecruitment.RefundCents((long)Math.Round(campaign.price * 100.0), campaign.amountOfCandidates, campaign.candidatesFound);
                // Paid back first; a refused refund keeps the campaign, so nothing is lost and the step can run again.
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
