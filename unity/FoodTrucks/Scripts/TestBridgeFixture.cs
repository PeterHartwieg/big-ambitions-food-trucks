#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using Blueprints;
using Buildings;
using Buildings.Office.Headquarters;
using BusinessLayoutSets;
using Entities;
using FoodTrucks.Core.Commissary;
using Helpers;
using Newtonsoft.Json.Linq;
using Streets;
using UI;
using UnityEngine;
using Vehicles.VehicleTypes;
using CoreLogistics = FoodTrucks.Core.Commissary.CommissaryLogistics;
using ItemTags = BigAmbitions.Tags.TagRef.Itemtag;

namespace FoodTrucks
{
    // The bridge's fixture and logistics commands (docs/test-bridge.md, "fixture logistics", "logistics"). The fixture
    // turns the loaded FT save into one where a logistics plan delivers to a commissary: a warehouse with pallets and
    // stock, a headquarters with a logistics manager, a delivery vehicle and driver in the warehouse, a space at the
    // commissary and a plan from the warehouse to it. Each step uses the game's own helpers, as the console's RentBuilding,
    // BizMan's Start business, the hiring screen, the warehouse's driver dropdown and the plan screen do; a step finds
    // what an earlier run made and leaves it. One log line per step.
    internal static partial class TestBridgeCommands
    {
        internal const string FixtureWarehouseName = "FT Fixture Warehouse";
        internal const string FixtureHeadquartersName = "FT Fixture HQ";
        private const string WarehouseType = "ba:businesstype_warehouse";
        private const string HeadquartersType = "ba:businesstype_headquarters";
        private const string WarehouseBuilding = "ba:buildingtype_warehouse";
        private const string LogisticsSkill = "ba:skill_logisticsmanager";
        private const string DriverSkill = "ba:skill_deliverydriver";
        private const string HotDog = "ba:itemname_hotdog";

        // Thrown by a step that can't be done: the fixture stops there and says why.
        private sealed class StepFailed : Exception
        {
            public StepFailed(string message) : base(message) { }
        }

        private sealed class FixtureReport
        {
            public readonly List<JObject> Steps = new List<JObject>();

            public void Add(string step, string status, string detail)
            {
                Steps.Add(new JObject { ["step"] = step, ["status"] = status, ["detail"] = detail });
                var line = $"Test bridge: fixture logistics, {step}: {status}: {detail}";
                if (status == "failed") Log.Error(line);
                else Log.Info(line);
            }
        }

        // { "name": "logistics", "site"?, "stock"?, "targets"?, "vehicle"? }
        private static JToken Fixture(JObject args)
        {
            var name = Need(args, "name", "the fixture's name: logistics");
            if (name != "logistics") throw new BridgeError($"No fixture '{name}'. Fixtures: logistics.");
            if (BuildingManager.IsInsideBuilding)
                throw new BridgeError("The player is inside a building; the game places furniture and vehicles only in buildings the player isn't in. Teleport the player outside first.");
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            if (!FoodTrucksSession.CanWrite) throw new BridgeError("The mod's save data is read-only.");
            var site = Site(Str(args, "site") ?? "hells-kitchen");
            var stock = ItemRows(args, "stock", "units", new[] { (HotDog, 200) });
            var targets = ItemRows(args, "targets", "amount", new[] { (HotDog, 40) });
            var prices = new Dictionary<string, float>(StringComparer.Ordinal);
            if (args["stock"] is JArray rows)
                foreach (var row in rows.OfType<JObject>())
                    if (Num(row, "price") is double price && Str(row, "item") is string item) prices[ItemKey(item)] = (float)price;

            var report = new FixtureReport();
            var context = new FixtureContext { Site = site, Stock = stock, Targets = targets, Prices = prices, VehicleId = Str(args, "vehicle") };
            var steps = new (string Name, Func<FixtureContext, (string Status, string Detail)> Run)[]
            {
                ("commissary_space", c => CommissarySpace(c, state)),
                ("warehouse", FixtureWarehouse),
                ("pallets", FixturePallets),
                ("stock", FixtureStock),
                ("headquarters", FixtureHeadquarters),
                ("logistics_manager", FixtureManager),
                ("vehicle", FixtureVehicle),
                ("driver", FixtureDriver),
                ("plan", FixturePlan),
            };
            var stopped = false;
            foreach (var (stepName, run) in steps)
            {
                if (stopped)
                {
                    report.Add(stepName, "not_run", "an earlier step failed");
                    continue;
                }
                try
                {
                    var (status, detail) = run(context);
                    report.Add(stepName, status, detail);
                }
                catch (StepFailed ex)
                {
                    report.Add(stepName, "failed", ex.Message);
                    stopped = true;
                }
                catch (Exception ex)
                {
                    report.Add(stepName, "failed", "the game threw: " + ex);
                    stopped = true;
                }
            }
            SaveGameManager.MarkChange();
            FoodTrucksSession.MarkChanged();
            return new JObject
            {
                ["fixture"] = "logistics",
                ["complete"] = !stopped,
                ["site"] = site.Id,
                ["steps"] = new JArray(report.Steps),
                ["warehouse"] = context.Warehouse == null ? null : BusinessJson(context.Warehouse),
                ["headquarters"] = context.Headquarters == null ? null : BusinessJson(context.Headquarters),
                ["plan"] = context.Plan == null ? null : PlanJson(context.Plan),
            };
        }

