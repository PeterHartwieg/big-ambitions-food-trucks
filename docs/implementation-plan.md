# Food truck mod: implementation plan

Turns [design.md](design.md) into ordered work. Written 7 October 2026. The structure follows advice from a Fable 5.1 review of the design: build one playable slice first, then add features in milestones that each end in an in-game check.

## Start here

1. Read this plan, then [design.md](design.md). The design's Technical answers table has the code facts, with class names to search for. The NYC research note in `research/` is background only.
2. Machine setup (paths to the decompile, the SDK clone, the game PC and the test save) is in `CLAUDE.local.md`, which is not committed.
3. Milestone 0 results go into the README's evidence section, marked checked, pending or unverified.

**Status, 8 October 2026:** Milestone 5 (hires) is built as 0.6.0 on branch `milestone-5`, on top of Milestone 4 (0.5.0) and Milestone 3 (0.4.0); none of the three is checked in game yet (`CHECKS.md`). Milestone 0 is done and played (README evidence table; session report in `evidence/milestone-0-session-1.md`). Milestone 1 was played on 8 October (README; session report in `evidence/milestone-1-session-1.md`). Selling, saves, the truck fixes and the uninstall step work. Carried into Milestone 2: the cart's grip (move the handle to the vanilla grip height and reach), a unit limit for the cart's stock, whether street food belongs in the game's day summary, the drive-by question (a workstation at the window?), and the unchecked items: hover outline, delivery refusal on a save with a warehouse, and the forced tax payment without the mod. Remaining debug keys are listed in the README; gate or remove them before the Workshop release.

## Ground rules

- **Version 1 keeps the full scope** Peter chose, built in the milestone order below. If a feature fights back, it moves to 1.1 following the [cut list](#cut-list). No milestone waits on another track's polish.
- **Peter's time in the game is the critical path.** Agents work unattended between checks. Every milestone ends in one in-game session of about 15 minutes with a written checklist.
- **The simulation is the truth, views are derived.** Core decides sales, queues and money. Pedestrians, the window and the map only show it. Switching between visible and statistical mode changes nothing in Core.
- **Placeholder art until Milestone 8.** Box models with correct size, pivots and wheel hubs. The 3D art pipeline runs as its own track and blocks nothing.
- **Evidence in writing.** Each spike answer goes into the mod README's evidence section, marked checked, pending or unverified.

## Layout and build

```
big-ambitions-food-trucks/
  Core/        pure C#: no Unity, no game types; netstandard2.1
  Game/        adapters, Harmony patches, the one runtime MonoBehaviour
  data/        demand.json, economy.json, zones.json, Locales/en.json
  tests/       Core tests, run with dotnet on the Mac
  unity/       the SDK mod folder: vehicle and prop prefabs, asmdef, manifest
  tools/       build, install and package scripts
```

- **Core** builds and tests on the Mac with the installed .NET (`dotnet test`). Agents can work on it without Unity, Windows or the game.
- **Mod build** uses the official SDK on the Mac: Unity 2022.3.62f2 is installed, and the SDK's mod builder packages the DLL and asset bundles for Windows. It needs the **Windows Build Support (Mono)** module added to that Unity version first. The build should run in Unity batch mode (`-batchmode -executeMethod`), so agents can rebuild without Peter.
- **Harmony 2.4.2**, checksum-pinned, in the package's `Dependencies\` folder. Each patch logs an "Active" or "Not active" line on startup.
- **Install** copies the package to the game's local mods folder on the Windows game PC over ssh (PowerShell `-File` scripts).

## Core modules

All pure and unit-tested. Inputs are plain data, outputs are decisions.

| Module | Job | Key tests |
|---|---|---|
| `DemandTables` | Load and validate the data files | bad files fail with a clear message |
| `AreaClassifier` | Nearby buildings and businesses to an area mix; hand-drawn zones on top | office block → office; zone overrides |
| `DemandModel` | Customers per hour for a spot, menu, clock, conditions and reputation | office lunch about 3x; winter ice cream 0.2; theatre surge at 22:30 |
| `SalesSimulator` | Per-minute tick: service rate, queue growth and abandonment, stock draw, sell-out | deterministic with a seed; sell-out stops sales |
| `PriceCurve` | Own price-to-conversion curve, calibrated once against the shop curve in the decompile | higher price, fewer buyers |
| `Economy` | Fees, wages, fuel, prices as multiples of the reference shop | `ReferenceShopIncome(difficulty)`; cart ≈ half a small shop at a good lunch pitch |
| `Reputation` | Health points and grade, regulars per spot and weekday, competition between vehicles | grade thresholds; regulars build and decay |
| `Fleet` | Vehicles, equipment, licence and permits, crews, hire schedules, travel timing | a vehicle without a permit can't open; travel time per route |
| `Catering` | Offer generation and gig evaluation | late arrival fails the gig |
| `SaveModel` | Versioned JSON and migrations | old versions load |

## Game adapters

Thin and checked in the game, not unit-tested: `WorldReader` (buildings, neighbourhoods, market demand, rain, time, promotion variable), `VehicleAdapter` (cargo, position, parking legality, hand-vehicle push), `MoneyAdapter` (`ChangeMoneySafe`, transaction labels), `ModDataStore`, `WindowInteraction` (copy of the seller stand pattern), `PedestrianQueueView`, `ContactsAdapter` (catering messages), `MapFilterPatch`, `TaxPatch`, and `FoodTruckRuntime`, the one `MonoBehaviour` that ticks Core every in-game minute.

Area classification is cached per parked position: computed on Open and at the start of each day, never every minute.

## Milestone 0: tooling and spikes

Throwaway builds, each answering one question in writing. The order is by risk; the code-only spikes (3, 4, 6) run in parallel with 1 and 2.

| # | Spike | Pass | If it fails |
|---|---|---|---|
| 1 | SDK build: copy `Example-Vehicle` under a new name, build for Windows in batch mode on the Mac, install on the game PC, the vehicle appears in game | builds without clicks; vehicle buyable | Peter clicks Build once per rebuild; plan around fewer builds |
| 2 | Modded hand vehicle: a box cart using the game's hand truck pattern | player can grab and push it | cart becomes a slow towed trailer or waits for 1.1; the slice uses the truck |
| 3 | `modData`: write versioned JSON, save, quit, load, read back; load a save made without the mod; remove the mod and load | data survives; both load without errors | own file per save, keyed by save name |
| 4 | Harmony on 1.0: the tax patch adds a test amount to taxable income | amount appears in the tax statement | the mod sends its own tax bill each period |
| 5 | One spawned character walks to a point on the pavement and stands there | works near the player | the queue becomes a counter on the window |
| 6 | `ChangeMoneySafe` with a mod transaction type and locale label | shows in the transaction history with the label | use a game transaction type |
| 7 | Uninstall: a save with a mod vehicle loads after the mod is removed | loads, vehicle gone or plain | document an uninstall step (sell vehicles first) |

**Peter:** add the Windows Build Support module in Unity Hub if the agent can't install it from the command line, and run one in-game session with the spike builds.

## Milestone 1: the ugly cart

Carried over from the Milestone 0 reviews (7 October 2026):

- A save that holds a mod vehicle and has an overdue tax bill throws in the game's forced payment once the mod is removed (it reads every vehicle's type). Document a safe uninstall: sell or store mod vehicles first.
- Mod vehicles need a dealer with a spawn point, or the mod's own purchase flow; the spike lists them at every dealer.
- From the in-game session: clear the vehicles' sleep setup (Sleep in the item panel throws), make truck storage openable, fix the cart's grip and its sideways sliding, and give the cart a hover highlight.
- The debug tax statement action should refuse while a bill is unpaid; the session's save count can run one ahead if serialization ever fails.

