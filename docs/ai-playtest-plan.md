# AI playthrough plan

An agent plays Food Trucks in a fresh character's save on the game PC, through the test bridge and the self-driving mod, for two to four in-game weeks. It plays as a player would: buys vehicles, rents commissary space, stocks up, sets routes and shifts, drives its own truck and serves at the hatch. It keeps a daily ledger and stops at the first broken rule. It runs before Peter's own long session, so that session can focus on how the mod feels.

It does not judge fun, clarity or how things look. That stays with Peter.

## What the agent can drive

- **Food Trucks bridge** (`tools/ft`, `ft-mcp`, only with `-foodtrucks-test`):
  - time: pause, speed, `run_until`
  - money, teleport of the player or a vehicle (to a point, a bay or a commissary door)
  - vehicle ops: open, close, enter, exit, back to the hatch, restock, transfer, rent bay
  - commissary orders, crew assign and shifts
  - `state` (clock, money, vehicles, crew, plans, `ledger_today`), events, the log
  - `ui` list and click, save, load, `shoot` with the game's own view
- **HART. Self Driving** (`DrivenRides`, file-polled test rig, enabled by the same `-foodtrucks-test` flag):
  - `ride kind=own x=.. z=.. speed=3 autopark=off` drives the car the player sits in to the building nearest x, z.
  - It writes one JSON line per ride to `DrivenRides/tests/ride-results.jsonl`, with `end`, `seconds`, `arrivedMetres` and `contactsCaused`.
  - It is driven from the Mac with `tools/turn-tests.sh homepc send|status|fetch` in that repo.
  - rc7 lets another mod's vehicle type plan as its own. Whether our truck finds a drivable prefab is the first thing to check (step P1).

What the agent can't do: the character creator (Peter makes the character), map picks (stops go in by address through the bridge), and the dealer's purchase screen (spawn plus the price taken off the money).

## Rules of the run

- Only in the save `FT playtest` (a fresh character Peter creates) and its copies `FT playtest dN`; never `HART. YT`.
- Take the game PC lock for the whole run and renew it every two hours.
- Other gameplay mods off, except HART. Self Driving.
- Time: whole days at speed 20 with `run_until` for crew-only days. The player's own driving runs at the self-driving mod's 3x, real time, because a time skip cancels a ride.
- Save as `FT playtest dN` at 05:00 every day, and reload once a week, to check that the save round-trips.
- Stop at once and report on any `ERROR: ` line, a vehicle stuck for more than one planned leg, money that doesn't reconcile, stock that appears or vanishes, or a refused action the plan didn't expect.

## The run

**P0. Setup (Peter, about 5 minutes).** Start a new game with a new character, save it as `FT playtest` and quit. Everything after this is the agent's.

**P1. Pre-flight (about 20 minutes).**
- Install the current main (0.10.0) and check that Self Driving is rc7 or later.
- Start with the flag, load `FT playtest` and read `state`.
- Spawn a truck with the debug op on a lane next to the player, take its price ($15,000) off with `money`, enter it and send `ride kind=own` to a building 600 m away.
- Pass: `end` is `Complete: arrived`, with 0 contacts caused.
- If the truck can't plan ("can't drive itself there"), log it as a Self Driving finding. Phase B then falls back to teleporting the truck from stop to stop after the planned drive time, and Phase A runs unchanged.

**Phase A. Owner economy, crew only (week 1, about 1 hour).** This tests money, stock and the crew logic over many days, without driving.
- Day 1:
  - Rent a space at the nearest commissary, and buy one cart and one truck the same way as in P1.
  - Hire two crew per vehicle through the game's own hire flow, by `ui` clicks. Fall back to the bridge's hire if that fails, and log it.
  - Order stock for two foods per vehicle.
  - Set each vehicle's route for Monday to Sunday: three stops by address from `data/zones.json` and the premium spots, using the default hours. Set shifts with the Schedule tab's auto-fill by `ui` click; if that's out of reach, use `crew schedule`.
- Days 2 to 7: `run_until` each next 05:00. At each stop the agent logs:
  - money, `ledger_today` and the mod's day summary line
  - each vehicle's stock, place and open state, and the crew's hours and pay
  - fines and commissary deliveries
- Mid-week, change one day's route (copy and paste a day) and one shift, as a player would.

**Phase B. The player's own truck (week 2, about 3 hours, mostly real time).** Each day:
1. 08:00 in the bay: restock by `vehicle restock` and `transfer`.
2. Teleport the truck from the bay onto the nearest lane, facing with traffic (the "out of the parking space" step), and seat the player.
3. `ride kind=own` to stop 1, with autopark off.
4. On arrival, teleport the truck into a legal kerb spot at the stop with its hatch to the pavement, using `bay_candidates`-style search by address.
5. Open, `back_to_hatch`, and `run_until` the closing time at speed 20 (the player sells).
6. Close and repeat for stops 2 and 3.
7. Drive back, then teleport into the bay.

