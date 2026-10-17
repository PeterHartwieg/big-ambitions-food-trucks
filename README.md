# Food Trucks for Big Ambitions

A mod that adds street food to Big Ambitions: push a cart or drive a truck to a legal spot, fit equipment, stock up and serve the crowd yourself or send a crew along a scheduled route.

**Status, 14 October 2026:** development build `0.9.10`, targeting Big Ambitions 1.0. The cart and truck, equipment and menus, demand and visible queues, licences and permits, crew routes, health grades, regulars, competition, catering, venue partnerships, map filters, commissary supply and warehouse logistics are implemented. This is an unreleased development build: Workshop publication has not happened, release review is under way, and the latest changes still need in-game checks. The dedicated Street food page and local Mac validation are being reviewed separately; neither is claimed checked here. See [CHECKS.md](CHECKS.md) for pending checks and [docs/implementation-plan.md](docs/implementation-plan.md) for development history.

Demand depends on the spot, time, nearby businesses, weather and season. Equipment determines both the menu and food capacity; crews use the game's employees and payroll. Street food has its own save data, income and expense breakdown, and income-tax integration. Balance is calculated and still subject to playtesting; the latest study is [docs/balance/balance-pass-2026-10-14.md](docs/balance/balance-pass-2026-10-14.md).

## Installing a development build

