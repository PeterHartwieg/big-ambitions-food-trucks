# Crew driving and fuel

Proposed on 21 October 2026. Source evaluation only; crew driving and automatic refuelling are not implemented or checked in game. The first development slice is the unattended native probe in [Crew driving spike](crew-driving-spike.md); traffic integration, continuity and fleet measurements follow separate gates.

Crewed trucks should travel through Persistent City's traffic, arrive when their journey actually finishes, and eventually stop for fuel. Food Trucks should retain ownership of crew assignments, schedules, selling, vehicle inventory and payments. Persistent City should own road routing, congestion and journey progress. The integration should remain optional so Food Trucks does not acquire a mandatory RLO dependency.

## Current foundations

| Area | Current implementation | Consequence |
| --- | --- | --- |
| Crew travel | `HireRoutes.Depart` saves the native pose, destroys the controller and sets parking to Undefined. `HireRunner.Step` requests arrival when `Transit.ArrivesAt` is reached. `HireRoutes.Arrive` places the vehicle at free parking or eventually back near its origin. | Physical journeys need an explicit backend and arrival acknowledgement. Existing hiding and recovery must not destroy a truck owned by that backend. |
| Travel estimates | `Travel.Between` uses straight-line distance, a road factor and a driving rate. Route editing, shift planning and the runner use it. | Road distance and conditional traffic estimates must eventually be available to all three, while actual arrival remains authoritative. |
| Persistent traffic | Persistent City's `TrafficKernel` manages directed sections, source waits, capacity, signals and physical progress. Resident journeys continue off-screen. | Reuse the same live kernel, rather than running an independent copy of congestion for trucks. |
| Persistent City ownership | `TrafficJourneys`, `PhysicalJourneys` and save validation bind each traffic entry to a travelling resident. `PhysicalVehicles` leases ambient Gley cars and excludes `VehicleController` objects. | An external participant needs a deliberate ownership and persistence extension. A fabricated resident or ambient replacement car is not an owned truck. |
| Owned vehicle motion | Driven Rides has `NwhCar.Wrap`, `NwhDrive`, steering/avoidance and local parking manoeuvres. `OwnCarDrive` and `Ride.Active` assume one player ride. | Reuse suitable lower-level algorithms; a fleet controller cannot call the player ride lifecycle unchanged. |
| Parking | Persistent City uses virtual curb handoffs. Food Trucks needs real parking, hatch access and commissary placement. | Road travel and local parking are separate work. Do not declare a selling stop reached at a nearby road node. |
| Fuel | `FoodTruck.asset` has `maxFuel: 80` and `taxDeductible: 1`; carts have zero capacity. Crew transit contains no fuel consumption. The game stores fuel on `VehicleInstance` and its live `CarController` fuel module. | Use the native tank. Crew consumption and station visits are new work. |

Persistent City source currently uses `save.Day * 1440 + hour * 60 + minute`; Food Trucks uses `(day - 1) * 1440 + hour * 60 + minute`, with an hourly-loop clamp. The contract must explicitly translate these clocks. Persistent City's processed simulation minute can lag the native clock after a skip; an estimate or stale snapshot cannot authorize backdated selling.

The inspected game sources are the local Big Ambitions 1.0 decompile dated 23 September 2026. Existing Persistent City and Driven Rides validation does not establish compatibility with unattended food trucks. No game or dependency source is copied into this repository.

## Intended route behavior

1. Before leaving, validate crew availability, a reachable truck route and a usable destination approach. Keep the existing protection for a vehicle the player is using or has just used.
2. Admit one owned truck to Persistent City's traffic with a stable journey identifier. Keep the native vehicle ID and its cargo, equipment, damage and tank.
3. Drive a physical body near the player. Elsewhere, retain the same route and traffic ownership in the background. Camera movement must not reset the route or erase a queue.
4. Complete road travel, then find and enter a suitable parking position. Open only after parking and normal pitch/hatch checks pass.
5. Traffic and parking delays shorten the scheduled selling window. A missed stop, blocked road or failed parking attempt produces an explicit decision and status, rather than a silent teleport. The return to the commissary remains part of the route and paid work.
6. Player takeover, sale, load/unload and dependency loss release control exactly once and preserve a recoverable owned vehicle. New journeys may use legacy simulated travel when the optional provider is absent. An already accepted physical journey needs explicit cancellation/recovery before any fallback.

