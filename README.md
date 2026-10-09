# Food Trucks for Big Ambitions

A mod that adds street food to Big Ambitions: push a cart or drive a truck to a legal spot, open the window and sell to the lunch crowd, then move on before the evening rush starts somewhere else.

**Status, 9 October 2026:** Milestone 6 (depth), build `0.7.0`, half built and not yet checked in the game: health grades with inspections and a Clean button, regulars per pitch and weekday, competition between your own vehicles, and live crowds from nearby venues (open or closed, theatre and cinema let-outs, club closing time, office staff). Milestone 5 (hires), build `0.6.0`, also unchecked, lets the game's own employees run a cart or truck: put an unassigned employee on the vehicle from its "Hires" button, add stops with opening hours, and the crew drives to each stop, opens, closes and moves on, paid by the game's own payroll. Milestone 4 (`0.5.0`, also unchecked) brought the food truck with its final model, the vendor licence and permits by phone from the City of New York, and the "Street food" map filters. Milestone 3 (`0.4.0`) brought customers as people with a queue; Milestone 2 (`0.3.1`) crowds that add up, the game's own equipment, stock limits, legal pavement spots and prices like a shop's; see [docs/implementation-plan.md](docs/implementation-plan.md). Balance is set by calculation: [docs/balance/cart-economics.md](docs/balance/cart-economics.md). Milestone 1 was played once; results below.

## Removing the mod

A save that still holds a Food Trucks vehicle breaks without the mod: a held cart leaves you somewhere else with no way to interact with the world, and once a tax bill is overdue the game's forced payment throws on the unknown vehicle. The game can't sell a pushed cart, so the mod does it:

1. Load every save that has a Food Trucks vehicle, let go of the cart and step out of the truck.
2. Press **Ctrl+Shift+F12** twice within 10 seconds (or run `foodtrucks_uninstall` twice in the console). Every Food Trucks vehicle is removed and refunded at its purchase price plus the stock still in it, and any pending delivery of one is cancelled. Crew members go back to being unassigned employees.
3. Wait for "Removed … vehicles, refunded …". Save, then remove the mod. If the message says something is still in the save or a refund was not paid, keep the mod installed and check `Player.log`.

Street food sales, licence and permit fees stay in that save's history, labelled with raw keys (`foodtrucks:transaction_sale`, `foodtrucks:transaction_licence`, `foodtrucks:transaction_permit`, and `foodtrucks:transaction_cityfees_label` on the tax statement) once the mod is gone, and the City of New York's texts about the licence show their raw `foodtrucks:` keys. The mod's own data stays in the save and does nothing without the mod.

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

Milestone 1 fixes for these, played on 8 October 2026 (session report in [docs/evidence/milestone-1-session-1.md](docs/evidence/milestone-1-session-1.md)):

| Finding | Fix | Status |
|---|---|---|
| Sleep throws on the test truck | The mod copies a vanilla car's sleep setup onto the truck at city load | Checked: sleeping in the truck works |
| Truck storage won't open | None needed: vanilla cars load by clicking them with a box in hand. The truck now lists its cargo | Checked: a box of hot dogs went into the truck by click |
| Cart distorted fingers | The mod copies the vanilla hand truck's grip rotations | Not fixed: the hands hang below the handle. The vanilla grip is about 1.16 m high and 0.65 m ahead of the player (logged); the cart's handle is 0.95 m high and 0.35 m ahead |
| Cart slides sideways | The cart's origin is its axle, the pusher 1.15 m behind | Checked, partly: the pivot feels much more natural. Turning without sliding not confirmed step by step |
| No hover highlight, offset icon | Vanilla overlay types; origin at the cart's centre | Icon checked: it sits on the released cart. Hover outline pending. The icon is still missing after a reload, as for the vanilla hand truck |
| Mod vehicles at every dealer | Only General US Trucks lists the cart, in person; delivery is refused | Checked: the cart is listed at $1,200 in person. Delivery refusal pending: a new game has no warehouse to deliver to |
| A save with mod vehicles breaks without the mod | Ctrl+Shift+F12 twice removes and refunds every Food Trucks vehicle | Checked: both vehicles removed and refunded ($3,279.60); the save then loads without the mod with no exceptions, and walking and interaction work. The forced tax payment without the mod is unverified: the console didn't open |

Milestone 1 selling, checked in the same session: loading by click and at the shelves, Open here, about one sale every 5 in-game minutes at $11.65 a hot dog, "Street food sale: Hotdog" in Econoview, the pause message when walking away, the close summary, an open cart surviving save and load, the tax fixture and the refusal of a second tax statement, and "data Fresh" on a save made without the mod.

Open from the session:

- One box of hot dogs holds 500 units, so six slots last days. The cart needs its own unit limit.
- The game's day summary shows $0 profit: it counts only businesses in rented buildings.
- Driving past the open cart in a vehicle within 8 m counts as working it. A workstation the player stands at may be better.
- F6 hid the UI while spawning the test truck; probably another mod's or the game's binding, not confirmed.

## Building

Needs Unity 2022.3.62f2 with Windows Build Support (Mono), the official [Big Ambitions modding SDK](https://github.com/hovgaardgames/bigambitions) with its game DLLs imported, and the .NET SDK.

```bash
dotnet test
tools/build-mod.sh --generate
tools/install-remote.sh <ssh host of the Windows game PC>
```

Debug keys (holding Ctrl): **F5** one box of each of the mod's new foods and one of each new machine into the food vehicle next to you, counted as bought at their own prices so the uninstall refund includes them (throwaway saves only; console `foodtrucks_new_items`), **F6** free food truck, **Shift+F6** a free vendor licence and permits for every food vehicle, **F7** free food cart, **F8** mark a point for drawing a park zone, **F9** the demand model's reading for the cart next to you, **Shift+F9** hire a customer service worker (skill 60) without a recruitment campaign, **F11** log the grip while pushing, **Shift+F7** a vanilla hand truck, **Shift+F8** record $200,000 of street food today without moving money, **F10** status, **Shift+F10** issue the tax statement now (throwaway saves only), **Shift+F12** the uninstall step.

`tools/park-remote.sh park|restore` moves the installed mod out of `ModsLocal` and back, and `tools/fetch-log-remote.sh <label>` copies the game's log; both are for the in-game checks in [CHECKS.md](CHECKS.md).

`build-mod.sh` links `unity/FoodTrucks` into the SDK's `Assets/Mods` and `unity/BuildEditor` into its `Assets/Editor`, generates the vehicle and item assets, and runs the SDK's own packager. Harmony 2.4.2 (MIT) is downloaded from NuGet with a pinned checksum and shipped in `Dependencies/`.

## Licence

MIT, see [LICENSE.txt](LICENSE.txt). The food truck is built on the SDK's example car (its physics setup and dirt textures), which Hovgaard Games publishes under MIT. Harmony is MIT, by Andreas Pardeike.
