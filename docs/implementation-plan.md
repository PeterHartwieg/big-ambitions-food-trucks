# Food truck mod: implementation plan

Turns [design.md](design.md) into ordered work. Written 7 October 2026. The structure follows advice from a Fable 5.1 review of the design: build one playable slice first, then add features in milestones that each end in an in-game check.

## Start here

1. Read this plan, then [design.md](design.md). The design's Technical answers table has the code facts, with class names to search for. The NYC research note in `research/` is background only.
2. Machine setup (paths to the decompile, the SDK clone, the game PC and the test save) is in `CLAUDE.local.md`, which is not committed.
3. Milestone 0 results go into the README's evidence section, marked checked, pending or unverified.

**Status, 9 October 2026:** Milestone 6 is half built as 0.7.0 on branch `milestone-6` (health grades, regulars, competition, live venue crowds; premium spots, event pitches and partnerships wait for Peter's answers in the Milestone 6 section). Milestone 5 (hires) is 0.6.0 on branch `milestone-5`, on top of Milestone 4 (0.5.0) and Milestone 3 (0.4.0); none of these is checked in game yet (`CHECKS.md`). Milestone 0 is done and played (README evidence table; session report in `evidence/milestone-0-session-1.md`). Milestone 1 was played on 8 October (README; session report in `evidence/milestone-1-session-1.md`). Selling, saves, the truck fixes and the uninstall step work. Carried into Milestone 2: the cart's grip (move the handle to the vanilla grip height and reach), a unit limit for the cart's stock, whether street food belongs in the game's day summary, the drive-by question (a workstation at the window?), and the unchecked items: hover outline, delivery refusal on a save with a warehouse, and the forced tax payment without the mod. Remaining debug keys are listed in the README; gate or remove them before the Workshop release.

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

**Game side, built 8 October 2026** (not yet checked in game): the data files ship in the mod's `data/` folder and load at startup (`DemandData`); `World` reads the buildings, businesses and neighbourhood around a cart from the city's door positions (on Open and each new day), plus time, rain, market demand and difficulty; `Legality` refuses parking lanes, crosswalks, building entrances and subway stations within 3 m (the game has no hydrant, driveway or pavement data; whether the road layer can tell pavement from road is only logged); `CargoLimit` hooks the game's two cargo methods so food stops at the unit limit and equipment at the slot count, the rest staying in the box; the fitted equipment (vanilla items in the cart's cargo, mapped in `economy.json`) makes the menu; `CartDemand` turns the model's customers, the price curve and the window's service rate into sales per hour; each customer buys a main item by category share and sometimes a cold drink. Ctrl+F9 shows the reading for the cart next to you. The cart's handle moved to the vanilla grip (1.16 m high, 0.65 m reach); measured in game on 9 October 2026, the vanilla hand truck's hands are 1.157 m high and 0.63 to 0.66 m ahead of the player, so the cart's grip matches.

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

- One tick, in whole minutes, for the visible queue and for sales away from the player. Customers arrive at random (Poisson) at the demand model's rate times the price conversion, wait in a first-come queue, and leave after their patience: 20 minutes, ±25% per customer, times 0.6 while it rains. Rain shortens the patience of people already waiting. Nobody joins a queue of 12. (Since the first playtest's fix, customers first walk up for 7 to 14 minutes; see below.)
- Each customer picks a category by the demand model's `CustomerShares` and gets that category's item with the most units left. A customer whose category has sold out walks on; the demand doesn't turn into drink sales. Add-on drinks ride with customers who came for food.
- `Servers` workers each make one order at a time, at the item's service time. When the player works the window, the game side either counts them as a server (automatic selling, as now) or calls `ServeHead` for each serve action. A hire who goes home puts their half-made order back at the head of the queue.
- Each sale takes one unit; the result lists the units to remove. Selling out sends the queue away and reports `SoldOut`, so the window closes.
- Deterministic: the random state lives in the save, so a save and load replay the same customers. Customer IDs let the visible queue spawn and remove the right people. Mono and .NET may round `Math.Exp` differently, which can change a draw but never what the save holds.
- The save schema stays at 1: every new field is optional and old saves load with defaults.