The first playable slice. It touches every risky layer at once: bundle, vehicle, cargo, money, saves.

- A box cart, bought from the vehicle dealer, pushed by the player.
- Load stock the way the game loads a car: carry a box out of a wholesaler and click the cart.
- "Open" at any spot. Sales come from a constant per hour, draw stock and pay money with the mod's label.
- State in `modData`: licence placeholder, open or closed, lifetime sales.
- One notification line per sale; no queue, no people.

## Milestone 2: demand and menu

- Data files with the first draft of every table in the design's Demand section.
- `WorldReader` feeds the real world into `AreaClassifier` and `DemandModel`: buildings, neighbourhood, daypart, day, rain, the mod's own season calendar, the game's market demand for shop saturation.
- Equipment as inventory; the menu follows the fitted equipment.
- Legality: carts use the mod's pavement rule, trucks the game's parking lanes (once trucks exist). The Open button names the failed rule.
- Own price curve.

**Balance:** set by calculation, not by play (Peter, 8 October 2026: an early build can't be judged by feel). `docs/balance/cart-economics.md` compares the cart with a delivery-driving day and a small vanilla shop; the cart must sit between them as a stepping stone. Good spots are rewarded; moving is not forced (8 October 2026). Its tuning goes into `data/` and the pricing rule.

**Second build, 0.3.0 (8 October 2026, not yet checked in game):** the study's data patch; stock weight per product and per-area appetite in Core; subway stations reported to the classifier as `subwayentrance` (the `transit` area); a zone-point debug key for drawing parks; `CartPricing` (your price, else the district's pricing manager via the game's formula, else the standard price) with a Prices dialog built from the game's list picker and text prompt; the ledger records the stock's wholesale cost, and the day's street food profit is added to the game's financial summary (day summary, history) and shown as a "Food carts" row in Econoview. Park zones themselves wait for the marked points.

**0.3.1 (8 October 2026, not yet checked in game):** additive crowds after the 0.3.0 session showed a spot next to offices and a club as near-empty at lunch. Each area near a spot has a presence (saturating with its votes) and adds its own crowd on top of a street baseline; who buys follows who is there at that hour. Recalibrated in `docs/balance/cart-economics.md` section 6; good spots are rewarded and moving is no longer forced.

**Pricing (decided 8 October 2026):** district prices by default, as a pricing manager sets them, plus custom prices per product; the cart shows up like a shop in the game's screens where that's feasible. Research done the same day [code, unverified in game]: a fake building registration is ruled out (the daily summary looks up every rented business's building and throws without one; rent, fees, market prices and rivals would also run on it). Planned instead: the pricing manager's own formula (`PricingManagerHelper.ComputeSuggestion`) when one covers the cart's district, otherwise the default price; custom prices per product, set through the game's own list picker and text prompt (a full panel styled on BizMan's price rows can come later), stored in the save; a postfix on `FinancialSummaryHelper.CreateFinancialSummary` adding the cart's profit to the day's total (day summary, weekly history, rivals, Persona); an Econoview "Food carts" row. A BizMan list row is possible but brittle; the pricing manager's own screen isn't worth a fake building.

**Game side, built 8 October 2026** (not yet checked in game): the data files ship in the mod's `data/` folder and load at startup (`DemandData`); `World` reads the buildings, businesses and neighbourhood around a cart from the city's door positions (on Open and each new day), plus time, rain, market demand and difficulty; `Legality` refuses parking lanes, crosswalks, building entrances and subway stations within 3 m (the game has no hydrant, driveway or pavement data; whether the road layer can tell pavement from road is only logged); `CargoLimit` hooks the game's two cargo methods so food stops at the unit limit and equipment at the slot count, the rest staying in the box; the fitted equipment (vanilla items in the cart's cargo, mapped in `economy.json`) makes the menu; `CartDemand` turns the model's customers, the price curve and the window's service rate into sales per hour; each customer buys a main item by category share and sometimes a cold drink. Ctrl+F9 shows the reading for the cart next to you. The cart's handle moved to the vanilla grip (1.16 m high, 0.65 m reach).

**Core half, built 8 October 2026 on branch `milestone-2-core`** (unit-tested, nothing wired into the game yet): `data/demand.json`, `data/economy.json` and an empty `data/zones.json`; `DemandTables`, `AreaClassifier`, `DemandModel`, `PriceCurve`, `ServiceRate`, `Menu.FromEquipment` and `SalesRate`. `CartMenu` and `CartTuning.SalesPerHour` still drive Milestone 1. Decisions made while building it:

- A seventh daypart, night (02 to 05), so every minute has a value. Before 05:00 the day-of-week factors of the previous day apply: 01:00 on Saturday is Friday night.
- The market factor acts per category, not on the whole menu: a burger truck among burger bars loses only its burger customers. Its ceiling is 1.0, because the game caps demand at 100 [code].
- Cold drinks bring a quarter of their appetite as customers of their own (`newCustomerWeight` 0.25). The rest becomes add-on drinks on other orders, about 0.2 per customer at lunch.
- Summer ice cream appetite is 1.3 (the design gave 1.3 and 1.4 in two places); summer cold drinks 1.4.
- The mod's calendar: 7 in-game days per month, day 1 is 1 April. Both are in `demand.json`.
- The price curve smooths the game's shop rule [code, unverified in game]. A customer buys up to max(class index, neighbourhood average index) times the reference price, with class indices 1.2, 1.4 and 1.7 and the average weighted by the neighbourhood's class mix (`CitizenHelper.MaxAcceptableRelativePrice`). The reference price is the lower of the item's default price and the cheapest shop in the neighbourhood (`ItemHelper.GetMarketReferencePrice`). So nobody in the game refuses a price up to the neighbourhood average, which is never below 1.2. The class mix is asset data; the curve assumes an average index of 1.35 (a 50/35/15 mix) and keeps every buyer up to it, and `PriceCurve.NeighbourhoodRatio` moves other neighbourhoods onto it once the adapter reads their index: prices up to the reference are unchanged, the neighbourhood's index lands on 1.35, and the curve still ends at 1.9 everywhere. Pricing at 0.8 of the reference brings 10% more buyers; the game has no such bonus.
- Equipment IDs are the mod's own (`hotdoggrill`, `burgerstation`, ...). Which item the player buys for each is a game-side choice.
- The theatre surge test moves to Milestone 6 with the rest of the live venue crowds.
- Until queues exist, the sales rate is customers × price conversion, capped by the window's service rate; customers above the cap are lost.
- `SalesRate` gives customers per hour, one main item each. The game-side wiring (or Milestone 3's `SalesSimulator`) still has to sell `AddOnsPerCustomer` extra drinks and pick each customer's item by `CustomerShares`; `Menu.PickNext` still picks the item with the most stock.
- A neighbourhood's season factor stacks with the month's footfall: the Hamptons' winter 0.5 times the winter months' 0.6 gives the design's 0.3.
- Each vehicle has a stock limit in units (`stockUnits` in `economy.json`, `StockLimit` in Core): cart 80, truck 160. The game counts cargo in boxes, and one wholesale box of hot dogs holds 500 units (Milestone 1 session), so without the limit a cart never sells out. The game side enforces it when loading (`StockLimit.CanLoad`, counting all items in the vehicle) and decides what happens to the rest of a box, since the game's cargo holds whole boxes. A cart loaded under Milestone 1 keeps what it has until it sells it.
- The game's hot dog sells at $11.65 (`Item.DefaultMarketPrice`, seen in the Milestone 1 session). The price curve works in ratios to the reference price, so it needs no money figure; the Economy module's targets will use this as their reference point.
- Bakery items take 3 minutes to serve, so a display-case cart serves 20 an hour, the top of the design's range. A cooler-only cart serves 60, but drink-only demand is small.

## Milestone 3: serving and queues

- Service speed by customer service skill × satisfaction, and a queue limit per vehicle (design: Staff skill and queues). The player's own skill counts while they work the window.

- The window interaction copied from the seller stand: the player serves the head of the queue.
- Visible queue within about 100 m; statistical sales elsewhere, from the same Core tick.
- Abandonment after about 20 minutes, sooner in rain.

**Core half, built 8 October 2026 on branch `core-m3-m7`** (unit-tested, nothing wired into the game yet): `SalesSimulator` in `Core/Selling`, its state (`ServiceState`) on each vehicle in the save, and a new data file, `data/rules.json`, for everything from here on, so balance passes on `demand.json` and `economy.json` never touch it. `SalesTick` and `SalesRate` still work as before; the game side switches when it builds the window. Decisions:

- One tick, in whole minutes, for the visible queue and for sales away from the player. Customers arrive at random (Poisson) at the demand model's rate times the price conversion, wait in a first-come queue, and leave after their patience: 20 minutes, ±25% per customer, times 0.6 while it rains. Rain shortens the patience of people already waiting. Nobody joins a queue of 12.
- Each customer picks a category by the demand model's `CustomerShares` and gets that category's item with the most units left. A customer whose category has sold out walks on; the demand doesn't turn into drink sales. Add-on drinks ride with customers who came for food.
- `Servers` workers each make one order at a time, at the item's service time. When the player works the window, the game side either counts them as a server (automatic selling, as now) or calls `ServeHead` for each serve action. A hire who goes home puts their half-made order back at the head of the queue.
- Each sale takes one unit; the result lists the units to remove. Selling out sends the queue away and reports `SoldOut`, so the window closes.
- Deterministic: the random state lives in the save, so a save and load replay the same customers. Customer IDs let the visible queue spawn and remove the right people. Mono and .NET may round `Math.Exp` differently, which can change a draw but never what the save holds.
- The save schema stays at 1: every new field is optional and old saves load with defaults.

**Game side, built 8 October 2026 (0.4.0, branch `milestone-3`, not yet checked in game):**

- `CartSelling` runs `SalesSimulator` every in-game minute instead of `SalesTick`. The queue (`VehicleState.Service`) is in the save while the cart is open. The plan's best-accepted item per category (`CategoryItems`) and its drink (`AddOnItem`) are the only items Core may sell, so a price customers refuse never sells. The player within 8 m of the cart is one server; further away nobody serves, so the queue waits and thins out (the message says so in-world). Time skips send the queue away. The serve action per customer (the seller stand pattern) is not built: serving is automatic while the player stands at the window.
- Staff skill: `ServiceRules` in `rules.json`. Speed is linear from 1/1.5 at skill 0 through 1.0 at 50 to 1.3 at 100. The player's skill is `charactersData[0]`'s `ba:skill_customerservice` (50 by default) [code, unverified in game]; the player has no satisfaction, so their skill counts in full. Hires will use skill × satisfaction / 100, the game's own measure. Mood (complaining below 25, pleased from 75) only shows the game's good or bad service emoji on served customers for now; Milestone 6 can feed it into reputation.
- Queue limits per vehicle in `rules.json` (cart 6, truck 10), within the global 12.
- `CartQueueView`: within 100 m of the player, outside, and not during time skips, each open cart gets a line of up to 10 places beside it on the NavMesh (window, 6 waiting, 3 for served customers still walking up), on the side and in the direction with the most walkable places out of parking lanes. People walk in from points 9 to 19 m along the pavement, stand at the window about 4.5 s in all (2.5 s with a backlog, two paying at a time; at least 1.5 s of paying) and walk off. While the line is on screen, each sale is paid when its customer starts paying at the window; the units count as sold for Core at once, and anything held is paid before saving, closing, time skips and when the line leaves the screen; people who give up show the game's angry emoji. At most 16 people at once. They are removed on close and grab (they walk off), beyond 110 m, indoors, on time skips, scene changes, session detach and the uninstall step. The game's character prefab and NavMesh agent are the spike 5 pattern; walking speed, emojis and the layout are unverified in game.
- Balance: within 3% of the fluid rule over a day (`docs/balance/cart-economics.md` section 7); the calculator now mirrors the queue.

## Milestone 4: truck, licence and city systems

- Truck vehicle with cargo, driven like any car.
- Vendor licence requires the game's Food Safety course. A permit per vehicle covers its crew. (A fleet cap was planned and dropped, see the design answers below.)
- Cart crossing a bridge uses the game's bridge skip with walking time. (Dropped: the cart takes the subway, see below.)
- `TaxPatch` live; truck sales in the tax statement.
- "Street food" category on the city map: offices, factories, nightlife, theatres and cinemas, premium spots.

**Core half, built 8 October 2026 on branch `core-m3-m7`:** `Licensing` and `FleetRules` in `Core/Fleet`. Decisions:

- The licence needs the Food Safety course, lasts 60 days and can be renewed in its last 10; a renewal adds a full period, so renewing early loses nothing. The game's diploma never expires [code, unverified in game]; the mod's licence does.
- Superseded before the game side: the permit cap (one, plus unlocks at 14, 35 and 70 days or 400, 1,500 and 4,000 items sold, up to 4) was removed from Core.
- To open, a vehicle needs the player's valid licence, its own permit and somebody at the window. Hires need neither the course nor a licence of their own.
- Superseded before the game side: `Licensing.GrandfatherOldVehicles` (a free permit for vehicles from saves before Milestone 4) was removed, since the mod is unreleased. The game side sets each vehicle's type when it first sees it.

**Design answers, 8 October 2026 (Peter, before the game side):**

- **Licence and permits come from the city by phone.** A call dialog on the game's City of New York contact (the one that sends parking tickets) sells and renews the licence and sells a permit for a vehicle. The game has no city hall. A vehicle that can't open says what's missing in-world.
- **No fleet cap.** Every vehicle still needs its own permit, but there is no limit on how many; the cap and its unlocks stay out unless a reason turns up later.
- **No old-save handling.** The mod is unreleased, so saves from 0.4.0 and earlier get no grandfathering. Schemas stay versioned as before.
- **Bridges need no work.** A cart crosses to another island by subway. The code reading says `SubwayStation` refuses entry inside a vehicle, so check it in game with the cart.
- **Parking always costs.** Trucks pay the game's street parking and draw its tickets like any car, open or not.
- **Truck price and balance wait** until near the end of development; the truck gets a placeholder price.
- **The final truck model goes in now** (`art/truck/FoodTruck.fbx`), so the window, queue and parking are tuned on its real shape.
- **The map's "Street food" category** shows venue buildings (offices, factories, nightlife, theatres and cinemas) and a marker for each of the player's carts and trucks (trucks have the game's own).

**Game side, built 8 October 2026 (0.5.0, branch `milestone-4`, not yet checked in game):**

- **Truck.** `foodtrucks:vehicletype_foodtruck` replaces the test truck. `TruckAssets` builds it from the SDK's example car with the final model from `art/truck` (body, wheels, collision hull, a serving hatch on the right), the art's positions for wheels, doors, fuel cap and loading point, and first-guess physics for a step van: 3,500 kg, springs, dampers and engine scaled by 2.5 from the 1,400 kg car, centre of mass at 0.6 m. 16 cargo slots, 80 l of fuel, $15,000 (placeholder), tax-deductible like a business vehicle. It is sold next to the cart at General US Trucks, in person only. The game's own systems treat it as a car: fuel, gas stations, street parking fees and tickets, sleep (`PrefabFixes`) [code, unverified in game].
- **Opening a truck.** The "Open here" and "Prices" buttons show in the driving panel. Open needs a stop in a legal parking space (the game's own parking check), and pavement beside the hatch (the truck's right side; with the hatch to the road it refuses), then uses the game's Park button to step out; the hatch opens, and closes when the player drives off. The player serves within 8 m, as at the cart. The queue lines up on the truck's right, from the hatch, at the nearest pavement out to 3.4 m from the truck's middle, so a wide parking space still puts it at the kerb; places on the road layer or a crosswalk don't count (`WindowShape`). Whether the road layer covers streets is unverified: it read "no" at every pavement spot logged so far; a truck queues 10. Core's menu, stock and sales are the cart's, with the truck's 4 slots and 160 units.
- **Licence and permits.** `CityLicensing` sets the City of New York contact's dialog override (an unused `CallDialogType`, registered with `CallDialogFactory`), so the contact gets a call button. The call offers the licence (or its renewal, in the last 10 days) and then a permit for each cart and truck that has none, one at a time. Fees are charged with a tax-deductible label. The licence needs the Food Safety course. A cart or truck refuses to open without a licence or permit, and an open one closes when the licence runs out. The city texts when renewal opens and the day after expiry. The contact is added to the phone without a notification when the mod loads.
- **Tax.** Unchanged: street food sales were already on the tax statement since Milestone 1, and the truck's go into the same record. The fees are deductible expenses.
- **Map.** `MapFilters` adds a "Street food" line to the city map's filters: offices, factories and warehouses, nightlife, theatres and cinemas, and "Your food carts". The building filters use the demand model's own rules (Core `StreetFoodMap`, on `AreaClassifier`), so a highlighted building brings that crowd. The vehicle filter adds a marker per cart, green while open; trucks already have the game's own vehicle marker, and so does a cart bought or spawned in the current session (the filter only turns that one green).
- **Debug.** Ctrl+F6 spawns a free food truck, Ctrl+Shift+F6 grants a free licence and permits for every food vehicle (throwaway saves).
- The Econoview row is now "Street food", not "Food carts".

## Milestone 5: hires

- A hire's customer service skill sets their speed and their customers' mood, so the schedule can put strong workers on peaks and cheaper ones off-peak.

- A hired worker serves alone at a parked vehicle, at the game's wages, as a deductible expense.
- Hire schedules: the vehicle is marked in transit for the game's travel time, then placed in a free parking spot at the next stop.

**Core half, built 8 October 2026 on branch `core-m3-m7`:** `HireSchedules` and `Travel` in `Core/Fleet`. Decisions:

- A schedule is up to six stops a day, each with a spot, a position and opening hours (a late stop can close at 03:00), on chosen weekdays. The vehicle leaves as late as it can and still open on time. If it can't make it, it opens on arrival and closes at the usual time; a stop it can't reach before closing is skipped. Leaving a stop closes the queue.
- Travel time is the game's taxi formula: straight-line metres × 0.04 × game speed, in game minutes [code, unverified in game]. A cart moves at the 1.2 m/s walking pace, and a real second is one game minute at normal speed, so a hire pushing a cart covers only about 72 m per game hour. Hire schedules are really for trucks, and carts need stops a block apart.
- Restocking a hired vehicle is still open: with no commissary, somebody has to carry boxes from a wholesaler.

**Design answers, 8 October 2026 (Peter, before the game side):**

- **Hires are the game's own employees.** The player hires a worker the usual way (job board, recruitment agency) and puts them on a cart or truck. They stay unassigned to any building in the game.
- **The vehicle's panel runs it.** A "Hires" button on the cart's and truck's panel takes on and lets go of crew and builds the route: "add this spot" saves where the vehicle stands, with opening and closing hours picked from a list. The player visits each stop once to add it.
- **The player restocks.** The crew sells what is loaded; sold out closes the window until someone brings boxes. The commissary stays a later feature.
- **The crew shows at the window** within the visible queue's range: a standing character dressed as the employee.

**Game side, built 8 October 2026 (0.6.0, branch `milestone-5`, not yet checked in game):**

- **Core additions.** `HireRunner` (`Core/Fleet/HireRunner.cs`) keeps each crewed vehicle's day plan in the save (`VehicleState.Shifts`, today's and a late stop's from yesterday) and gives one step at a time: arrive when the travel time is up, close a window the crew opened when its stop's hours end, leave for a stop the vehicle is away from (timed from where it really stands), open at the stop once its hours begin. A vehicle the player holds, drives or opened themselves is left alone; one the player used in the last game hour, or stands within 15 m of, isn't driven away (the crew still opens and closes it), so the player can step out at a wholesaler to restock. A crew with nothing to sell doesn't set off. Calling it again after each step catches up after a load or a time skip. The stop radius (25 m) is in `rules.json`; how many work a window at once is the vehicle's `staff` in `economy.json` (cart 1, truck 3), the player included. A truck the crew parked further out for its stop still counts as there (`VehicleState.Arrival`), a vehicle that can't sell where it stands is moved, and a departure that would arrive after the stop closes doesn't start. Editing the route replans today but keeps a late stop from yesterday running. `ServiceRules.Team` gives several workers one speed: the mean of each one's speed, and customers react to the mean effective skill. New save fields are additive (`Crew`, `Shifts`, `HireStop`, `Arrival`, a stop's `Y` and `Heading`, a transit's destination height and heading and where it left from); the schema stays at 1.
- **Crew** (`HireCrew`). Anyone hired and not assigned to a building can join one vehicle's crew. The mod logs their shift hours on the game's own counters (`workedHoursToday`, `workedHoursThisWeek`, worked days) before the game's hourly employee run, for each hour the shift covers at least half of, so the game's midnight payroll pays them as unassigned wages, tax-deductible. They are paid for the planned shift, like a building's rota, even when the vehicle can't run (with the private driver, waiting for parking, or after an error stopped its route). The route's hours and days stand in for a building's work shifts in their job demands, counted the way the payroll counts (an hour covered at least half; a late stop's hours after midnight on the next weekday). Midnight's hour is counted from a preview of the new day's plan, which the payroll doesn't save. Anyone a headhunter is replacing stops serving. Three Harmony patches: the hourly count (`EmployeeHelper.RunHourly`), the "no task" complaint never fires for crew (`NoTaskAssignedComplaint`), and the game's "unassigned employee" to-do isn't created for crew (`TasksUI.CreateTodoTask`). A crew member who is fired, resigns or gets a building job leaves the vehicle with a message; a vehicle that is gone (sold, seized) lets its whole crew go. [code, unverified in game]
- **The window.** Crew on shift serve with skill × satisfaction / 100; the player at the window adds their own skill, up to the vehicle's staff (the best skills serve). Sick or training crew don't serve. A crewed vehicle keeps selling through sleep and other time skips: the route runs through the skipped minutes in order, stopping at each leg's start and end and each arrival, and the window sells up to each of those moments, so a skip over a whole stop still opens, sells and closes it. Sales away from the player come without a message each; each stop closes with one summary. The sales and the route share one game-minute clock, and during a skip both run every frame, so demand and the ledger day are at most one frame of game time behind (at midnight, a frame's sales can land on the new day).
- **Moving.** A vehicle leaving for a stop is taken off the map: its controller is destroyed and its parking state set to Undefined, so the game charges no parking while it is away. Its colliders are removed from the traffic system first, as the game's own vehicle sale does. On arrival a truck goes into the nearest free parking space within 60 m of the stop, found the way the game's private driver returns a car (`ParkingLaneGenerator.TryGetRandomFreeSpotForPlayerVehicle`), turned round if its hatch would face the road; a cart goes exactly where the player stood it. With no free space, the truck waits 10 minutes, then takes one within 200 m, and after 20 minutes goes back where it left from, with the parking state it had there, and gives that stop up until its hours end. A vehicle in transit at a load is taken off the map again. A vehicle in a building or underground garage is left there (the crew says it waits on the street). Travel uses Core's times with the game's speed setting, as the taxi does. One vehicle's route failing stops only that vehicle; a vehicle it left on its way is brought back, where it left from or nearby, as soon as there is room. A crew window closed by a failing sale stays closed for the rest of that opening.
- **Route editing** (`StaffDialog`), in the game's list picker: the employees who can join (best customer service first, with skill, satisfaction and wage), the crew to take off, "add this spot" (only where the vehicle could open now), each stop to remove, and the days (every day, Monday to Friday, Monday to Saturday, weekends). Stops are kept in time order and may not overlap; a closing time after midnight reads "(after midnight)". Adding or removing a stop keeps the kept plans and the open window pointing at the same stops.
- **Refusals.** A stop where the crew can't open (no stock, no permit, licence run out, pavement rule) says why once per stop and day, and the crew waits there until the stop's hours end.
- **Seen at the window** (`WindowCrew`): within the queue view's range, a standing character per crew member, with the employee's looks (or stable random looks if they have none yet): behind the cart opposite the queue, or inside the truck behind the hatch. They stand still: the game has no serving animation outside a building.
- **Uninstall** puts every crew member back as a plain unassigned employee and ends routes.
- **Reviews.** Eleven rounds with Opus 5.5, GPT 6 Astra and GPT 6.1 Sol until all three said ship; briefs, findings and the fixes for each round are in `build/review-m5/` (not committed). One finding was taken only in part: during a time skip, sales and demand follow the live clock to within one frame of game time.
- **Debug.** Ctrl+Shift+F9 hires a customer service worker with skill 60 (the game's own candidate generator), so a test session needn't run a recruitment campaign.

## Milestone 6: depth

- Regulars per spot and weekday.
- Health grades with inspections.
- Same-food competition between vehicles (shops are covered by the game's market demand).
- Premium spots (first come) and event pitches.
- Live venue crowds: opening hours for every venue, fixed theatre and cinema surge times, real counts for the player's own venues.
- Partnerships with the player's own venues.

**Core half, built 8 October 2026 on branch `core-m3-m7`:** `HealthGrades`, `Regulars`, `Competition` and `ReputationFactor` in `Core/Reputation`; the rules are in the design's Health grades, Regulars and Competition sections. The game side combines them: `ReputationFactor.Combine(grade, regulars, competition footfall)` goes into `DemandQuery.Reputation`, and competition's per-category shares into the new `DemandQuery.CategoryShares`. Shares apply after the menu's ranking and its appetite cap, and an estimate without them is unchanged. Decisions beyond the design:

- Health points come from the last 14 open days, so closing doesn't wash them away. Inspections have no schedule: after a 7-day gap the chance builds day by day, so the intervals average 12, 6 and 4 calendar weeks with no safe stretch. A vehicle draws at most once a day.
- Regulars are fleet-wide. The weekday comes from the day number (day 1 is a Monday [code, unverified in game]), and days the game skipped still count as missed.
- Competition counts only open neighbours with something to sell, so parked empty carts bring nothing.

**Peter:** second balance check over a full in-game week, including a rainy day and a weekend.

## Milestone 7: catering and Bakery

- Catering offers by text message from a bookings contact; booked gigs as map filters.
- Bakery mod goods in the display case when that mod is installed.

**Core half, built 8 October 2026 on branch `core-m3-m7`:** `CateringOffers` and `CateringGigs` in `Core/Catering`; the rules are in the design's Catering gigs section. The game side's contract:

- Once a day, `ForDay` with the candidate places (address and main area type), the eligible trucks, items sold, the reference price per category the trucks can sell, and the largest truck's stock limit. The game side sends each offer as a text message.
- The player's reply calls `Accept` or `Decline`. `ExpireOffers` drops offers nobody answered.
- When a truck reaches the place, `Arrive` with the truck's ID, type, menu and stock. Only `Started` counts. `NotYet`, `NotReady`, `Busy` and `NotEligible` change nothing, so the player can restock and come back before the deadline. A started gig's units are held back from street sales.
- When the truck leaves, or once the gig's end has passed with the truck still there, call `Finish` with the time it left (or the end), then pay the fee and take the units. Never call it during the gig. When time moves on, do that first and then call `MissOverdue`, and message the player about the misses.
- A missed or failed gig only lowers the booking reputation, so fewer offers come.

## Milestone 8: art and release

- Final cart, truck and equipment models from the winning Blender pipeline, swapped into the existing prefabs.
- Workshop packaging, README with the evidence section, a private upload, a check on a clean install, then public.

## Checklists

Every milestone ends with three lists in the mod's `CHECKS.md`, split by who can check what (decided 8 October 2026, after two long sessions where the useful finds came from playing, not from scripted rows):

1. **Automated:** Core tests green, build hash and version logged, install.
2. **Peter in game:** about 15 minutes of normal play, asking only for what eyes or judgement can tell: how something looks and moves, whether the numbers feel right. No rows for things the log proves.
3. **Log lines:** what the agent reads from `Player.log` afterwards, including each Harmony patch's Active line. Where something can't be seen in the log yet, add a log line rather than a checklist row.

The uninstall step, a save made without the mod and removing the mod are checked only in milestones that change save data or vehicle types, and once before release.

## Parallel tracks

Each track owns its folders. `FoodTruckRuntime` has one owner at a time.

| Track | Where | Owns |
|---|---|---|
| Unity and vehicles | Mac, SDK project | `unity/`, `Game/VehicleAdapter`, build scripts |
| Core | Mac, any agent | `Core/`, `tests/`, `data/` |
| Game adapters | Mac build, Windows test | `Game/` except the vehicle adapter |
| Art | Blender on the Mac | models, until Milestone 8 |

## Cut list

If version 1 runs long, cut from the top. Never cut: cart and truck, the data-driven demand model, legal spots, licence through the course, saves, the tax patch.

1. Partnerships with the player's own venues
2. Live venue crowds beyond opening hours and fixed surge times
3. Event pitches
4. Hire schedules with simulated driving (keep hires serving at a parked vehicle)
5. Catering gigs
6. Health grades
7. Visible queues (fall back to a queue counter)
8. Bakery items

## Decisions made while planning

- Per-save state lives in `GameInstance.modData` if spike 3 passes; it travels with Steam Cloud saves. Otherwise an own file per save.
- The mod uses its own price curve, not the game's.
- Shop competition comes only from the game's market demand. The same-food rule applies between vehicles.
- A vehicle's permit covers its crew; only the player takes the Food Safety course.
- Carts cross bridges with the game's bridge skip and walking time.
- `Economy.ReferenceShopIncome(difficulty)` makes the calibration targets testable.