        private sealed class FixtureContext
        {
            public CommissarySite Site = null!;
            public List<(string Item, int Amount)> Stock = new List<(string, int)>();
            public List<(string Item, int Amount)> Targets = new List<(string, int)>();
            public Dictionary<string, float> Prices = new Dictionary<string, float>();
            public string? VehicleId;
            public Warehouse? Warehouse;
            public BuildingRegistration? Headquarters;
            public EmployeeInstance? Manager;
            public LogisticsManagerPlan? Plan;
        }

        // [ { "item", "<amountKey>" } ], or the default; an item is a key or the game's name for it.
        private static List<(string Item, int Amount)> ItemRows(JObject args, string key, string amountKey, (string, int)[] fallback)
        {
            if (args[key] == null || args[key]!.Type == JTokenType.Null) return fallback.ToList();
            if (!(args[key] is JArray rows) || rows.Count == 0) throw new BridgeError($"\"{key}\" is a list: [ {{ \"item\", \"{amountKey}\" }} ].");
            var list = new List<(string, int)>();
            foreach (var row in rows.OfType<JObject>())
            {
                var item = ItemKey(Need(row, "item", "an item key or name"));
                var amount = Num(row, amountKey) ?? throw new BridgeError($"Each \"{key}\" row needs \"{amountKey}\".");
                if (amount < 0 || amount > 100000 || amount != Math.Floor(amount)) throw new BridgeError($"\"{amountKey}\" is a whole number from 0 to 100,000.");
                list.Add((item, (int)amount));
            }
            return list;
        }

        private static string ItemKey(string text)
        {
            if (ItemsGetter.GetByName(text, suppressError: true) != null) return text;
            var match = ItemsGetter.AllItems?.FirstOrDefault(i => i != null &&
                string.Equals(Localizor.LocalizorManager.GetLocalization(i.itemName, null), text, StringComparison.OrdinalIgnoreCase));
            return match?.itemName ?? throw new BridgeError($"No game item '{text}'.");
        }

        // ---- Steps ----

        // The player rents a space at the commissary with the Commissary button's Rent (Commissary.RentForTesting).
        private static (string, string) CommissarySpace(FixtureContext c, FoodTrucks.Core.Saves.SaveState state)
        {
            if (CommissaryOrdering.CanOrder(state.Commissary, c.Site.Id))
            {
                var space = state.Commissary.Spaces.First(s => s.CommissaryId == c.Site.Id);
                return ("already", $"{c.Site.Id} has a space ({space.Kind} {space.VehicleId})");
            }
            var food = SaveGameManager.Current.VehicleInstances.Where(v => v != null && FoodTrucksMod.KindOf(v.vehicleTypeName) != null).ToList();
            VehicleInstance? vehicle;
            if (c.VehicleId != null) vehicle = FoodVehicle(c.VehicleId);
            else vehicle = food.FirstOrDefault(v => !state.Commissary.Spaces.Any(s => s.VehicleId == v.id));
            if (vehicle == null)
                throw new StepFailed(food.Count == 0
                    ? "no food cart or truck in this save; a space is rented for a vehicle (debug spawn truck, or buy one), then run the fixture again"
                    : $"every food vehicle has a space elsewhere; pass \"vehicle\" to move one here ({string.Join(", ", food.Select(v => v.id))})");
            var refusal = global::FoodTrucks.Commissary.RentForTesting(vehicle.id, c.Site.Id);
            if (refusal != null) throw new StepFailed(refusal);
            return ("done", $"{FoodTrucksMod.KindOf(vehicle.vehicleTypeName)} {vehicle.id} rented a space at {c.Site.Id} (the Commissary button's Rent, first day's rent charged)");
        }