**Game side, built 8 October 2026 (0.4.0, branch `milestone-3`, not yet checked in game):**

- `CartSelling` runs `SalesSimulator` every in-game minute instead of `SalesTick`. The queue (`VehicleState.Service`) is in the save while the cart is open. The plan's best-accepted item per category (`CategoryItems`) and its drink (`AddOnItem`) are the only items Core may sell, so a price customers refuse never sells. The player within 8 m of the cart is one server; further away nobody serves, so the queue waits and thins out (the message says so in-world). Time skips send the queue away. The serve action per customer (the seller stand pattern) is not built: serving is automatic while the player stands at the window.
- Staff skill: `ServiceRules` in `rules.json`. Speed is linear from 1/1.5 at skill 0 through 1.0 at 50 to 1.3 at 100. The player's skill is `charactersData[0]`'s `ba:skill_customerservice` (50 by default) [code, unverified in game]; the player has no satisfaction, so their skill counts in full. Hires will use skill × satisfaction / 100, the game's own measure. Mood (complaining below 25, pleased from 75) only shows the game's good or bad service emoji on served customers for now; Milestone 6 can feed it into reputation.
- Queue limits per vehicle in `rules.json` (cart 6, truck 10), within the global 12.
- `CartQueueView`: within 100 m of the player, outside, and not during time skips, each open cart gets a line of up to 10 places beside it on the NavMesh (window, 6 waiting, 3 for served customers still walking up), on the side and in the direction with the most walkable places out of parking lanes. People walk in from points 9 to 19 m along the pavement, stand at the window while their order is made, and pay and walk off when Core completes it (changed after the first playtest, see below). While the line is on screen, each sale is held until its customer leaves the window; the units count as sold for Core at once, and anything held is paid before saving, closing, time skips and when the line leaves the screen; people who give up show the game's angry emoji. At most 16 people at once. They are removed on close and grab (they walk off), beyond 110 m, indoors, on time skips, scene changes, session detach and the uninstall step. The game's character prefab and NavMesh agent are the spike 5 pattern; walking speed, emojis and the layout are unverified in game.
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

**Game side, built 8 to 9 October 2026 overnight (0.7.0, branch `milestone-6`, not yet checked in game).** Built: health grades, regulars, competition and live venue crowds. Premium spots, event pitches and partnerships followed in the second half below.

