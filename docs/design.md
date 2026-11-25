# Food Trucks: design decisions

Food Trucks is a public Workshop mod for Big Ambitions that adds street food as an early-game business. The player starts with a hand cart, moves up to a truck, and later runs a small crewed fleet supplied through commissaries. This file lists the decisions still in force and the game constraints behind them. Exact numbers live in the tuning tables: [rules](../data/rules.json), [economy](../data/economy.json), [demand](../data/demand.json), [pitches](../data/pitches.json) and [commissaries](../data/commissaries.json). The public contract for other mods is [vendor-api.md](vendor-api.md). In-game acceptance is in [CHECKS.md](../CHECKS.md) and bridge operations in [test-bridge.md](test-bridge.md). The [NYC research note](research/nyc-food-trucks-2026-10-07.md) is dated background, not a spec.

## Decisions

### Game boundary and saves

- Every native business system (customers, BizMan, staff, revenue, tax) hangs off a building's `BuildingRegistration`. Vehicles have none, so Core simulates street food and the game only shows the result.
- Core owns the street-food rules. Unity code stays in thin adapters. Native UI that needs a registration gets a transient stand-in, never added to the game's registrations or saved, and its patches do nothing unless that stand-in is showing.
- Per-save state is versioned JSON in `GameInstance.modData`, so it travels with the save.
- Save schema is 2. Schema 1 migrates unchanged: its takings pot held only unpaid crew sales, because player sales were paid on the spot then.
- A save or block written by a newer mod version loads read-only. Unknown data is kept as written and never overwritten.
- The truck logo is an optional, separately versioned field on the vehicle. A malformed, unknown or newer logo survives round-trips untouched, cannot be edited, and never invalidates financial state.
- Mod-only phone to-dos never reach a save file. They are removed while the game serializes and put back after, so a save opened without the mod has none.
- Removing the mod safely means running its uninstall step first. The first spike showed that a save held mid-interaction loses world interaction once the mod is gone.

### Licensing, food and the economics target

- The game's diploma names are a fixed enum, and the 1.0 business school has no door for its Food Safety course. The mod registers that course's data and offers it at the Market Demands door.
- One renewable vendor licence for the player, sold by phone from the City of New York. Each cart or truck needs its own permit. Permits are uncapped, and a vehicle's permit covers its crew.
- A fleet-only player hires through the native recruitment agency. Basic HR stays required. The agency lists "Street food fleet" for customer service campaigns, and candidates arrive unassigned and ready for a crew.
- Economics are tuned against the game's own early options at the same difficulty: delivery driving and a small starter shop. A hot dog cart at a good route sits between them. Good spots should pay well, and staying at one is fine. The old rule that staying must earn clearly less than moving was dropped. Rationale: [cart-economics.md](balance/cart-economics.md).
- Shops affect street food only through the game's per-neighbourhood market demand, read one way. Vehicles do not count as providers in the native market.
- The player's own vehicles compete by item, not category. Nearby vehicles add some footfall, and vehicles selling the same item split that item's customers.
- Area crowds add up independently on top of an ordinary-street baseline every pavement has. Offices and a nightclub next door both bring their own crowd.
- Rain comes from the game. Season comes from the mod's own calendar on the in-game day count, because the game has no climate seasons.

### Equipment and supply

- A cart has two general equipment places and a truck four. Each also has one dedicated place for the first drinks fridge. Extra fridges use general places, so older multi-fridge setups stay valid with their stock.
- Fitted equipment decides the menu and stock. Each piece holds a fixed number of weighted units of the foods it serves. A food with no fitted piece cannot be loaded.
- Extra Storage takes a general place, serves nothing and multiplies every piece's storage. A second one is refused. On a cart the trade-off between storage and a second menu item is intended.
- The game's `ba:itemname_water` is a factory ingredient, not bottled water, so it is out of the catalogue. Existing water cargo and commissary stock stay saved. Nothing sells, restocks or newly books it, and orders already booked keep their original delivery.
- Popcorn, cotton candy, slushies and kabobs are base-game items on native machines (cinema and theatre concessions, the industrial grill), bought at wholesalers like hot dogs.
- The four mod foods are dumplings, tacos, chopped cheese and grilled corn, with their own machines. They are game items sold only through commissaries. They are not furniture and have no native market demand.
- The Bakery mod is optional. Its items reach a vehicle only when it is installed.
- Item IDs, weights and equipment mappings live in [economy.json](../data/economy.json).

