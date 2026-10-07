# Food truck mod: implementation plan

Turns [design.md](design.md) into ordered work. Written 7 October 2026. The structure follows advice from a Fable 5.1 review of the design: build one playable slice first, then add features in milestones that each end in an in-game check.

## Start here

1. Read this plan, then [design.md](design.md). The design's Technical answers table has the code facts, with class names to search for. The NYC research note in `research/` is background only.
2. Machine setup (paths to the decompile, the SDK clone, the game PC and the test save) is in `CLAUDE.local.md`, which is not committed.
3. Milestone 0 results go into the README's evidence section, marked checked, pending or unverified.

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
| `Fleet` | Vehicles, equipment, permits and cap, crews, hire schedules, travel timing | cap blocks a fifth vehicle; travel time per route |
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
- Load stock at a wholesaler into the cart's cargo.
- "Open" at any spot. Sales come from a constant per hour, draw stock and pay money with the mod's label.
- State in `modData`: licence placeholder, open or closed, lifetime sales.
- One notification line per sale; no queue, no people.

## Milestone 2: demand and menu

- Data files with the first draft of every table in the design's Demand section.
- `WorldReader` feeds the real world into `AreaClassifier` and `DemandModel`: buildings, neighbourhood, daypart, day, rain, the mod's own season calendar, the game's market demand for shop saturation.
- Equipment as inventory; the menu follows the fitted equipment.
- Legality: carts use the mod's pavement rule, trucks the game's parking lanes (once trucks exist). The Open button names the failed rule.
- Own price curve.

**Peter:** first balance check. He plays a day at three kinds of spot and decides whether the tables feel right.

## Milestone 3: serving and queues

- The window interaction copied from the seller stand: the player serves the head of the queue.
- Visible queue within about 100 m; statistical sales elsewhere, from the same Core tick.
- Abandonment after about 20 minutes, sooner in rain.

## Milestone 4: truck, licence and city systems

- Truck vehicle with cargo, driven like any car.
- Vendor licence requires the game's Food Safety course. A permit per vehicle covers its crew. Fleet cap.
- Cart crossing a bridge uses the game's bridge skip with walking time.
- `TaxPatch` live; truck sales in the tax statement.
- "Street food" category on the city map: offices, factories, nightlife, theatres and cinemas, premium spots.

## Milestone 5: hires

- A hired worker serves alone at a parked vehicle, at the game's wages, as a deductible expense.
- Hire schedules: the vehicle is marked in transit for the game's travel time, then placed in a free parking spot at the next stop.

## Milestone 6: depth

- Regulars per spot and weekday.
- Health grades with inspections.
- Same-food competition between vehicles (shops are covered by the game's market demand).
- Premium spots (first come) and event pitches.
- Live venue crowds: opening hours for every venue, fixed theatre and cinema surge times, real counts for the player's own venues.
- Partnerships with the player's own venues.

**Peter:** second balance check over a full in-game week, including a rainy day and a weekend.

## Milestone 7: catering and Bakery

- Catering offers by text message from a bookings contact; booked gigs as map filters.
- Bakery mod goods in the display case when that mod is installed.

## Milestone 8: art and release

- Final cart, truck and equipment models from the winning Blender pipeline, swapped into the existing prefabs.
- Workshop packaging, README with the evidence section, a private upload, a check on a clean install, then public.

## Checklists

Every milestone ends with three lists in the mod's `CHECKS.md`:

1. **Automated:** Core tests green, build hash and version logged.
2. **Peter in game:** numbered steps of about 15 minutes in total, each with the exact thing to see. Always includes "load a save made without the mod" and "remove the mod and load".
3. **Log lines:** the `Player.log` lines the mod must print, including each Harmony patch's Active line.

Example for Milestone 1:

1. The mods menu shows Food Trucks enabled.
2. The dealer lists the food cart at the expected price.
3. Buy it, push it 50 m; it follows at walking speed.
4. Load 20 hot dogs at a wholesaler; the cart shows 20.
5. Open at 12:00 in Midtown; three sales arrive in the transaction history with the mod's label, and the stock goes down.
6. Save, quit to the menu, load: cart owned, sales count restored.
7. Remove the mod, load the save: the game loads, no exception in the log.

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
