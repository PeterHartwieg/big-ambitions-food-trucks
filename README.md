# Food Trucks for Big Ambitions

A mod that adds street food to Big Ambitions: push a cart or drive a truck to a legal spot, open the window and sell to the lunch crowd, then move on before the evening rush starts somewhere else.

**Status, 7 October 2026:** Milestone 0. This build only tests whether the parts the mod depends on work in the game. It is not playable yet. The design is in [docs/design.md](docs/design.md) and the build order in [docs/implementation-plan.md](docs/implementation-plan.md).

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

## Building

Needs Unity 2022.3.62f2 with Windows Build Support (Mono), the official [Big Ambitions modding SDK](https://github.com/hovgaardgames/bigambitions) with its game DLLs imported, and the .NET SDK.

```bash
dotnet test
tools/build-mod.sh --generate
tools/install-remote.sh <ssh host of the Windows game PC>
```

`tools/park-remote.sh park|restore` moves the installed mod out of `ModsLocal` and back, and `tools/fetch-log-remote.sh <label>` copies the game's log; both are for the in-game checks in [CHECKS.md](CHECKS.md).

`build-mod.sh` links `unity/FoodTrucks` into the SDK's `Assets/Mods` and `unity/BuildEditor` into its `Assets/Editor`, generates the vehicle assets, and runs the SDK's own packager. Harmony 2.4.2 (MIT) is downloaded from NuGet with a pinned checksum and shipped in `Dependencies/`.

## Licence

MIT, see [LICENSE.txt](LICENSE.txt). The placeholder test truck reuses the SDK's example car, which Hovgaard Games publishes under MIT. Harmony is MIT, by Andreas Pardeike.