        // A warehouse building near the commissary, rented as the console's RentBuilding does (BuildingHelper.RentBuilding,
        // no rent, no deposit) and started as a warehouse as BizMan's Start business does (StartBusinessUI.SetUpBusiness).
        private static (string, string) FixtureWarehouse(FixtureContext c)
        {
            var existing = PlayerBusinesses(WarehouseType).OfType<Warehouse>().Where(r => r.GetBuildingType() == WarehouseBuilding)
                .OrderBy(r => r.BusinessName == FixtureWarehouseName ? 0 : 1).FirstOrDefault();
            if (existing != null)
            {
                c.Warehouse = existing;
                return ("already", $"the player's warehouse {existing.BusinessName} at {existing.Address}");
            }
            var origin = CommissaryDoor(c.Site);
            var building = NearestRentable(origin, b => b.BuildingType == WarehouseBuilding && Offers(b, WarehouseType) &&
                (BuildingSizeHelper.GetData(b)?.numberOfVehicleSlots ?? 0) >= 1)
                ?? throw new StepFailed("no warehouse building with a vehicle slot is free to rent; rent one by hand (BizMan or the console's RentBuilding) and run the fixture again");
            BuildingHelper.RentBuilding(building, 0f, 0f);
            if (!(building.GetRegistration() is Warehouse warehouse))
                throw new StepFailed($"the game's registration of {building.Address} isn't a warehouse after renting it");
            var course = StartBusiness(warehouse, WarehouseType, UniqueName(FixtureWarehouseName));
            c.Warehouse = warehouse;
            return ("done", $"rented {building.Address} ({building.BuildingSize}, version {building.BuildingVersion}, {Metres(origin, building)} m from {c.Site.Id}'s door) " +
                            $"at no rent and started {warehouse.BusinessName}, {warehouse.vehicleSlots.Count} vehicle slots{course}");
        }

        // Pallet storage (items tagged iswarehousestorage) registered in the warehouse as the game's layout insert does
        // (ItemHelper.InitializeNewInstance, BuildingRegistration.AddItemInstanceToBuilding), enough for the stock. The
        // places come from the game's own layout for a building of this size when it has pallets, else beside the
        // building's delivery spot.
        private static (string, string) FixturePallets(FixtureContext c)
        {
            var warehouse = c.Warehouse!;
            var pallets = warehouse.itemInstances.Values.Where(IsPallet).ToList();
            var needed = 0;
            foreach (var (item, amount) in c.Stock)
            {
                var missing = Math.Max(0, amount - BuildingHelper.CountResourcesInPallets(warehouse.Address, item));
                var box = Math.Max(1, ItemsGetter.GetByName(item).boxSize);
                needed += (missing + box - 1) / box;
            }
            var free = pallets.Sum(p => Math.Max(0, p.ItemCached.cargoCapacity - p.cargoInstances.Count));
            if (needed <= free && pallets.Count > 0)
                return ("already", $"{pallets.Count} pallets with room for {free} boxes; the stock needs {needed}");
            var places = PalletPlaces(warehouse, out var source);
            var added = new List<ItemInstance>();
            foreach (var (itemName, position, yaw) in places)
            {
                if (free >= needed && pallets.Count + added.Count > 0) break;
                var instance = ItemHelper.InitializeNewInstance(itemName);
                instance.position = position;
                instance.yRotation = yaw;
                warehouse.AddItemInstanceToBuilding(instance);
                instance.priceOnPurchase = instance.ItemCached.DefaultMarketPrice;
                added.Add(instance);
                free += instance.ItemCached.cargoCapacity;
            }
            if (free < needed)
                throw new StepFailed($"room for {free} boxes after placing {added.Count} pallets ({source}); the stock needs {needed}. Place more pallets by hand or ask for less stock");
            GameEvent.Invoke("ba:gameevent_itemcargochanged");
            return ("done", $"placed {added.Count} {string.Join(", ", added.Select(a => a.itemName).Distinct())} ({source}), room for {free} boxes");
        }

        // Stock on the pallets through the game's own delivery into a building (ItemHelper.DeliverCargoToBuilding, as an
        // import partnership delivers), up to each item's amount, priced per unit (the item's wholesale price unless given).
        private static (string, string) FixtureStock(FixtureContext c)
        {
            var warehouse = c.Warehouse!;
            var done = new List<string>();
            var already = new List<string>();
            foreach (var (item, amount) in c.Stock)
            {
                var have = BuildingHelper.CountResourcesInPallets(warehouse.Address, item);
                if (have >= amount)
                {
                    already.Add($"{have} {item}");
                    continue;
                }
                var price = c.Prices.TryGetValue(item, out var given) ? given : ItemsGetter.GetByName(item).GetWholesalePrice();
                var cargo = new CargoInstance(item, amount - have, price);
                ItemHelper.DeliverCargoToBuilding(cargo, warehouse, IsPallet);
                if (cargo.amount > 0) throw new StepFailed($"{cargo.amount} {item} didn't fit on the pallets");
                done.Add($"{amount - have} {item} at ${price:F2} a unit (now {BuildingHelper.CountResourcesInPallets(warehouse.Address, item)})");
            }
            if (done.Count == 0) return ("already", "the pallets hold " + string.Join(", ", already));
            GameEvent.Invoke("ba:gameevent_itemcargochanged");
            return ("done", string.Join(", ", done) + (already.Count > 0 ? "; already there: " + string.Join(", ", already) : ""));
        }