### Commissary

- A commissary's phone contact unlocks on the first visit. Spaces are rented, moved and given up through its phone call.
- Phone orders arrive at 08:00 the next morning and are paid on arrival. A delivery fee is charged once per commissary and morning, shared across every order delivered there that morning. Hauled stock and logistics deliveries pay no fee.
- Each food has one weekly box limit across all commissaries together, for the whole fleet. A growing fleet is meant to build supply through the player's own warehouse and a logistics plan, which delivers to a rented commissary with no fee.
- In any commissary bay, "Commissary stock" moves chosen items both ways. Returned goods keep what the vehicle paid, the day they first came on board and their commissary origin. Nested stock travels with its piece.
- Rent waives only the hourly public parking increments for that vehicle while it stands in the resolved bay of its own rented space. Street parking earlier that day stays due.
- The 04:00 overnight fine is separate from rent and also applies to vehicles with no space anywhere. That is the push to rent.
- A vehicle is cleaned only at the commissary where it rents. Standing in that bay at the 04:00 check counts as cleaned.
- The native contacts app ties a contact's address to its tenant's opening hours, which would block calls. Commissary contacts therefore stay addressless. The header shows the host building's address and a View on map button as display only ([#23](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/23)).
- With the map closed, View on map follows the native path. With the map already open, the mod calls `CityMap.FocusOnBuilding` so the card is selected too.
- Set destination targets the host building's entrance. The game's GPS target is never treated as the bay stall.

### Serving and money

- The player serves from a held cart stance or a truck hatch. Walking away stops service, and "Work the window" returns to the stance.
- Native fast-forward, the work skip and needs drain all run while serving. Core's sale clock still steps minute by minute, so a skip or fast-forward books the same sales as real time, never twice.
- Every window sale, player or crew, goes into one saved pot per vehicle and calendar day. It settles once in the game's midnight processing and is booked on the day it was sold. No sale changes the balance directly.
- Sales, stock cost, tax and the day summary are recorded once at sale time. Settlement never records revenue again.
- Held visible orders flush into the pot before save, closing, a skip or vehicle removal.
- A day that has not fully caught up stays held until it has, and then pays once. Failed or refused payouts stay saved for retry.
- A same-stop route edit can replan a short approach leg. An already open window keeps serving within the stop's original hours as long as the vehicle has not actually moved.
- A sold-out crew with a rented bay drives straight back once someone is on shift for the whole drive. Before this rule a truck could wait out several fined nights.

### Pitch and stance safety

- A truck pitch uses the game's parking-lane legality plus a reachable serving hatch.
- A cart needs safe pedestrian ground for both the worker and the customers, on separate sides of the counter. Parking lanes, road, crosswalks and traffic strips are rejected. Customers never stand in the road to rescue a server position.
- There is no end-of-cart service. An earlier end-based or drifting stance looked workable but put the worker in the parking lane on a kerb edge, so a cart with no safe counter side shows no line and keeps unsafe crew hidden.
- Worker, player and the first customer share the midpoint of the two fitted worktop pieces. With fewer than two, the counter centre is used. The below-counter fridge does not count.
- The register faces customers. Equipment controls and the cart fridge face the worker side.
- Two park premium pitches are curated: 23 Eleventh Street in Lower Manhattan and 5 Second Avenue in Hell's Kitchen. Each premium pitch has one active holder at a time.
- Industry City's park keeps its ordinary park demand. A premium anchor there is deferred until a legal truck placement passes the hatch rule ([park study](balance/park-premium-pitches.md)).

### Schedules and employee UI

