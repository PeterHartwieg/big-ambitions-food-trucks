#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Entities;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Localizor;

namespace FoodTrucks
{
    // The street food business in the game's books: its profit in the day summary, and in Econoview a "Street food"
    // group with a row per vehicle that opens into its lines (Core's StreetFoodStatement decides the numbers).
    // Crew wages and parking are paid by the game into its own rows; the patches here note each vehicle's share
    // as it is charged, and Econoview shows that share under the vehicle and takes it off the game's rows.
    internal static class StreetFoodBooks
    {
        internal const string ScrollerTypeName = "UI.Smartphone.Apps.EconoView.EconoViewIncomeStatementScrollerController";
        private const string UnassignedWage = "ba:transaction_unassignedwage";
        // The game's fixed parking ticket (ParkingSimulator.RunHourly charges $125).
        private const long TicketCents = 12_500;

        private static readonly Dictionary<LedgerLine, string> LineKeys = new Dictionary<LedgerLine, string>
        {
            { LedgerLine.Sales, "foodtrucks:econoview_line_sales" },
            { LedgerLine.Catering, "foodtrucks:econoview_line_catering" },
            { LedgerLine.Stock, "foodtrucks:econoview_line_stock" },
            { LedgerLine.Wages, "foodtrucks:econoview_line_wages" },
            { LedgerLine.Parking, "foodtrucks:econoview_line_parking" },
            { LedgerLine.Permit, "foodtrucks:econoview_line_permit" },
            { LedgerLine.CommissaryRent, "foodtrucks:econoview_line_commissary" },
            { LedgerLine.OvernightFine, "foodtrucks:econoview_line_overnight_fines" },
        };

        private static MethodInfo? createRow;
        private static FieldInfo? rowName, rowParent, rowValues, rowExpanded;
        // Both of the game's rows have to lose the moved costs, or they would count twice: moving is on only when
        // both of those patches are in place (CheckMoves, after patching).
        private static bool movesActive;

        // Called once after patching.
        internal static void CheckMoves()
        {
            var scroller = AccessTools.TypeByName(ScrollerTypeName);
            // The Street food rows have to be there too, or the moved costs would show nowhere.
            movesActive = scroller != null && HasOurPostfix(scroller, "LoadSalaryRows") &&
                          HasOurPostfix(scroller, "LoadOngoingExpenseRows") && HasOurPostfix(scroller, "LoadFeeRows");
            Log.Info(movesActive
                ? "Econoview: crew wages and parking move from the game's rows to the vehicles."
                : "Econoview: crew wages and parking stay in the game's rows (a patch for moving them isn't active).");
        }

        private static bool HasOurPostfix(Type type, string method)
        {
            var original = AccessTools.Method(type, method, Type.EmptyTypes);
            var info = original == null ? null : Harmony.GetPatchInfo(original);
            return info != null && info.Postfixes.Any(p => p.PatchMethod.DeclaringType == typeof(StreetFoodBooks));
        }