        // A headquarters, rented and started as the warehouse is; the player's own if there is one.
        private static (string, string) FixtureHeadquarters(FixtureContext c)
        {
            var existing = PlayerBusinesses(HeadquartersType).OrderBy(r => r.BusinessName == FixtureHeadquartersName ? 0 : 1).FirstOrDefault();
            if (existing != null)
            {
                c.Headquarters = existing;
                return ("already", $"the player's headquarters {existing.BusinessName} at {existing.Address}");
            }
            var origin = CommissaryDoor(c.Site);
            var building = NearestRentable(origin, b => b.BuildingType != WarehouseBuilding && Offers(b, HeadquartersType))
                ?? throw new StepFailed("no building that takes a headquarters is free to rent; rent an office by hand and start a headquarters there");
            BuildingHelper.RentBuilding(building, 0f, 0f);
            var registration = building.GetRegistration();
            var course = StartBusiness(registration, HeadquartersType, UniqueName(FixtureHeadquartersName));
            c.Headquarters = registration;
            return ("done", $"rented {building.Address} ({building.BuildingSize}, {Metres(origin, building)} m from {c.Site.Id}'s door) at no rent and started {registration.BusinessName}{course}");
        }

        // A logistics manager working for the headquarters, hired as the game's recruitment makes a candidate
        // (RecruitmentHelper.GenerateCandidate with the headquarters as the assigned business, then EmployeeHelper.HireCandidate).
        private static (string, string) FixtureManager(FixtureContext c)
        {
            var hq = c.Headquarters!.Address;
            var plans = SaveGameManager.Current.logisticsManagerPlans;
            var mine = plans.FirstOrDefault(p => !p.isFactory && p.targetAddress == c.Warehouse!.Address);
            // The plan's own manager first, then any free one of this headquarters.
            var existing = EmployeeHelper.GetEmployeeInstances().Where(e => e != null && e.HasSkill(LogisticsSkill) && e.assignedAddress == hq &&
                    !plans.Any(p => p != mine && p.assignedEmployeeId == e.id))
                .OrderBy(e => mine != null && mine.assignedEmployeeId == e.id ? 0 : 1).FirstOrDefault();
            if (existing != null)
            {
                c.Manager = existing;
                return ("already", $"{HireCrew.Name(existing)} ({existing.id}), logistics {existing.GetSkillValue(LogisticsSkill):F0}, works for {c.Headquarters.BusinessName}");
            }
            var candidate = RecruitmentHelper.GenerateCandidate(LogisticsSkill, 60f, hq)
                ?? throw new StepFailed("the game made no candidate");
            EmployeeHelper.HireCandidate(candidate);
            c.Manager = candidate;
            return ("done", $"hired {HireCrew.Name(candidate)} ({candidate.id}), logistics 60, ${candidate.hourlyWage:F2} an hour, for {c.Headquarters.BusinessName}. " +
                            "No work shift: the headquarters has no workstation (see the doc)");
        }

        // A delivery vehicle in the warehouse's first slot, placed as the game's vehicle delivery does
        // (VehicleDeliveryHelper.TryPlaceVehicleInWarehouse: the slot's parking place, VehicleSlot.AssignVehicle), free.
        private static (string, string) FixtureVehicle(FixtureContext c)
        {
            var warehouse = c.Warehouse!;
            var slotted = warehouse.vehicleSlots.FirstOrDefault(s => VehicleOf(s) != null);
            if (slotted != null)
            {
                var v = VehicleOf(slotted)!;
                return ("already", $"slot {warehouse.vehicleSlots.IndexOf(slotted) + 1} has a {v.vehicleTypeName} ({v.id}), {v.VehicleType.destinationsThatCanDeliver} destinations");
            }
            if (warehouse.vehicleSlots.Count == 0) throw new StepFailed("the warehouse has no vehicle slots");
            var type = DeliveryVehicleType() ?? throw new StepFailed("the game has no vehicle type that can deliver from a warehouse");
            var slotIndex = warehouse.vehicleSlots.FindIndex(s => string.IsNullOrEmpty(s.vehicleInstanceId));
            var controllers = InstanceBehavior<BuildingManager>.Instance.GetBuildingTransform(new BuildingSizeInfo(warehouse)).GetComponentsInChildren<WarehouseSlotController>();
            var place = controllers.FirstOrDefault(x => x.slotIndex == slotIndex + 1)
                ?? throw new StepFailed($"the warehouse's interior has no parking place for slot {slotIndex + 1}");
            var colours = InstanceBehavior<GlobalReferences>.Instance.vehicleColors;
            var vehicle = new VehicleInstance(type.vehicleTypeName)
            {
                vehicleColorName = colours != null && colours.Length > 0 ? colours[0].name : null,
                fuel = type.maxFuel,
                streetName = warehouse.StreetName,
                streetNumber = warehouse.StreetNumber,
                position = place.vehiclePosition.position,
                rotation = place.vehiclePosition.rotation,
            };
            warehouse.vehicleSlots[slotIndex].AssignVehicle(vehicle);
            SaveGameManager.Current.VehicleInstances.Add(vehicle);
            return ("done", $"a {type.vehicleTypeName} ({vehicle.id}) in slot {slotIndex + 1}: {type.destinationsThatCanDeliver} destinations, " +
                            $"driver skill {type.requiredDeliveryDriverSkillValue} needed; no money paid");
        }