There is no published Workshop release yet. Build from this repository using [Building](#building), or use a complete development package supplied for testing. With the game closed, place the entire `FoodTrucks` package folder in the game's `ModsLocal` folder, preserving its asset bundles, data, locales and dependencies. A source checkout or the DLL alone is not an installable package. Restart the game after installing or replacing it. To remove it from saves, follow [Removing the mod](#removing-the-mod) first.

## Getting started

1. Complete the game's **Food Safety** course. Call the **City of New York** to buy a vendor licence.
2. Buy a food cart or food truck in person at **General US Trucks**. Call the city again for that vehicle's permit.
3. Fit equipment by carrying it to the vehicle and loading it into cargo, then load matching food. For example, a Hot Dog Grill unlocks hot dogs. Each fitted piece has its own food capacity; excess stock stays in the box.
4. Park on a legal spot, step out of the truck or let go of the cart, and click the vehicle. **Open for business** opens it, or says which rule stops it. Stay at the cart to serve; a truck places you at its hatch. **Close** in the same menu closes it, for a cart too, without moving it. Set your own prices on the vehicle's BizMan page (Street food); otherwise district pricing suggestions or standard prices apply.
5. Visit a commissary's marked loading bay to unlock its phone contact. Call it and choose **Spaces and standing orders**, then **Spaces**, to rent a space for a vehicle (or move or give one up). With a space there, the call's **Order stock** orders foods and machines for the next morning; deliveries are paid on arrival, and phone orders share a weekly food limit across the fleet. In any commissary's bay, the vehicle's panel has **Commissary stock**: **Fill up** loads food, **Take** loads one item by name (taking a piece of equipment fits it), and **Return** gives food or a piece back, a piece taking the food that no longer fits without it. Daily or weekly standing orders and warehouse logistics can supply a larger operation.
6. The vehicle's BizMan page puts unassigned employees on it and builds its route, by map pick or with **Add the spot where it stands**. A crew with a schedule can restock at its rented commissary bay, travel and serve without you. A vehicle is cleaned only in the bay where it rents its space (**Clean** in its on-foot menu), and vehicles left out at 04:00 can receive a commissary fine. Catering offers arrive through **Street food bookings** as the business grows.

## Validation history

The current review candidate, automated results, local Mac session limitations and remaining release gates are recorded in the [14 October audit handoff](evidence/autonomous-audit-2026-10-14.md).

The evidence below records specific historical builds and sessions, with their original checked, pending and unverified results. It is not a claim that every current feature or later fix has been checked. Current outstanding checks are in [CHECKS.md](CHECKS.md).

## Removing the mod

A save that still holds a Food Trucks vehicle breaks without the mod: a held cart leaves you somewhere else with no way to interact with the world, and once a tax bill is overdue the game's forced payment throws on the unknown vehicle. The game can't sell a pushed cart, so the mod does it:

1. Load every save that has a Food Trucks vehicle, let go of the cart and step out of the truck.
2. Press **Ctrl+Shift+F11** twice within 10 seconds (or run `foodtrucks_uninstall` twice in the console). Every Food Trucks vehicle is removed and refunded at its purchase price plus the stock still in it, and any pending delivery of one is cancelled. Crew members go back to being unassigned employees, and what crews sold that hasn't been paid in yet (it comes in after midnight) is paid in at once. Commissary spaces are given up: the stock waiting at commissaries is paid back at its goods price, orders not yet delivered are cancelled (they are paid on delivery, so there is nothing to pay back), rent and overnight fines still owed are paid if you have the money and dropped if not, and the commissary contacts are removed. Recruitment campaigns for the street food fleet end, and the price of the candidates not yet found is paid back; without this step, a save with a fleet campaign still running plays on, but the agency's "manage campaigns" list can't open until that campaign ends.
3. Wait for "Removed … vehicles, refunded …". Save, then remove the mod. If the message says something is still in the save or a refund was not paid, keep the mod installed and check `Player.log`.

Street food sales, licence and permit fees, commissary rent, orders and overnight fines stay in that save's history, labelled with raw keys (`foodtrucks:transaction_sale`, `foodtrucks:transaction_licence`, `foodtrucks:transaction_permit`, `foodtrucks:transaction_commissary_rent` and so on, and `foodtrucks:transaction_cityfees_label` and `foodtrucks:transaction_commissary_label` on the tax statement) once the mod is gone, and the City of New York's texts about the licence and overnight fines show their raw `foodtrucks:` keys. The mod's own data stays in the save and does nothing without the mod.

## Evidence

Each question the mod depends on, and how sure we are. "Checked" means seen in the game. Reading the game's code counts as unverified. Milestone 0 was played on 7 October 2026 with the 1.0 game (build 25482473); the full session report is in [docs/evidence/milestone-0-session-1.md](docs/evidence/milestone-0-session-1.md).

| # | Question | Status | Notes |
|---|---|---|---|
| 1 | Can the official SDK build a mod vehicle on a Mac in batch mode, and can a player buy it? | Checked: yes | `tools/build-mod.sh` builds without clicks. A Harmony postfix on `VehicleContractSettings.SetListOfVehiclesForSale` lists both test vehicles at General US Trucks (1 7th Avenue), in the order list; buying works with delivery unticked. One purchase at a time until the vehicle is picked up. |
| 2 | Can a modded vehicle be pushed like the hand truck? | Checked: yes, rough | Grab, walk, run, release and grab again all work with a `HandTruck` prefab built from primitives. The cart slides sideways to stay in front, the fingers are distorted, there's no hover highlight before pickup, and the interaction icon is offset. |
| 3 | Does mod state survive save, quit and load in `GameInstance.modData`? | Checked: yes | Save count and a $200,040 ledger came back after quitting the game. Data also survived a save written while the mod was removed (zero ledger tested). |
| 4 | Can the mod add its sales to income tax? | Checked: yes | The game's own tax statement listed "Street food $200,040.00" and taxed it; Econoview's projection matched the mod's status readout to the cent. |
| 5 | Can the mod spawn a person who walks to a point and stands there? | Checked: yes | The pedestrian prefab walked 8.2 m on the NavMesh, stood, and was removed on request. |
| 6 | Do mod payments show in the transaction history with their own label? | Checked: yes | "Street food sale: test sale". With the mod removed, Econoview shows the raw key `foodtrucks:transaction_sale`. |
| 7 | What happens to a save with mod vehicles after the mod is removed? | Checked: unsafe | Parked mod vehicles disappear but the save plays; Persona shows broken wealth figures. A save made while holding the mod cart loads to a different place with no world interaction. Reinstalling the mod restores the vehicles, even after a save without it. The mod needs an uninstall step that sells or stores its vehicles first. |

Also found in the session: clicking Sleep in a mod vehicle's item panel throws `SleepEnvironment config is not assigned` (the prefabs carry an empty sleep setup), and the test truck's storage can't be opened although it shows 0/20.

Park premium pitches (14 October 2026): **partly checked**. Two premium address anchors use observed park/entrance positions. Live bridge checks passed parking and hatch rules at Lower Manhattan and Hell's Kitchen; Premium claims, visible queues and map presentation remain pending; Industry City's proposed premium anchor is deferred after failed truck placement checks. [Selection, calculator results and live checks](docs/balance/park-premium-pitches.md).

Milestone 1 fixes for these, played on 8 October 2026 (session report in [docs/evidence/milestone-1-session-1.md](docs/evidence/milestone-1-session-1.md)):

| Finding | Fix | Status |
|---|---|---|
| Sleep throws on the test truck | The mod copies a vanilla car's sleep setup onto the truck at city load | Checked: sleeping in the truck works |
| Truck storage won't open | None needed: vanilla cars load by clicking them with a box in hand. The truck now lists its cargo | Checked: a box of hot dogs went into the truck by click |
| Cart distorted fingers | The mod copies the vanilla hand truck's grip rotations | Not fixed: the hands hang below the handle. The vanilla grip is about 1.16 m high and 0.65 m ahead of the player (logged); the cart's handle is 0.95 m high and 0.35 m ahead |
| Cart slides sideways | The cart's origin is its axle, the pusher 1.15 m behind | Checked, partly: the pivot feels much more natural. Turning without sliding not confirmed step by step |
| No hover highlight, offset icon | Vanilla overlay types; origin at the cart's centre | Icon checked: it sits on the released cart. Hover outline pending. The icon is still missing after a reload, as for the vanilla hand truck |
| Mod vehicles at every dealer | Only General US Trucks lists the cart, in person; delivery is refused | Checked: the cart is listed at $1,200 in person. Delivery refusal pending: a new game has no warehouse to deliver to |
| A save with mod vehicles breaks without the mod | Ctrl+Shift+F12 (Ctrl+Shift+F11 since 0.9.6) twice removes and refunds every Food Trucks vehicle | Checked: both vehicles removed and refunded ($3,279.60); the save then loads without the mod with no exceptions, and walking and interaction work. The forced tax payment without the mod is unverified: the console didn't open |

Milestone 1 selling, checked in the same session: loading by click and at the shelves, Open here, about one sale every 5 in-game minutes at $11.65 a hot dog, "Street food sale: Hotdog" in Econoview, the pause message when walking away, the close summary, an open cart surviving save and load, the tax fixture and the refusal of a second tax statement, and "data Fresh" on a save made without the mod.

Open from the session:

- One box of hot dogs holds 500 units, so six slots last days. The cart needs its own unit limit. *Done:* Milestone 2 gave each vehicle one shared limit (cart 80, truck 160 units). Since the storage-per-piece build (0.9.7, pending: not yet seen in game) each fitted piece of equipment holds its own foods instead: 40 units a piece on a cart, 80 on a truck, a coffee counting a quarter and a pastry half. A piece serving two foods shares its room, the Extra Storage cabinet doubles every piece, and food with no piece to hold it doesn't load. The vehicle's cargo slots show each piece's load (62/80) with a tooltip.
- The game's day summary shows $0 profit: it counts only businesses in rented buildings.
- Driving past the open cart in a vehicle within 8 m counts as working it. A workstation the player stands at may be better.
- F6 hid the UI while spawning the test truck. It was the Camera Tools mod's Hide UI hotkey, which can be F4, F6 to F10, Home, Insert, Delete, Page Up or Down or a number key and ignores Ctrl. The game's own F7 (hide the HUD) and F8 (free camera) also ignore Ctrl but work only when the game runs with `-console`. Since 0.9.6 the debug keys avoid all of these (pending: not yet seen in game).

## Building

Needs Unity 2022.3.62f2 with Windows Build Support (Mono), the official [Big Ambitions modding SDK](https://github.com/hovgaardgames/bigambitions) with its game DLLs imported, and the .NET SDK.

```bash
dotnet test
tools/build-mod.sh --generate
tools/install-remote.sh <ssh host of the Windows game PC>
```

Developer debug keys require starting the game with `-foodtrucks-test`; without it, developer hotkeys and console commands are disabled. The safe uninstall step remains available on Ctrl+Shift+F11 and `foodtrucks_uninstall`, with its existing second confirmation.

Debug keys (holding Ctrl; "Alt" means Ctrl+Alt): **K** free food truck, **Alt+K** a free vendor licence and permits for every food vehicle, **J** free food cart, **Alt+J** a vanilla hand truck, **U** one box of each of the mod's new foods and one of each new machine into the food vehicle next to you, counted as bought at their own prices so the uninstall refund includes them (throwaway saves only; console `foodtrucks_new_items`), **Alt+U** hire a customer service worker (skill 60) without a recruitment campaign, **L** status, **Alt+L** the demand model's reading for the cart next to you, **G** log the grip while pushing, **Alt+G** mark a point for drawing a park zone, **V** a city event at the nearest event place, **Alt+V** a catering gig at the nearest place, **X** issue the tax statement now, **Alt+X** record $200,000 of street food today without moving money (throwaway saves only; V, Alt+V, X and Alt+X run only on a second press within 10 seconds), **F3** commissary bays, **Alt+F3** commissary deliveries now, **Shift+F3** the overnight check as a dry run, **Shift+F11** the uninstall step. The letters are ones the game doesn't bind (its bindings ignore Ctrl), in the same place on QWERTY and QWERTZ; Ctrl+Shift with a letter is left to other mods, and F1, F2, F4 to F10 and F12 to the game and Camera Tools.

`tools/park-remote.sh park|restore` moves the installed mod out of `ModsLocal` and back, and `tools/fetch-log-remote.sh <label>` copies the game's log; both are for the in-game checks in [CHECKS.md](CHECKS.md).

For local macOS playtests, close Big Ambitions and run `python3 tools/install-local-mac.py` after building. The package includes Windows and Mac bundles. The installer preserves other mods and backs up the previous Food Trucks package. See [local testing](docs/test-bridge.md#local-macos-testing) for launch and bridge instructions (in-game Mac verification pending).

To prepare a local release-candidate archive after building, run `python3 tools/package-release.py`. It verifies `build/FoodTrucks.sha256`, the exact runtime file set, both platform bundles and required license notices, then writes `build/FoodTrucks-0.9.10-rc.zip` with one `FoodTrucks/` folder. It refuses an existing output; use `--output` for another candidate. This prepares a local artifact without uploading or publishing it. The version remains unreleased. See [release packaging](docs/test-bridge.md#local-release-candidate-packaging).

`build-mod.sh` links `unity/FoodTrucks` into the SDK's `Assets/Mods` and `unity/BuildEditor` into its `Assets/Editor`, generates the vehicle and item assets, and runs the SDK's own packager. Harmony 2.4.2 (MIT) is downloaded from NuGet with a pinned checksum and shipped in `Dependencies/`.

## Licence

MIT, see [LICENSE.txt](LICENSE.txt). The food truck is built on the SDK's example car (its physics setup and dirt textures), which Hovgaard Games publishes under MIT. Harmony is MIT, by Andreas Pardeike.