The first release should have a bounded number of nearby physical trucks, with all remaining trucks travelling logically. Use hysteresis and safe placement checks at visibility transitions. Background progress remains traffic-constrained while a physical body cannot be shown. Hand carts require their own pedestrian pushing integration and are outside this truck work.

## Fuel and automatic station visits

The existing native tank remains authoritative. A live controller must synchronize its tank before being released; a newly shown controller must start from the saved native amount. Do not add a competing crew fuel balance.

During physical driving, native fuel consumption is the starting point. During background travel, account only for the background segment, using actual road progress and a calibrated model. The game has a bridge-distance fuel calculation that can inform an initial estimate, but it is not a measured city-driving model. Track a monotonic progress/accounting boundary so visibility changes, reloads and callbacks cannot consume the same segment twice. Preserve disabled-fuel settings. Calibrate under the reviewed RLO configuration, whose car-ownership feature can alter consumption. Any idle component needs explicit engine-on duration; a stopped clock or parked serving truck must not accidentally burn driving fuel.

Before a leg, estimate fuel for the journey and a reachable refuelling option with reserve. If necessary, insert a station visit, then resume the business route. Prefer the reachable station with the least practical detour, not the nearest station by straight-line distance. A fixed low-tank percentage can provide a warning but is insufficient for reachability.

Native `GasStationController` and refuel triggers provide candidate station geometry. Validate truck access, approach/exit and a service position; queue or reserve the position when several trucks arrive. The native overlay operates on the player's selected vehicle, so crew purchases need an explicit vehicle-targeted adapter rather than switching player selection. Read or validate the effective pricing behavior for the installed game/mod combination.

Charge for litres actually added, using the normal transaction path and the truck's tax treatment. Extend the per-vehicle ledger and profit display with fuel while reconciling the game's own expense totals to avoid a second deduction. Payment, tank update and saved service state need retry-safe completion. Insufficient funds cause a failed purchase and an actionable warning, with no free fuel or forced overdraft by default.

Automatic refuelling is the proposed default, with an optional explicit station stop later. If no station can be reached, stop safely and alert the player. Roadside delivery is later work. Serving-window generator fuel, repairs and wear simulation are separate features, not implicit requirements of driving fuel.

## Delivery sequence

| Stage | Outcome |
| --- | --- |
| Unattended native probe | Prove one actual owned truck can drive, stop and return control without affecting the player. No new journey persistence or traffic-provider API. |
| Traffic and continuity gates | Add explicit external ownership in Persistent City's shared queues, validate truck capacity, then prove off-screen continuity, coordinated checkpoints and fleet performance. |
| Crew route integration | Replace timer completion for provider-backed legs; add parking, schedule consequences, multiple trucks, player takeover and full lifecycle recovery. |
| Fuel service | Calibrated consumption in both motion modes, automatic station detours, service positions, payments, warnings and recovery. |

The spike must leave a place for cumulative distance and later service legs, but should not implement station selection or invent a general-purpose transport framework.

## Source map

- Food Trucks: `Core/Fleet/HireRunner.cs`, `Core/Fleet/HireSchedules.cs`, `Core/Saves/SaveState.cs`, `Core/Saves/SalesLedger.cs`, `unity/FoodTrucks/Scripts/HireRoutes.cs`, `HireCrew.cs`, `Payments.cs`, and `unity/FoodTrucks/Vehicles/FoodTruck.asset`.
- Sibling Persistent City checkout: `Core/TrafficKernel.cs`, `TrafficJourneys.cs`, `PhysicalJourneys.cs`, `TrafficNetwork.cs`, `Persistence/SaveCodec.cs`, `Runtime/PhysicalVehicles.cs`, `PhysicalPopulation.cs`, `RoadRoutes.cs`, and `PersistentCityMod.cs`.
- Sibling Driven Rides checkout: `unity/DrivenRides/Scripts/OwnCar.cs`, `NwhCar.cs`, `NwhDrive.cs`, parking adapters, and `Core/Drive` / `Core/Parking`.
- Local game methods: `CarController.Start`, `StartEngine`, `StopEngine`, `SavePosition`, `SetFuel`; `GasStationTrigger.IsColliderRelevant`; `GasStationOverlay.Refuel`; `SkipBridgeHelper.GetFuelConsumption`.