        // A delivery driver for that slot, hired for the warehouse and set as the warehouse's driver dropdown does
        // (DriverStation.SetUp: VehicleSlot.employeeDriverId, EmployeeInstance.UpdateWeeklyHoursAndDays).
        private static (string, string) FixtureDriver(FixtureContext c)
        {
            var warehouse = c.Warehouse!;
            var slot = warehouse.vehicleSlots.First(s => VehicleOf(s) != null);
            var vehicle = VehicleOf(slot)!;
            var current = string.IsNullOrEmpty(slot.employeeDriverId) ? null : EmployeeHelper.GetEmployeeById(slot.employeeDriverId, showError: false);
            if (current != null && Warehouse.CanDriverOperateVehicle(current, vehicle))
                return ("already", $"{HireCrew.Name(current)} ({current.id}) drives slot {warehouse.vehicleSlots.IndexOf(slot) + 1}; max destinations {MaxDestinations(c)}");
            var needed = vehicle.VehicleType.requiredDeliveryDriverSkillValue;
            var driver = EmployeeHelper.GetEmployeeInstances().FirstOrDefault(e => e != null && e.HasSkill(DriverSkill) &&
                e.assignedAddress == warehouse.Address && !warehouse.IsDriverAssignedToAnySlot(e) && Warehouse.CanDriverOperateVehicle(e, vehicle));
            var hired = driver == null;
            if (driver == null)
            {
                driver = RecruitmentHelper.GenerateCandidate(DriverSkill, Math.Max(60f, needed), warehouse.Address)
                    ?? throw new StepFailed("the game made no candidate");
                EmployeeHelper.HireCandidate(driver);
            }
            var before = current;
            slot.employeeDriverId = driver.id;
            before?.UpdateWeeklyHoursAndDays();
            driver.UpdateWeeklyHoursAndDays();
            GameEvent.Invoke("ba:gameevent_employeeassigned");
            var max = MaxDestinations(c);
            if (max < 1) throw new StepFailed($"{HireCrew.Name(driver)} drives the slot, but the game counts 0 destinations for the plan");
            return ("done", $"{(hired ? "hired" : "assigned")} {HireCrew.Name(driver)} ({driver.id}), driving {driver.GetSkillValue(DriverSkill):F0}, " +
                            $"to slot {warehouse.vehicleSlots.IndexOf(slot) + 1}; max destinations {max}");
        }

        // The plan, as BizMan's logistics screen builds it: a warehouse plan of the headquarters (LogisticsManagersPlanList
        // .AddPlan), the warehouse picked (LogisticsManagerPlanUI.OnChangedWarehouse), the manager picked
        // (ChangeLogisticsManager), a destination added and set to the commissary's entry of the mod's dropdown
        // (LogisticsCommissary.DestinationFor, as UpdateSelectedBusiness sets it), and the stock targets typed in.
        private static (string, string) FixturePlan(FixtureContext c)
        {
            if (!LogisticsCommissary.Ready)
                throw new StepFailed("commissary logistics is off (its patches aren't active; see the startup log), so a plan can't name a commissary");
            var warehouse = c.Warehouse!;
            var plans = SaveGameManager.Current.logisticsManagerPlans;
            var changes = new List<string>();
            var plan = plans.FirstOrDefault(p => !p.isFactory && p.targetAddress == warehouse.Address);
            if (plan == null)
            {
                plan = new LogisticsManagerPlan { headquartersAddress = c.Headquarters!.Address, isFactory = false };
                plans.Add(plan);
                plan.targetAddress = warehouse.Address;
                changes.Add($"new plan {plan.id} at {c.Headquarters.BusinessName} from {warehouse.BusinessName}");
            }
            if (plan.assignedEmployeeId != c.Manager!.id)
            {
                plan.assignedEmployeeId = c.Manager.id;
                changes.Add($"manager {HireCrew.Name(c.Manager)}");
            }
            var address = LogisticsCommissary.DestinationFor(c.Site);
            var index = plan.destinations.FindIndex(d => d != null && d.deliveryTargetAddress == address);
            if (index < 0)
            {
                var destination = new LogisticsManagerPlanDestination { isUiCollapsed = false };
                plan.destinations.Add(destination);
                destination.Reset();
                destination.deliveryTargetAddress = address;
                index = plan.destinations.Count - 1;
                changes.Add($"destination {index + 1} set to {c.Site.Id}");
                Log.Info($"Commissary logistics: destination {index + 1} of the plan from {warehouse.BusinessName} set to {c.Site.Id}.");
            }
            var targets = plan.destinations[index].stockTargets ??= new List<ItemAmountTarget>();
            foreach (var (item, amount) in c.Targets)
            {
                var target = targets.FirstOrDefault(t => t != null && t.itemName == item);
                if (target != null && target.targetAmount == amount) continue;
                if (target == null) targets.Add(new ItemAmountTarget(item, amount));
                else target.targetAmount = amount;
                changes.Add($"target {amount} {item}");
            }
            c.Plan = plan;
            var max = plan.MaxDestinations;
            if (index >= max)
                throw new StepFailed($"the commissary is destination {index + 1} and the plan delivers to its first {max}; remove a destination in BizMan or add a vehicle");
            return (changes.Count == 0 ? "already" : "done",
                (changes.Count == 0 ? $"plan {plan.id} delivers to {c.Site.Id}" : string.Join("; ", changes)) + $"; destination {index + 1} of {max}");
        }