        // Day summary: the street food profit counts in the game's total profit (day summary, history, rivals).
        public static void DaySummary(int dayNumber, FinancialSummary __result)
        {
            try
            {
                var state = FoodTrucksSession.CurrentState;
                if (__result == null || state == null) return;
                long profit;
                if (FoodTrucksSession.CanWrite)
                {
                    profit = SalesLedger.DayProfitCents(state, dayNumber);
                    SalesLedger.MarkInSummary(state, dayNumber);
                }
                else
                {
                    // Read-only data (written by a newer mod version) can't be marked, so only days already marked
                    // count, with what was added then; otherwise Econoview's rows and the total would disagree.
                    if (!SalesLedger.IsInSummary(state, dayNumber)) return;
                    profit = SalesLedger.SummaryCents(state, dayNumber);
                }
                if (profit != 0) __result.totalProfit += profit / 100f;
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // Econoview's "Street food" group, after the other income. Its children: a row per vehicle (closed, opening into
        // its lines), then the fleet's licence, event places and commissary delivery fees, then anything from before the breakdown.
        public static void IncomeRows(object __instance)
        {
            try
            {
                var statement = Statement(__instance);
                if (statement == null || statement.IsEmpty || !Reflect(__instance)) return;
                var group = Row(__instance, "Default", "foodtrucks:econoview_row", statement.Total, null);
                var names = VehicleNames();
                foreach (var vehicle in statement.Vehicles)
                {
                    var first = vehicle.Total[0];
                    var type = first > 0 ? "Success" : first < 0 ? "Danger" : "Warning";
                    var name = names.TryGetValue(vehicle.VehicleId, out var known) ? known : GoneName(vehicle.VehicleId);
                    var row = Row(__instance, type, name, vehicle.Total, group);
                    rowExpanded!.SetValue(row, false);
                    foreach (var line in vehicle.Lines)
                        Row(__instance, Sign(line.Value), LineKeys[line.Key], line.Value, row);
                }
                if (statement.Licence.Any(v => v != 0))
                    Row(__instance, Sign(statement.Licence), "foodtrucks:econoview_row_licence", statement.Licence, group);
                if (statement.EventPlaces.Any(v => v != 0))
                    Row(__instance, Sign(statement.EventPlaces), "foodtrucks:econoview_row_events", statement.EventPlaces, group);
                if (statement.DeliveryFees.Any(v => v != 0))
                    Row(__instance, Sign(statement.DeliveryFees), "foodtrucks:econoview_row_delivery_fees", statement.DeliveryFees, group);
                if (statement.NotBrokenDown.Any(v => v != 0))
                    Row(__instance, Sign(statement.NotBrokenDown), "foodtrucks:econoview_row_earlier", statement.NotBrokenDown, group);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // The game's "Unassigned staff wages" row (and its group) without the crew's wages shown under the vehicles.
        public static void OngoingExpenseRows(object __instance)
        {
            try { Move(__instance, "econoview_row_ongoing_expenses", "econoview_row_unassigned_staff_wages", s => s.MovedWages, dropEmptyGroup: false); }
            catch (Exception ex) { Log.Error(ex); }
        }

        // The game's "Parking fees" row (and its group) without the vehicles' parking.
        public static void FeeRows(object __instance)
        {
            try { Move(__instance, "econoview_row_fees", "econoview_row_parking_fees", s => s.MovedParking, dropEmptyGroup: true); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void Move(object instance, string groupName, string childName, Func<StreetFoodStatement, long[]> moved, bool dropEmptyGroup)
        {
            var statement = Statement(instance);
            if (statement == null || !Reflect(instance)) return;
            var cents = moved(statement);
            if (cents.All(c => c == 0)) return;
            var data = Traverse.Create(instance).Field("_data").GetValue() as IList;
            if (data == null) return;
            var rows = data.Cast<object>().ToList();
            var group = rows.LastOrDefault(r => (string)rowName!.GetValue(r) == groupName);
            var child = group == null ? null : rows.LastOrDefault(r => (string)rowName!.GetValue(r) == childName && ReferenceEquals(rowParent!.GetValue(r), group));
            if (group == null || child == null)
            {
                // Can't happen while Core only moves what the game's row holds; then the vehicles' lines count twice.
                Log.Error($"Econoview: no {childName} row to move {string.Join(", ", cents)} cents out of.");
                return;
            }
            var childValues = (List<float>)rowValues!.GetValue(child);
            var groupValues = (List<float>)rowValues.GetValue(group);
            for (var c = 0; c < cents.Length && c < childValues.Count && c < groupValues.Count; c++)
            {
                childValues[c] = Snap(childValues[c] + cents[c] / 100f);
                groupValues[c] = Snap(groupValues[c] + cents[c] / 100f);
            }
            if (childValues.All(v => v == 0f)) data.Remove(child);
            if (dropEmptyGroup && groupValues.All(v => v == 0f) && !data.Cast<object>().Any(r => ReferenceEquals(rowParent!.GetValue(r), group)))
                data.Remove(group);
        }

        // Float sums of the game's charges and the ledger's cents can differ by a few cents.
        private static float Snap(float value) => Math.Abs(value) * 100f < StreetFoodStatement.ToleranceCents ? 0f : value;

        private static StreetFoodStatement? Statement(object instance)
        {
            var summaries = Traverse.Create(instance).Field("_financialSummaries").GetValue<List<FinancialSummary>>();
            var state = FoodTrucksSession.CurrentState;
            if (summaries == null || summaries.Count == 0 || state == null) return null;
            var days = summaries.Select(s => s?.dayNumber ?? 0).ToList();
            var wages = summaries.Select(s => movesActive && s != null ? Cost(s.totalUnassignedStaffWages) : 0L).ToList();
            var parking = summaries.Select(s => movesActive && s != null ? Cost(s.parkingFees) : 0L).ToList();
            return StreetFoodStatement.Build(state, days, wages, parking);
        }

        // The game keeps costs as negative amounts.
        private static long Cost(float amount) => amount < 0f ? (long)Math.Round(-(double)amount * 100.0) : 0L;

        private static bool Reflect(object instance)
        {
            createRow ??= AccessTools.Method(instance.GetType(), "CreateRow");
            if (createRow == null) return false;
            var model = createRow.ReturnType;
            rowName ??= AccessTools.Field(model, "rowName");
            rowParent ??= AccessTools.Field(model, "parent");
            rowValues ??= AccessTools.Field(model, "values");
            rowExpanded ??= AccessTools.Field(model, "isExpanded");
            return rowName != null && rowParent != null && rowValues != null && rowExpanded != null;
        }

        private static object Row(object instance, string type, string name, long[] cents, object? parent)
        {
            var rowType = createRow!.GetParameters()[0].ParameterType;
            var values = cents.Select(c => c / 100f).ToList();
            return createRow.Invoke(instance, new object?[] { Enum.Parse(rowType, type), name, values, true, parent, null });
        }

        private static string Sign(long[] cents) => cents.Sum() >= 0 ? "Success" : "Danger";

        // "Food cart", "Food truck", numbered when the player has more than one of a kind, in the order of their IDs.
        internal static Dictionary<string, string> VehicleNames()
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            // Vehicles left with the private driver wait in the game's own list.
            var game = SaveGameManager.Current;
            var vehicles = game.VehicleInstances.Concat(game.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>())
                .Where(v => v != null && !string.IsNullOrEmpty(v.id) && FoodTrucksMod.KindOf(v.vehicleTypeName) != null)
                .GroupBy(v => v.id, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(v => v.id, StringComparer.Ordinal)
                .ToList();
            foreach (var kind in vehicles.GroupBy(v => v.vehicleTypeName))
            {
                var list = kind.ToList();
                var label = LocalizorManager.GetLocalization(kind.Key, null);
                for (var i = 0; i < list.Count; i++)
                    names[list[i].id] = list.Count > 1 ? $"{label} {i + 1}" : label;
            }
            return names;
        }

        // A vehicle the player no longer has.
        private static string GoneName(string vehicleId)
        {
            var kind = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId)?.VehicleType;
            var label = LocalizorManager.GetLocalization(kind == "truck" ? FoodTrucksMod.TruckTypeName : FoodTrucksMod.CartTypeName, null);
            return LocalizorManager.GetLocalization("foodtrucks:econoview_vehicle_gone", null).Replace("{vehicle}", label);
        }

        // Crew wages: the game pays each employee at midnight, into yesterday, for the hours counted that day. A wage
        // booked as unassigned is split over the vehicles the hire worked those hours on (CrewLog, written as
        // HireCrew counts each hour). Only for days from before the log does it go to the crew the hire is on.
        internal sealed class WageCall
        {
            public Transaction? Last;
            public float WorkedHours;
        }

        public static void WagePrefix(EmployeeInstance __instance, out WageCall? __state)
        {
            __state = null;
            try
            {
                var state = FoodTrucksSession.CurrentState;
                if (state == null || !FoodTrucksSession.CanWrite || __instance == null || SaveGameManager.Current == null) return;
                // Most employees are nobody's crew: skip them before touching the transactions.
                if (CrewVehicle(state, __instance.id) == null && !CrewLog.HasHours(state, SaveGameManager.Current.Day - 1, __instance.id)) return;
                __state = new WageCall { Last = LastTransaction(), WorkedHours = __instance.workedHoursToday };
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        public static void WagePostfix(EmployeeInstance __instance, WageCall? __state)
        {
            try
            {
                var state = FoodTrucksSession.CurrentState;
                if (__state == null || state == null || !FoodTrucksSession.CanWrite || __instance == null) return;
                var paid = LastTransaction();
                if (paid == null || ReferenceEquals(paid, __state.Last) || paid.transactionType != UnassignedWage || paid.amount >= 0f) return;
                var day = paid.timestamp.Day;
                var cents = (long)Math.Round(-(double)paid.amount * 100.0);
                var split = CrewLog.Attribute(state, day, __instance.id, cents, __state.WorkedHours, CrewVehicle(state, __instance.id)?.VehicleId);
                CrewLog.Consume(state, day, __instance.id);
                foreach (var (vehicleId, share) in split)
                {
                    if (!SalesLedger.RecordGameCost(state, day, vehicleId, LedgerLine.Wages, share))
                        Log.Info($"Econoview: wage of {CartSelling.Money(share)} for {HireCrew.Name(__instance)} on day {day} came after that day's summary; Econoview leaves it out.");
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static VehicleState? CrewVehicle(SaveState state, string employeeId) =>
            state.Vehicles.FirstOrDefault(v => v.Crew != null && v.Crew.Contains(employeeId));

        private static Transaction? LastTransaction()
        {
            var transactions = SaveGameManager.Current?.Transactions;
            return transactions == null || transactions.Count == 0 ? null : transactions.Last();
        }

        // Parking: at midnight the game charges each vehicle what it ran up the day before, into yesterday.
        // Rent covers a food vehicle in its commissary bay; clear its balance before noting the charges for Econoview.
        public static void ParkingDailyPrefix(out Dictionary<string, float>? __state)
        {
            __state = null;
            try
            {
                if (!FoodTrucksSession.CanWrite) return;
                foreach (var vehicle in FoodVehicles().Where(v => v.unpaidParkingAmount > 0f))
                {
                    var bay = Commissary.PublicParkingBay(vehicle);
                    if (bay != null)
                    {
                        vehicle.unpaidParkingAmount = 0f;
                        Log.Info($"Commissary {bay.Id}: no public parking for {LocalizorManager.GetLocalization(vehicle.vehicleTypeName, null)} " +
                                 $"{World.Short(vehicle.id)} tonight (in the rented bay).");
                        continue;
                    }
                    (__state ??= new Dictionary<string, float>(StringComparer.Ordinal))[vehicle.id] = vehicle.unpaidParkingAmount;
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        public static void ParkingDailyPostfix(Dictionary<string, float>? __state)
        {
            try
            {
                var state = FoodTrucksSession.CurrentState;
                if (__state == null || state == null || !FoodTrucksSession.CanWrite) return;
                var day = SaveGameManager.Current.Day - 1;
                foreach (var vehicle in FoodVehicles())
                {
                    // Charged when the game cleared the amount.
                    if (!__state.TryGetValue(vehicle.id, out var amount) || vehicle.unpaidParkingAmount != 0f) continue;
                    SalesLedger.RecordGameCost(state, day, vehicle.id, LedgerLine.Parking, (long)Math.Round(amount * 100.0));
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // Parking tickets: the game writes one hourly at most per illegally parked vehicle, into today.
        public static void ParkingHourlyPrefix(out Dictionary<string, int>? __state)
        {
            __state = null;
            try
            {
                if (!FoodTrucksSession.CanWrite) return;
                __state = FoodVehicles().ToDictionary(v => v.id, v => v.parkingTickets?.Count ?? 0, StringComparer.Ordinal);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        public static void ParkingHourlyPostfix(Dictionary<string, int>? __state)
        {
            try
            {
                var state = FoodTrucksSession.CurrentState;
                if (__state == null || state == null || !FoodTrucksSession.CanWrite) return;
                var day = SaveGameManager.Current.Day;
                foreach (var vehicle in FoodVehicles())
                {
                    if (!__state.TryGetValue(vehicle.id, out var before)) continue;
                    var tickets = (vehicle.parkingTickets?.Count ?? 0) - before;
                    if (tickets > 0) SalesLedger.RecordGameCost(state, day, vehicle.id, LedgerLine.Parking, tickets * TicketCents);
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static IEnumerable<VehicleInstance> FoodVehicles() =>
            SaveGameManager.Current.VehicleInstances.Where(v => v != null && !string.IsNullOrEmpty(v.id) && FoodTrucksMod.KindOf(v.vehicleTypeName) != null);
    }
}