- **Reputation in the demand.** Each sales plan passes `ReputationFactor.Combine(grade, regulars, neighbours' footfall)` and competition's category shares into the demand model (`CartDemand.Reputation`). The open message names the grade on the window ("grade pending" until the first inspection), and the open log line lists the three parts.
- **Pitches.** Regulars and routes know a pitch by a key (`World.PitchKey`): a known pitch within 30 m (a regulars entry, a route stop, or a visit today not yet counted), else a new 10 m grid square, so parking a few metres off still finds the regulars. Route stops use the same key; since two stops can now share one, arrivals and given-up stops also keep the stop's index.
- **The day book** (`Core/Reputation/ReputationBook.cs`, saved as `VehicleState.Days`), by working day: the night until 05:00 (the demand model's night end) belongs to the evening before, so a stop past midnight is one working day for cleaning, regulars and inspections. While someone works an open window, each tick adds its minutes to the pitch fixed at opening; opening marks the day; cleaning and stale sales mark it too. At the end of a frame in which the game hour changed (after sales and routes have caught up), and once after a load, every working day before yesterday's goes into the vehicle's health marks and the fleet's regulars, in day order, and is dropped; then the regulars move on through that day, so days nobody worked count as missed. The one-day lag lets a late shift be cleaned up the morning after and a window open past 05:00 finish its day. Nothing more is recorded for a day already counted.
- **Inspections** are drawn when a vehicle opens (player or crew), and on each new working day a vehicle stays open into; a visit sends a message with the grade and points.
- **Cleaning.** A "Clean" button on the vehicle's panel: 20 in-game minutes under the game's time-skip overlay (the taxi's pattern). Cleaning counts for the working day it happens on and for the working day the vehicle last opened on (`LastOpenedDay`), so cleaning up after a late shift reaches the night. A crew cleans whenever they close a window, sold out or not, for the day of the last minute served. The game's own cleaning (the mop, cleaning shifts) works only inside buildings.
- **Stale stock.** The game's food never spoils, so the mod's rule (`health.staleAfterDays`, 3): the mod keeps each item's units on board in batches by the day it first saw them (loose or boxed), and the oldest go first. Food ages by calendar day; selling a unit whose batch is that many days old marks the working day of the sale. Each sale takes the oldest unit at once, so a refill before the next look at the cargo can't inherit an old date; topping up doesn't make fresh units stale.
- **Live venue crowds** (`Core/Demand/LiveCrowds.cs`, `rules.json` `liveCrowds`): what each nightclub, theatre, cinema, office and factory near a pitch is doing is read every game hour (a crew's vehicle far from the player is read where it stands); its weight is worked out at each minute, so surges start and end on time. Sales run one game minute at a time; each minute plans demand at its own time and reads the venues for its own hour, so a catch-up over a skip follows opening hours and surges as they happen. An area's crowd is multiplied by its venues' mean weight after the presence curve (`AreaClassifier.LiveFactors`, `DemandQuery.AreaLive`), so a 2.5× surge is 2.5× that area's crowd. Closed: nothing. Theatres let out at 22:30, plus 17:00 at weekends; cinemas every 2.5 hours from 17:00 to 00:30; each surge lasts 45 minutes at 2.5× (theatre) or 2× (cinema), times 1.5 on Saturdays and Sundays. A nightclub's guests come out for 30 minutes after it closes, at 2.5×. Offices and factories bring their staff at work ÷ 10, between 0.3× and 2×: the player's own from their work shifts this hour, AI ones from their head count while open. Opening hours come from each business's own schedule, AI businesses included [code, unverified in game]. Real customer counts for the player's own venues are not used yet.
- **Balance.** The calculator (`tools/balance`) still assumes every venue open at all hours. Closed venues now bring nobody, so nightlife pitches by day and office pitches at weekends lose their crowd, and theatres and cinemas gain at their surges. Peter's week-long balance check below covers it; the calculator needs opening hours per business type to follow.

- **Reviews.** Seven rounds with Opus 5.5, GPT 6 Astra and GPT 6.1 Sol until all three said ship (fresh reviewers for rounds 1 and 2, then resumed); findings and fixes are in `build/review-m6/` (not committed).

**Second half, built 9 October 2026 overnight (0.8.0, branch `milestone-6`, not yet checked in game).** Peter was away, so I took my own recommendations to the four questions I had left for him (premium spots, events, partnerships, the first half's defaults); each is easy to change in `data/pitches.json` or to take out.

- **Premium spots** (`Core/Pitches`, `data/pitches.json`): 17 named pitches, two or three per neighbourhood, picked from the balance calculator's best pitches (`--reports premium`) so they cover office lunches, mornings, nightlife and weekends. Each is outside one building, named by its address; a vehicle within 40 m of the building's nearest entrance stands on it. The first of the player's vehicles to open there holds it until it closes (`VehicleState.PremiumSpot`): 1.25 times the passers and the spot's character as areas (office, nightlife, retail, transit, industrial or waterfront), as a zone in add mode does. A later vehicle there sells as on any pitch and is told who holds it. AI trucks don't exist yet, so first come applies only among the player's vehicles. Peter can veto any spot in game.
- **City events:** the City of New York texts 3 or 4 days ahead about a weekend market (Saturday and Sunday, 10:00 to 16:00, 0.6 chance a day, $80 a place) or a street festival (Friday and Saturday, 16:00 to 23:00, 0.4 chance, $120), at one of 7 event places (festivals at 5 of them), each at least 110 m from every premium spot so an event never takes a premium spot's street. Places whose building the city doesn't have get no events, and the city texts only players who own a food vehicle. The draw is seeded by kind and day, so a save always gets the same events; days the game skipped are caught up. The player books places by phone (the city's call gets "Event places"), up to 2 per event, until it ends; the fee is tax-deductible. While it runs, a vehicle with a booked place within 60 m gets 2 or 2.5 times the passers and the event's crowd (retail or nightlife); booked places go to the first vehicles there, including one already open when it starts, and come back when that vehicle closes. A vehicle without a place can't open there while it runs ("The street is taken by a city event"), and one already open when it starts closes. The event's crowd comes whatever the venues around it are doing (`DemandQuery.PitchAreas`, kept apart from the venues' live factor). An event stays in the save until every open window has sold past its end, so a catch-up over one or more midnights still sells through it. An event whose place or kind is no longer in `pitches.json` is dropped at load, and places booked for one not yet over are paid back. The game's tax tracking ignores money coming in under a deductible label, so the refund leaves the fee's deduction standing; it only happens after an edit to the data file. The morning of the event, the city reminds the player of their booking. The map's new "Premium spots and events" filter lights the premium spots' buildings and the places of events to come.
- **Partnerships:** a vehicle within 50 m of one of the player's own nightclubs, theatres, cinemas, offices or factories can sign a partner deal from its Hires list (one venue per vehicle, one vehicle per venue), and end it there. While it sells there, the venue counts as if the vehicle stood at its door, and its surges (a show letting out, a club closing) are 1.5 times as big above the usual crowd; guest and staff counts are not boosted. The design's venue bonus (row 10) turned out not to work as written: the game's cached customer demands are rebuilt from the venue's own items and hold no food demand. Instead a Harmony patch on `BusinessHelper.UpdateSatisfaction` adds 5 points to the venue's facility satisfaction (and the overall, by the game's mean of four) once for each day the partner sold there for an hour: in the next daily update. When the day's last minutes are sold only after that update (a time skip), the bonus goes on that morning's score as soon as the day counts as worked; the postfix notes the day the game last worked the score out from paying customers (`PartnershipState.ScoredDay`). Only a score the game worked out from paying customers gets it; with none, the game copies the old score and the bonus would stack. The game runs that update only for businesses that earn from customers, so offices and factories get the crowd and no bonus. A deal ends with a message when the vehicle is gone or the venue is no longer the player's.
- **Real counts for the player's own venues:** a nightclub, theatre or cinema the player owns brings its guests of the hour before ÷ 20, between 0.3× and 2×, times any surge (`liveCrowds.guests` in `rules.json`); AI venues have no counts and keep their usual crowd. An hour the venue was closed says nothing and counts as unknown (weight 1). So does an empty count for the hour that has just ended while the game is still simulating its businesses' hour (it spreads that over several frames, `BusinessSimulatorHelper.Work`), and the spot is read again at the next sales step until the work is done; any other empty hour is a real empty venue. Today's hours come from the orders the game hasn't processed yet, earlier days' from its daily history.
- **Defaults kept from the first half:** stale after 3 days, cleaning takes 20 minutes, the crew always cleans, a closed venue brings nobody.
- **Reviews.** Five rounds with Opus 5.5, GPT 6 Astra and GPT 6.1 Sol until all three said ship (resumed reviewers after round 1); findings and fixes are in `build/review-m6b/` (not committed). Taken only in part: the earlier vehicle can lose a shared place when a catch-up reaches the later one first, and a refund for an event dropped from the data file leaves the fee's tax deduction standing.
- **Debug:** Ctrl+Shift+F11 puts an event at the nearest event place, from now for three hours, with one place booked free.
- **Balance** (`tools/balance --reports pitches`, dry weekday or Saturday in May): a premium spot adds about $50 to $400 to a hot dog cart's day and $50 to $1,000 to a truck's; at a few weekend nightlife spots the cart is already at its window's limit and gains nothing. An event place pays $35 to $640 after the fee for a hot dog cart and $150 to $1,580 for a truck over the event's hours. The first fees ($150 and $250), and event places on the busiest streets, lost money for a cart at half the places: there its window and its 80 units already run full, and more passers add nothing. The calculator has no live venues, so it shows events and premium spots as if every venue were open.
- **Shared places over a time skip.** Routes catch up one vehicle after the other, so who holds a premium spot or a booked event place is kept as time-stamped holds (`Core/Pitches/Holds.cs`, `SaveState.PremiumHolds`, `CityEvent.Holds`): a claim counts the other holds from its minute on, including ones that start later, at their busiest minute, so no more vehicles than places ever hold a place at one minute, whichever the catch-up reaches first. Taken only in part: when the vehicle that came later in game time is caught up first, it keeps the place and the earlier one goes without (at an event it doesn't open). Windows closed during a catch-up (sold out, licence run out) give their places back at that game minute.

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

**Game side, built 9 October 2026 overnight (0.9.0, branch `milestone-7` on top of `milestone-6`, not yet checked in game).** Peter was away; these are my calls.

- **The bookings contact.** "Street Food Bookings", a permanent contact the mod adds (`Catering.cs`), texts each offer: the kind, the place's address, guests, food, day and hours, fee, and the day to answer by. Calling it goes through the open offers one at a time, "Take it" or "Turn it down"; with none, it says how many gigs are booked. Offers come once a day (and once after a load on a day without them), from Core's `ForDay`.
- **Places** are the city's buildings whose own type or business points to one area (offices for office lunches, homes and clubs for parties, shops for events; the design's waterfront has no buildings yet), named by address. A truck stands at a gig within 60 m of the building's nearest entrance.
- **The fee's price** per food is the highest default price among the items of that food the caterers' menus can serve.
- **Arriving.** A truck with a permit, standing still at the place for two game minutes, ten with the player at the wheel (the player may sit in it; driving past, in transit or still at another gig doesn't count) from an hour before the start, starts the gig if someone can serve (any of its crew not off sick or training, or the player in or beside it), it has the licence, the food on its menu and a portion per guest, not counting portions already sold to customers walking to the window. A truck is watched from the day before, so one parked there through a time skip counts from when it was first seen there, and sleeping in the truck outside doesn't make it late. Bookings are tried earliest first. Not ready says so once per gig; the player can restock and come back before the start plus 10 minutes. The crew isn't paid extra for a gig outside their route's hours: the route's hours stand in for their shifts.
- **During the gig** its portions are held back from street sales (`CartDemand.Plan`), so the window can stay open on the rest, and the crew's route leaves the truck where it is for the gig's minutes, even when a route catches up after the gig was settled. The player may walk away once it has started.
- **The end.** At the gig's end the truck is paid and the portions held back for it leave the cargo; the fee goes into the street food record once for all its portions (`StreetSales.RecordTotal`), so it is taxed like any sale, and the stock's wholesale cost into the day's costs. Stale food served at a gig marks the health grade as at the window. Nothing leaves the truck when the record can't take the fee, and portions promised to customers walking to the window are never taken. A fee the game refuses is kept in the save and tried again each game minute. A gig day counts as a day the truck opened, for the health grade and the inspector. A truck gone unseen over a skip past the gig's end stayed to the end (the player can't drive during a skip, and the route keeps it there for the gig's minutes); one seen gone live left when it was last there. Portions the truck no longer had aren't paid for. A truck that leaves early fails the gig, at the last minute it was seen there. Missed gigs get a text and cut the booking reputation (Core).
- **Map:** a "Catering bookings" filter lights the places of booked gigs.
- **The contact** is made again with its call button if the player deletes it. The uninstall step drops open offers and booked gigs without a word and then removes the contact, so a save without the mod shows no raw texts.
- **Bakery mod:** its bread, buns and chocolate cookies are pastries on the display case, its latte and americano coffee on the coffee machine; its bread counter fits as a display case and its bistro machine as a coffee machine (`economy.json`). Without the mod nothing changes. The log says at load whether it is there [code, unverified in game].
- **Reviews.** Five rounds with Opus 5.5, GPT 6 Astra and GPT 6.1 Sol until all three said ship; findings and fixes are in `build/review-m7/` (not committed). The first round caught the fee booked once per portion. Taken only in part: no staffing check once a gig has started, crew aren't paid extra for gig hours outside their route, and while one truck is being confirmed as parked the other gigs' misses can come up to ten game minutes late.
- **Debug:** Ctrl+F12 books a gig at the nearest place from in 30 minutes, 40 guests, for the first food a truck with a permit can serve.

## Fixes from the first playtest (9 October 2026)

Peter's first session on 0.9.0 found these; they come before the rest of Milestone 8.

- **The Food Safety course has no door.** The 1.0 business school has five doors (Basic HR, Headquarters, Office Businesses, Market Demands, Product Manufacturing), and `DiplomaName.FoodSafetyCourse` is an enum value nothing uses, so the licence couldn't be bought in normal play. *Fixed on branch `fix-course`, not yet checked in game:* the mod adds a `DiplomaData` for the course at every load, 6 hours at $25 an hour with no prerequisite (`fleet.course` in `rules.json`; the numbers still to confirm with Peter). The Market Demands door now asks first, with the game's `HudConfirm`, which course to take. The confirm button opens the game's study panel for Food Safety at the door's own chair and computer; the "Market Demands" button runs the door as before. Escape, opening the phone and the game's other ways of closing the question start nothing. Price, time skip and completion are the game's, nothing is paid before the panel's Start button, and the result is kept in the save's own `PlayerDiplomas`, so the licence check is unchanged and a save still loads without the mod. Once the course is passed, the door goes straight to Market Demands. The City of New York's refusal says where the course is taught.
- **Queue places go by spawn, not arrival.** Core put a customer in the queue the minute they arrived; the view then spawned them 9 to 19 m along the pavement and sent them straight to their place in Core's order, so someone spawned later could be ahead of a person already standing at the window, and Core could serve them before they got there. *Fixed on branch `fix-queue`, not yet checked in game:* an arriving customer now walks up first (`ServiceState.Approaching`, saved with the queue; older saves load with nobody walking up). The walk takes 7 to 14 whole minutes, drawn from the saved random state (`queue.walkInMinutes` in `rules.json`; an in-game minute is a second at normal speed). Then they join the back of the queue, earliest to reach it first, and their patience counts from there. Whether the queue is full is checked when they reach it, not when they turn up: counting the people still walking would hold places for minutes and leave the window idle. Closing, selling out and time skips send the walkers away with the queue. The view spawns people when Core has them walking up, at the way in whose distance best fits the walk left, shows the first four, and sends them to the place behind the line; they get a line place only when Core lets them join. Sales away from the player are the same, and the balance calculator, which runs Core's simulator, moved by under 1%.
- **The sale comes at the start of the wait.** The view paid a sale when its customer stepped up to the window, and Core might have finished the order before they arrived. *Fixed on branch `fix-queue`, not yet checked in game:* nobody is served before they have joined the queue, so the order's service time (3 to 6 minutes for food, a second each at normal speed) runs while they stand at the window. When Core completes it, they pay and walk off, and only then does the held sale pay out (`Paid`), so the money and the sale message come as they leave. Someone who reaches the window after Core finished picks up for a second first. Held sales are still all paid before saving, closing, time skips and when the line leaves the screen.
- **Opening a truck puts the player outside.** "Open here" uses the game's Park button to step out. The player should stay inside, standing at the open hatch behind the counter, where the crew stands (`WindowCrew`), facing out and serving from there. Needs a spike: how to hold the player character there with the game's controller (and what the camera does), and how they leave (the leave key steps them out by the door; the window stays open while they are within 8 m, as now). The cart stays as it is.
- **Queue view error on quitting.** `CartQueueView.CheckLines` threw a NullReferenceException in `PlayerHelper.GetPosition` as the game quit (the player is gone by then). *Fixed on branch `fix-course`:* the check is skipped while there is no player object.
- **Parked cars spawn on a cart in a parking lane.** The game's parking lanes keep a spot free only for player vehicles found through `VehicleHelper.AllPlayerVehicles` that have a `CarHolder/Colliders/BodyCollider` (`ParkingLaneGenerator.AddPlayerVehicleObstacles`, `VehicleHelper.TryGetBodyColliderBounds`) [code, 1.0]. The cart has no collider at that path, so a lane regenerating near it puts a car on top of it. Fix with the cart model swap: give the cart prefab a `CarHolder/Colliders/BodyCollider` box (the widened body collider from `art/README.md`), check the cart is in `AllPlayerVehicles`, and log at load whether the lanes see it. If either fails, a postfix on `AddPlayerVehicleObstacles` adds the carts.
- **Crew don't serve when the player opens.** With a worker assigned to the truck but no route, the player opened it and walked away, and the window paused ("Nobody's at the truck's window"). Crew serve only during a route stop's hours (`HireCrew.OnShift` needs `HireRunner.Want`), because those hours are what the game pays them for. Peter expects an assigned crew to serve whenever the vehicle is open. Fix: crew who are present serve whenever their vehicle is open and they aren't driving it elsewhere. Hours outside the route are paid by the mod at the worker's hourly wage, as a wage transaction dated that day (and in the vehicle's crew wages line in Econoview), and the open message names who is serving.
- **A breakdown of the street food business (Peter, 9 October 2026).** Today Econoview has one "Street food" row (the day's sales less the stock sold) and BizMan has nothing.
  - *Econoview* (built 9 October 2026 on branch `fix-econoview`, not yet checked in game): "Street food" is a group in the income statement. Under it, one row per vehicle ("Food cart", "Food truck", numbered when there are several of a kind; "(sold)" for one no longer owned), coloured like the game's business rows and closed until clicked. A vehicle opens into Sales, Catering, Stock sold, Crew wages, Parking and tickets, and Vending permit, each shown only when it has an amount in one of the four days. The vendor licence and event places belong to the whole fleet, so they are their own rows under Street food ("Vendor licence", "Event places", refunds counted against bookings), not under a vehicle. Days from before the breakdown show their old profit as one "Not broken down" row. Partnerships and premium spots cost nothing, so there is no "other fees" line.
    - *Ledger* (Core, `SalesLedger`, `StreetFoodStatement`): each day's record has, per vehicle, sales, catering, stock cost, crew wages, parking and permit, plus the fleet's licence and event places (additive fields; old saves load with none). Street sales and catering fees are told apart where they are recorded (`StreetSales.Record` and `RecordTotal`), and the day's revenue for tax is unchanged.
    - *Totals.* The day summary now adds sales and catering less stock, permits, the licence and event places, the costs the game's own summary doesn't know (none of the mod's charges carry a game category). The amount added is kept with the day when it is marked, so Econoview shows exactly what the stored total holds; days marked by older versions held sales less stock. Crew wages and parking are in the game's stored summary already (`totalUnassignedStaffWages`, `parkingFees`), so they are moved in Econoview's display only: the vehicles' share is taken off the "Unassigned staff wages" and "Parking fees" rows (and their groups) and shown under the vehicle. The stored summary is never changed for this, so `totalProfit`, the weekly history, rivals and a save without the mod see the game's own numbers. The moves are on only when the Street food rows and both row patches are active, and for a day only when the vehicles' share fits inside the game's row.
    - *Attribution* [code, 1.0]. Crew wages: the game pays each employee at midnight into the day before (`EmployeeInstance.PayWage`), for the hours the mod counted that day (`HireCrew.CountHour`). Each counted hour is logged with its vehicle (`CrewHours` in the save, additive; `CrewLog` in Core), and a wage booked as unassigned is split over the vehicles by those hours, so a hire who moves from one truck to another mid-day is shared between them. The save keeps the first day logging covered (`CrewHoursSince`, set to the day after the first load by a version that logs, since that day's earlier hours weren't logged). For days from then on, only logged hours count: a hire who worked in one of the player's shops and joined a crew without working it gets no vehicle wage. For earlier days the wage goes to the crew the hire is on at payment. Parking: `ParkingSimulator.RunDaily` charges each vehicle's `unpaidParkingAmount` into the day before, and `RunHourly` writes tickets ($125) per vehicle, so both are read per vehicle around those calls. A charge booked into a day whose summary is already stored (a crew member quitting mid-day is paid into yesterday) is in neither the game's stored figures nor the breakdown: Econoview leaves it out, as the game's own rows already do.
  - *BizMan* [code, 1.0]: the business list is built only from `BuildingRegistrations` (`BusinessScrollerController.PopulateAllModels`), and Insight, schedule and pricing all read the registration, so a vehicle can't be a normal BizMan business. Decided (Peter, 9 October 2026): one "Street food" entry at the end of the business list that opens a fleet page of our own, built from BizMan's parts: one row per vehicle with today's and the week's sales and profit, items sold per product (like the game's sold-amount tooltip), stock left, health grade, crew and route. Needs a spike on adding the entry to the list; if the list can't take it, the same page opens from the vehicle's panel.

**0.9.1 (9 October 2026):** the course, the queue walk-up and paying, the quit guard and the Econoview breakdown are built (branches `fix-course`, `fix-queue`, `fix-econoview`, merged into `milestone-7`); the truck window, cart parking, crew serving and the BizMan fleet page are still to do. Reviews: four rounds with Opus 5.5, GPT 6 Astra and GPT 6.1 Sol until all three said ship; findings in `build/review-fixes/` (not committed). Not yet checked in game; `CHECKS.md` has the list.

## Milestone 8: art and release

- Final cart, truck and equipment models from the winning Blender pipeline, swapped into the existing prefabs.
- Workshop packaging, README with the evidence section, a private upload, a check on a clean install, then public.

**State, 9 October 2026.** The truck's final model has been in the build since Milestone 4. The cart's final model (`art/cart/FoodCart.fbx`) is committed and passes its scorer, but the game still builds the cart from placeholder boxes (`CartAssets.BuildCart`); `art/README.md` has its placement table, with every attachment point matched to the code.

**Visible equipment (Peter, 9 October 2026).** Fitted equipment is still inventory only, so a cart looks the same whatever it serves. For the cart, the fitted pieces show on the counter, in its two slots, as a mix of:

- the game's own item model, where it fits a cart's counter (to check per item: size, and whether it reads from the street);
- a small model of our own from the Blender pipeline, where the game's item is shop-sized or looks wrong on a cart (the industrial coffee machine and the drinks fridges are the likely ones).

Each slot gets a mount point on the cart model, kept clear of the push bar, the umbrella pole and the box zone that `art/README.md` lists. The truck is not decided yet: the game's items inside behind the hatch is the simple option. Placing equipment freely ("tetris") stays a later version.

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