        // ---- Helpers ----

        private static IEnumerable<BuildingRegistration> PlayerBusinesses(string type) =>
            SaveGameManager.Current.BuildingRegistrations.Where(r => r != null && r.RentedByPlayer && r.businessTypeName == type);

        private static bool Offers(Building building, string businessType) =>
            BuildingTypeHelper.GetData(building)?.availableBusinessTypes?.Contains(businessType) == true;

        private static Vector3 CommissaryDoor(CommissarySite site) =>
            World.EntrancesOf(site.Address)?.FirstOrDefault() ?? throw new StepFailed($"the city has no building at {site.Address} ({site.Id})");

        private static int Metres(Vector3 from, Building building)
        {
            var controller = InstanceBehavior<CityManager>.Instance.FindCityBuildingController(building.Address);
            return controller == null ? -1 : (int)Math.Round(Vector3.Distance(from, controller.transform.position));
        }

        // The rentable building nearest the point: on the market (AvailableForRent in its registration), not the player's,
        // no special service, not a commissary's building.
        private static Building? NearestRentable(Vector3 from, Func<Building, bool> fits)
        {
            var all = SaveGameManager.Current.BuildingRegistrations.Where(r => r?.Address != null)
                .GroupBy(r => (r.Address.streetName, r.Address.streetNumber)).ToDictionary(g => g.Key, g => g.First());
            var commissaries = new HashSet<string>((DemandData.Commissaries?.Sites ?? Enumerable.Empty<CommissarySite>()).Select(s => s.Address), StringComparer.Ordinal);
            Building? best = null;
            var bestDistance = float.MaxValue;
            foreach (var controller in InstanceBehavior<CityManager>.Instance.cityBuildingControllers ?? Array.Empty<CityBuildingController>())
            {
                var building = controller?.building;
                if (building?.Address == null || building.SpecialService != null || building.IsHamptonsHouse()) continue;
                if (commissaries.Contains(World.AddressKey(building.Address.streetName, building.Address.streetNumber))) continue;
                if (!all.TryGetValue((building.Address.streetName, building.Address.streetNumber), out var registration)) continue;
                if (!registration.AvailableForRent || registration.RentedByPlayer || !fits(building)) continue;
                var distance = Vector3.Distance(from, controller!.transform.position);
                if (distance < bestDistance)
                {
                    best = building;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // What BizMan's Start business sets for a warehouse or headquarters (StartBusinessUI.SetUpBusiness), without its
        // name checks and course check (the course is reported, not required) and without the happiness and goal events.
        // Returns a note on the course for the step's line.
        private static string StartBusiness(BuildingRegistration registration, string type, string name)
        {
            registration.BusinessName = name;
            registration.businessTypeName = type;
            // Headquarters: Monday to Friday 8 to 16, as Start business sets them (ResetScheduleDays does the same).
            // Warehouse: Monday to Saturday 8 to 16.
            registration.ResetScheduleDays();
            if (type == WarehouseType)
                foreach (var day in registration.scheduleDays) day.isOpen = (int)day.day != 7;
            registration.TemporarilyClose(false);
            var isWarehouse = registration.GetBuildingType() == WarehouseBuilding;
            if (!isWarehouse) BusinessHelper.UpdateCustomerCapacity(registration);
            registration.creationDay = SaveGameManager.Current.Day;
            BusinessHelper.GenerateMissingTodoTasksForBusiness(registration);
            GameEvent.Invoke("ba:gameevent_newbusiness");
            InstanceBehavior<CityManager>.Instance.FindCityBuildingController(registration.Address)?.UpdatePoi();
            InstanceBehavior<UIs>.Instance.mapFilters.ApplyFilters();
            try
            {
                var path = LogoHelper.GetPlayerBusinessLogoPath(name);
                if (isWarehouse)
                    BusinessLogoGenerator.Instance.GenerateWarehouseLogo(name, BusinessTypeHelper.GetData(registration), path, isPlayerBusiness: true);
                else
                {
                    registration.logoSettings = LogoHelper.GenerateLogoSetting(type);
                    BusinessLogoGenerator.Create(name, registration.logoSettings, path, isPlayerBusiness: true);
                }
            }
            catch (Exception ex) { Log.Error($"Test bridge: fixture logistics, the logo of {name} failed (cosmetic): {ex.Message}"); }
            GlobalEvents.onBuildingRegistrationChange?.Invoke(registration.Address);
            SaveGameManager.MarkChange();
            var course = BusinessTypeHelper.GetData(type).courseRequired;
            return course != 0 && !EducationHelper.HasCompletedDiploma(course) ? $"; the player lacks its course ({course}), which Start business would ask for" : "";
        }

        private static string UniqueName(string name)
        {
            var taken = new HashSet<string>(SaveGameManager.Current.BuildingRegistrations.Where(r => r?.BusinessName != null).Select(r => r.BusinessName), StringComparer.Ordinal);
            if (!taken.Contains(name)) return name;
            for (var n = 2; ; n++)
                if (!taken.Contains($"{name} {n}")) return $"{name} {n}";
        }

        private static bool IsPallet(ItemInstance instance) => instance?.ItemCached != null && instance.ItemCached.HasTag(ItemTags.iswarehousestorage);

        // Places for pallets in this warehouse: the pallets of the game's own layout for a building of this size (a
        // warehouse layout first), skipping places an item stands at already; then a row beside the delivery spot.
        private static List<(string Item, Vector3 Position, float Yaw)> PalletPlaces(Warehouse warehouse, out string source)
        {
            var building = warehouse.BuildingCached;
            var taken = warehouse.itemInstances.Values.Select(i => (Vector3)i.position).ToList();
            bool Free(Vector3 p) => taken.All(t => (t - p).sqrMagnitude > 0.75f * 0.75f);
            var palletNames = new HashSet<string>(ItemsGetter.GetAllItemNamesByTag(ItemTags.iswarehousestorage), StringComparer.Ordinal);
            var layouts = BusinessLayoutSetHelper.GetAllBusinessLayoutSets().Values
                .Where(s => s != null && s.BuildingSize == building.BuildingSize && s.BuildingVersion == building.BuildingVersion && s.Items != null)
                .OrderBy(s => s.BusinessType == WarehouseType ? 0 : 1).ThenBy(s => s.LayoutName, StringComparer.Ordinal);
            foreach (var layout in layouts)
            {
                var spots = layout.Items.Where(i => i != null && i.itemName != null && palletNames.Contains(i.itemName))
                    .Select(i => (i.itemName, (Vector3)i.position, ((Quaternion)i.rotation).eulerAngles.y)).Where(p => Free(p.Item2)).ToList();
                if (spots.Count == 0) continue;
                source = $"the places of the game's {layout.BusinessType} layout '{layout.LayoutName}' for this building size";
                return spots;
            }
            // No layout with pallets: a row beside the delivery spot, 2 m apart. Unchecked against the interior's walls.
            var pallet = palletNames.Select(n => ItemsGetter.GetByName(n, suppressError: true)).Where(i => i != null)
                .OrderByDescending(i => i!.cargoCapacity).ThenBy(i => i!.itemName, StringComparer.Ordinal).First()!;
            var root = InstanceBehavior<BuildingManager>.Instance.GetBuildingTransform(new BuildingSizeInfo(warehouse));
            var spot = root.GetComponentInChildren<DeliverySpot>(true);
            var origin = spot != null ? spot.transform : root;
            var row = new List<(string, Vector3, float)>();
            for (var i = 1; i <= 12; i++)
            {
                var p = origin.position + origin.right * (2f * i);
                if (Free(p)) row.Add((pallet.itemName, p, origin.eulerAngles.y));
            }
            source = $"a row beside the {(spot != null ? "delivery spot" : "building's origin")}, unchecked against the interior";
            return row;
        }

        private static VehicleInstance? VehicleOf(VehicleSlot slot) =>
            string.IsNullOrEmpty(slot.vehicleInstanceId) ? null : SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v != null && v.id == slot.vehicleInstanceId);

        // The game's vehicle type for warehouse deliveries that asks least of its driver: delivers to at least one
        // destination, fits a warehouse slot (not a hand vehicle or scooter), not a mod's.
        private static VehicleType? DeliveryVehicleType() =>
            VehicleTypeHelper.GetVehicleTypeNames().Where(n => !VehicleTypeHelper.IsModVehicleType(n) && FoodTrucksMod.KindOf(n) == null)
                .Select(VehicleTypeHelper.GetVehicleType)
                .Where(t => t != null && t.destinationsThatCanDeliver > 0 && Warehouse.CanVehicleOccupySlot(new VehicleInstance(t.vehicleTypeName)))
                .OrderBy(t => t.requiredDeliveryDriverSkillValue).ThenBy(t => t.price).FirstOrDefault();

        private static int MaxDestinations(FixtureContext c) =>
            c.Manager == null ? 0 : LogisticsManagerPlan.CalculateMaxDestinations(c.Warehouse!.Address, c.Manager.id);

        private static JObject BusinessJson(BuildingRegistration registration) => new JObject
        {
            ["name"] = registration.BusinessName,
            ["address"] = registration.Address?.ToString(),
            ["type"] = registration.businessTypeName,
        };

        // ---- logistics: status and run ----

        // { "op": "status" | "run" }
        private static JToken Logistics(JObject args)
        {
            var op = Need(args, "op", "status or run");
            if (op == "status") return new JObject { ["plans"] = PlansJson() };
            if (op != "run") throw new BridgeError($"Unknown logistics op '{op}': status, run.");
            RequireFtCity("logistics run");
            if (LogisticsManagerHelper.WarehouseDeliveriesWork.HasPendingWork || LogisticsManagerHelper.FactoryDeliveriesWork.HasPendingWork)
                throw new BridgeError("The game's own logistics run is still working through its deliveries; try again in a moment.");
            var before = PlansJson();
            var planned = SaveGameManager.Current.logisticsManagerPlans.Where(p => p != null && !p.isFactory).Sum(p => p.GetPlannedDeliveries().Count());
            // As the 02:00 hour does (GameManager.RunMainGameTick), then drained at once as DeletePlan does
            // (DistributedWork.ForceCompleteAllWork), so the deliveries and the game's end-of-run texts happen in this call.
            LogisticsManagerHelper.DoAllWarehouseDeliveries();
            LogisticsManagerHelper.WarehouseDeliveriesWork.ForceCompleteAllWork();
            var after = PlansJson();
            Log.Info($"Test bridge: logistics run now (the game's 02:00 warehouse deliveries), day {SaveGameManager.Current.Day} " +
                     $"{SaveGameManager.Current.Hour:D2}:{(int)SaveGameManager.Current.Minute:D2}: {planned} destinations planned.");
            return new JObject { ["deliveries"] = planned, ["before"] = before, ["after"] = after };
        }

        private static JArray PlansJson() =>
            new JArray((SaveGameManager.Current.logisticsManagerPlans ?? new List<LogisticsManagerPlan>()).Where(p => p != null && !p.isFactory).Select(PlanJson));

        // A plan: its warehouse with what its pallets hold of each item any destination targets, its manager, how many
        // destinations it delivers to, and each destination with its targets and, for a commissary, what it holds of them
        // (the mod's stock; null for a business).
        private static JObject PlanJson(LogisticsManagerPlan plan)
        {
            var hasWarehouse = plan.targetAddress != null && !plan.targetAddress.IsUndefined();
            var warehouse = hasWarehouse ? BuildingHelper.GetBuildingRegistration(plan.targetAddress) : null;
            var items = plan.destinations.Where(d => d?.stockTargets != null).SelectMany(d => d.stockTargets).Where(t => t != null)
                .Select(t => t.itemName).Distinct(StringComparer.Ordinal).ToList();
            var manager = string.IsNullOrEmpty(plan.assignedEmployeeId) ? null : EmployeeHelper.GetEmployeeById(plan.assignedEmployeeId, showError: false);
            var state = FoodTrucksSession.CurrentState;
            return new JObject
            {
                ["id"] = plan.id,
                ["headquarters"] = plan.headquartersAddress?.ToString(),
                ["warehouse"] = warehouse == null ? null : new JObject
                {
                    ["name"] = warehouse.BusinessName,
                    ["address"] = warehouse.Address?.ToString(),
                    ["stock"] = new JArray(items.Select(i => new JObject { ["item"] = i, ["amount"] = BuildingHelper.CountResourcesInPallets(warehouse.Address, i) })),
                },
                ["manager"] = manager == null ? null : new JObject { ["id"] = manager.id, ["name"] = HireCrew.Name(manager), ["skill"] = Math.Round(manager.GetSkillValue(LogisticsSkill), 1) },
                ["max_destinations"] = hasWarehouse ? plan.MaxDestinations : 0,
                ["destinations"] = new JArray(plan.destinations.Select((d, i) =>
                {
                    var site = LogisticsCommissary.SiteIdOf(d?.deliveryTargetAddress);
                    return new JObject
                    {
                        ["index"] = i + 1,
                        ["site"] = site,
                        ["address"] = d?.deliveryTargetAddress?.ToString(),
                        ["targets"] = new JArray((d?.stockTargets ?? new List<ItemAmountTarget>()).Where(t => t != null).Select(t => new JObject
                        {
                            ["item"] = t.itemName,
                            ["target"] = t.targetAmount,
                            ["held"] = site == null || state == null ? null : (JToken)CoreLogistics.Held(state.Commissary, site, t.itemName),
                        })),
                    };
                })),
            };
        }
    }
}