- Roster size and simultaneous staffing are separate. A roster has no size limit. A cart has one serving row and a truck two.
- One employee belongs to one vehicle at a time.
- Each crew member's shifts on any row add up against the per-day cap in [rules.json](../data/rules.json). Shifts end at 24:00, so night work counts on each calendar day it touches. There is no rolling limit.
- The native Schedule tab caps new shifts but not handle drags. The mod validates every edit before writing to Core, restores the accepted shifts on refusal, and redraws the rows on the next frame because the native callback still holds the edited objects ([#33](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/33)).
- Native day paste copies opening hours along with shifts. Before the game recalculates opening state, the mod restores the route's hours on the stand-in, including with no crew or an empty source day. Without that guard the Schedule tab is not offered ([#25](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/25)).
- Crew stay unassigned to any building, so native schedules, stations and sick days leave them alone. The employee list shows the vehicle name and a "Street food crew" task, counts crew as assigned, and routes Manage schedule to the vehicle page. All of this is display only ([#21](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/21)).
- The native business dropdown maps option i to building i - 1, so the vehicle is option 0 and every building keeps its index. Picking a building moves the employee there and off the crew, as in the base game.

### Sale and removal

- A closed, released cart in any commissary bay can be sold. The quote is the game's resale value for the cart plus its paid equipment and stock, nested stock included. This deliberately bypasses the native zero price for hand vehicles.
- Confirmation rechecks the save, ownership, bay, closed state and quote. It settles that cart's takings and earned wages, then ends its permit, route, rental, pitch holds and venue partnership.
- Shared commissary stock and orders, the licence, booked event capacity and past accounts stay. Crew stay employed and unassigned.
- A sale payment that fails is kept in the save and retried after the cart is gone. No payout happens while the cart is still owned.
- Uninstall refunds purchase price plus eligible paid cargo. It reads both the native street vehicle list and the private driver's stored list, by vehicle ID. Native `VehicleHelper.Delete` only clears the street list ([#24](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/24)).
- If any copy of a vehicle, an unpaid taking or another required cleanup remains, uninstall withholds both the success notice and the refund for that ID, so a retry cannot pay twice.

### Marketing

- Marketing is bought per vehicle on its BizMan page: none, flyers, local ads or citywide ads. Prices and footfall live in [rules.json](../data/rules.json) under `marketing`.
- A pick takes effect the next day. Picking charges nothing, and today keeps the level it already paid for.
- The fee is charged once at midnight through the game's bank, as a tax-deductible marketing expense. If the player cannot pay, the booking turns off and a notice says so. Skipped days are neither charged nor marketed.
- Econoview has an itemized Marketing line. The fleet page's average daily income includes the fee.
- Price rationale (modeled at 20 passers an hour, shipped lower): [marketing.md](balance/marketing.md). Shipped in 0.10.12 ([#36](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/36)).

### Events, catering and legacy locations

- City events and catering use real building addresses in the game's own format. Street IDs are not locale keys, so the mod resolves addresses through the native address helper, never `Localizor`.
- Fully booked city events still offer Set destination through the native guider.
- When addresses are ready, invalid events are cancelled. Only unused booked places are refunded, exactly once. Invalid booked or started catering is removed without reputation, money or stock changes.
- Address validation, refunds and expiry wait together until city data is ready. An unresolved address is unknown, never proof of a mismatch.
- Stored phone text is repaired only for positively identified city messages whose event has not ended. Finished history stays as it was.
- Address identity compares whole street aliases mapped to the game's street IDs, plus house numbers. Alias collisions and spelling-only differences stay unknown, and unknown keeps existing booking guidance verbatim.
- An offer or booking becomes "no longer active" only on proof of inactivity. That changes the guidance text and nothing else, so the radius and metadata stay as written.
- Each reconciliation pass uses one snapshot of street names and alias availability. Writes compare before writing and respect read-only state. Immediate and deferred paths apply the same hold rule.
- A cross-minute name cache is deferred until there is a reliable invalidation signal and a measured need.

### Native UI constraints

- The phone app list is a fixed enum, so street food lives in BizMan. A vehicle page uses copied native parts, and the Schedule tab runs on the in-memory stand-in described under game boundary.
- The fleet overview's first job is showing which vehicle needs attention. Warnings are real phone to-dos, decided in Core, as the game does for shops.
- UI reuses native controls at native scale and style.
- The serving stock panel can outlive `GameManager` during unload or quit. Teardown skips the native `ItemPanelUI.Toggle(false)` then, but still books held orders and releases listeners, work energy, game speed and the time machine ([#26](https://github.com/PeterHartwieg/big-ambitions-food-trucks/pull/26)).

### Physical crew travel

- Shipped crews travel offscreen on a timer: straight-line distance times a road factor, then placement at a free parking spot on arrival.
- The physical driving probe is development-only. It compiles only with `FOODTRUCKS_CREW_DRIVING_DEV`, runs only in an FT test save with the `-foodtrucks-test` bridge and its ownership lock, and the release build checks that it is excluded.
- Production may adopt a physical backend only after these gates: shared Persistent City routing and traffic priority, save/load continuity, and measured fleet cost and route economics ([route economics study](balance/crew-route-economics-2026-10-21.md)).
- Persistent City would own road admission and journey progress. Food Trucks keeps vehicle, crew, stock and money state, and the integration stays optional.
- The two mods count minutes differently, so day/minute origins are translated explicitly. Arrival comes from actual progress and parking admission, never from an estimate.
- The native tank and damage stay authoritative. Only offscreen segments would get a separate fuel model, settled exactly once. Automatic refuel detours wait until reachable refuels are tested.
- A live physical journey hands back cleanly before any timer fallback.
- Crew parking, the routing rollout, cart pushing and refuelling each need separate approval.

### Probe safety and provenance

- Ownership tracks the exact native controller, session and generation. Stale tokens cannot write.
- A refusal before engine acquisition makes no native writes.
- After acquisition, interruption cleanup is synchronous. Pose, fuel and damage are preserved before release.
- A stopped truck that still occupies a junction keeps a reported record until it is actually clear.
- Global collision layers and masks are never changed, and the parking-gap proxy stays disabled.
- Admission requires the cooked convex body to contain each wheel prism, exact NWH wheel references, a common traffic-trigger height band and a native wheel-step guard.
- The low-speed straight envelope includes body, tilt, tracking and heading. Extra side padding stopped the truck near fixed pavement props and was dropped.
- Completion counts only after real physical braking. A forced interruption hold is reported as a separate result.
- The bounded pure pursuit, projection and braking equations were adapted from the MIT-licensed Driven Rides repository, revision `90981d0061418a4d4742792d90c62c6a686f2a80`: `Core/Drive/PursuitDriver.cs`, `Core/Drive/DriveLine.cs` and `Core/Drive/CarModel.cs`. The same copyright and permission notice (Peter Hartwieg) is in [LICENSE.txt](../LICENSE.txt). File hashes and the exact equations reused are in the [historical provenance record](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/e97400846d190da299239e129fbcef50c3f4d6ef/docs/crew-driving-provenance.md).

## Game integration constraints

Observed against the inspected Big Ambitions 1.0 assemblies of 23 September 2026, unless a newer check is noted.

- The official mod API is thin: registration, options, asset bundles, load events and a logger. Everything else calls public game classes or uses Harmony, which the mod ships in `Dependencies/`.
- `BuildingRegistration` underlies BizMan tabs, schedules, employees, alerts and income. Normal BizMan tabs assume a building.
- Income tax comes from rented buildings' sales history, not from transactions. Patches on `TaxCalculationHelper` add street sales at the game's own rate.
- `GameManager.NewDay` runs payroll, parking and `BusinessHelper.RunDaily` before taxes, the daily UI and the midnight bank balance. Street-food settlement runs in the `RunDaily` prefix, inside that order.
- Diploma names and phone apps are fixed enums. Education screens throw on unknown diploma values.
- Market demand per product and neighbourhood is readable through `ProductMarketHelper.GetNeighborhoodDemand`.
- Opening hours of all venues are readable. Hourly customer counts exist only for the player's own businesses. Cinemas and theatres have no show times, so surges use fixed times.
- Customers exist only inside buildings and there is no outdoor queue to reuse. The mod spawns its own pedestrians on the navmesh. Street people exist only within about 100 m of the player, so queues are visible near the player and statistical elsewhere.
- Native hand vehicles have zero resale price.
- The private driver's garage moves a vehicle from `VehicleInstances` to the separately saved `privateDriverVehicleInstances` list. Native delete only clears the first list.
- `ItemPanelUI.Toggle(false)` dereferences the `GameManager` singleton unchecked, and that singleton can be gone during unload or quit.
- `ContactsApp.SetUpHeader` hides the address line and View on map for addressless contacts. Giving a contact an address ties its call button to the tenant's opening hours. With the map open, `buildingToFocus` only moves the camera.
- Native map search indexes only buildings with business names, so address GPS cannot rely on it.
- `GuidersManager.SetGuiderTarget(Address, Destination)` persists the custom destination itself.
- `ScheduleDayButton` paste copies opening hours with the shifts. The Schedule tab caps new shifts at 12 hours but not handle drags.
- `MyEmployees` reads business and task from a building only, and its business dropdown maps option i to building i - 1.
- Release compatibility from 0.10.6: the dedicated-cooler runtime and its tuning (reserved cooler place, soda stock weight) ship together. Combining the newer cooler code with older tuning failed eight Core tests during the 0.10.5 reconciliation, so neither half ships alone.