The crew vehicles keep their week 1 plans meanwhile.

**Phase C. Growth and stress (weeks 3 and 4, about 1 hour, crew only at speed 20).**
- Add a second commissary space, a third vehicle and standing orders.
- Run an overnight stop past midnight, a weekend at a park, and one rainy day if the weather allows.
- Let stock run low on purpose (the low-stock to-do should appear), and leave one vehicle without crew for a day (it should warn, not move).

## The ledger and the checks

`build/playtest/<run>/ledger.csv`, one row per day and vehicle:
- day, vehicle, stops served, minutes open
- units sold per food, revenue
- wages, commissary rent, delivery fees, fines
- stock at the start and end, deliveries
- money before and after, and the game's own day total

Checks, each day:
- **Money:** the money change equals revenue minus wages, rent, fees, fines and purchases, to the cent (the game's other costs, such as the apartment and taxes, are read from its own transactions).
- **Stock:** start plus deliveries minus sold equals end, per food and vehicle.
- **Moves:** every planned leg starts and ends, every vehicle ends the day in its bay or at a planned overnight stop, and no owed drive back is left past its shift.
- **Log:** `tools/check-log` with the 0.10.0 rules, no `ERROR: ` lines, and each Harmony patch's `Active` line present.
- **Balance:** the week's totals per vehicle sit within ±25% of `tools/balance` for the same stops, foods and shifts. A bigger gap is reported, not failed.

At the end, `build/playtest/<run>/report.md`: what was played, a table of daily profit per vehicle, every finding with its log lines and a game-view shot, and the balance comparison.

## Build before the run (no game needed)

1. `tools/playthrough`: a Python runner on `ftlib`. It does the scripted day loop, the daily save, the ledger, the checks and the report, and calls `turn-tests.sh` for rides. The agent handles judgement and recovery. Tested against `tools/mock-bridge`.
2. Bridge additions:
   - `vehicle buy`: debug spawn plus the dealer's price as a real transaction, so the ledger sees it.
   - `day_summary`: the mod's day totals per vehicle as data, not log text.
   - `teleport` to `lane:<vehicle>` (nearest lane facing with traffic) and to `kerb:<address>` (a legal kerb spot with the hatch to the pavement).
3. A Self Driving ride wrapper in the runner: send `ride`, then poll `ride-results.jsonl` until a line with a new `end` appears, with the rig's 20-minute timeout.

Order: the two bridge pieces with the runner first (Phase A can run without driving), the ride wrapper and the lane and kerb teleports second.

Status, 17 October 2026: Phase A's runner and its bridge pieces are built and tested against the mock only (`tools/playthrough`, `vehicle buy`, `day_summary`, and a `route` command for stops by address, which Phase A needed; see docs/test-bridge.md, "Playthrough runner"). Two shortcuts differ from the plan above: crews are hired with the `hire` debug action rather than the game's hire flow, and the licence comes from the `licence` debug action, free. The ride wrapper and the lane and kerb teleports are not built. Peter's later decisions (17 October): Phase A starts with one cart on the character's own money, with an optional growth step that buys a truck when money less a reserve covers it; the books check compares only the mod's own lines; and the AI tester plays blind through `tools/play` while `tools/playthrough watch` referees.

## Open questions for Peter

- Truck price taken off by hand ($15,000 at General US Trucks), or should the agent go to the dealer by `ui` clicks and buy for real? Recommended: by hand, since the dealer is the game's code, not ours.
- Run length: two weeks (Phases A and B) first, then decide on C? Recommended.

## Next, from Peter during the first blind run (17 October 2026)

Built on branch `blind1-fixes` (17 October 2026), not yet run in game: no notice per route update, `tools/play walk` and `tools/play map` (bridge `walk` and `map`, docs/test-bridge.md), and the route to-do's cause (merged before).

- **Notifications:** don't post a notice for every route update of every street food vehicle. Crew route progress belongs in the log and the vehicle page, not in the notice stream.
- **Walking for the tester:** a `walk` control that sends the player to a point and lets the character's own navigation take it there (the game's click-to-move, no WASD). This also covers pushing a cart into a bay, and real walking to buildings.
- **The map for the tester:** the map's highlights given to the tester as data, filtered the way a player filters the map (business types, commissaries, free buildings for rent and so on). With walking, this lets a blind run rent a home and live a normal game day around the business.
- **Route warning text (agreed, after the first blind run):** the "route problem" to-do says "check the stops' times" for every problem. In the first run all three carts had it because parts of their route days had nobody on shift: days off with the route still running, and a shift starting at 10:00 while the restock and the 52-minute walk start the route day at 09:38. The to-do should name the real cause per problem (stops' times, an unreachable stop, or nobody on shift on which days and from when), and `tools/play look` should show that text, not only the kind.
