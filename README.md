# Food Trucks for Big Ambitions

A mod that adds street food to Big Ambitions: push a cart or drive a truck to a legal spot, open the window and sell to the lunch crowd, then move on before the evening rush starts somewhere else.

**Status, 7 October 2026:** Milestone 0. This build only tests whether the parts the mod depends on work in the game. It is not playable yet. The design is in [docs/design.md](docs/design.md) and the build order in [docs/implementation-plan.md](docs/implementation-plan.md).

## Evidence

Each question the mod depends on, and how sure we are. "Checked" means seen in the game. Reading the game's code counts as unverified.

| # | Question | Status | Notes |
|---|---|---|---|
| 1 | Can the official SDK build a mod vehicle on a Mac in batch mode, and can a player buy it? | Pending | Builds without clicks on the Mac (`tools/build-mod.sh`). Registering a vehicle type does not put it on sale anywhere; a Harmony postfix on `VehicleContractSettings.SetListOfVehiclesForSale` adds it to dealers. Not yet seen in game. |
| 2 | Can a modded vehicle be pushed like the hand truck? | Pending | Needs a `HandTruck` component, `spawnInPlayerObject`, and the `ba:vehicletag_ishandvehicle` tag. The test cart is built from primitives by `unity/BuildEditor/CartAssets.cs`. |
| 3 | Does mod state survive save, quit and load in `GameInstance.modData`? | Pending | The game serializes the whole `GameInstance`, and `GlobalEvents.onSaveGame` runs before it does. The state is versioned JSON (`Core/Saves`). |
| 4 | Can the mod add its sales to income tax? | Pending | Postfixes on `TaxCalculationHelper.GetBusinessIncome`, `GetBusinessSales` and `GetBusinessIncomeTotal` add the mod's daily sales ledger. |
| 5 | Can the mod spawn a person who walks to a point and stands there? | Pending | Uses the game's `ThirdPersonCharacterPool`, as for passengers leaving cars. |
| 6 | Do mod payments show in the transaction history with their own label? | Pending | Transaction types are strings; the label comes from the mod's locale file. |
| 7 | What happens to a save with mod vehicles after the mod is removed? | Pending | From the code: the vehicle record stays in the save and fails to spawn, with a logged exception. Loading while holding a mod cart may break player positioning. |

## Building

Needs Unity 2022.3.62f2 with Windows Build Support (Mono), the official [Big Ambitions modding SDK](https://github.com/hovgaardgames/bigambitions) with its game DLLs imported, and the .NET SDK.

```bash
dotnet test
tools/build-mod.sh --generate
tools/install-remote.sh <ssh host of the Windows game PC>
```

`build-mod.sh` links `unity/FoodTrucks` into the SDK's `Assets/Mods` and `unity/BuildEditor` into its `Assets/Editor`, generates the vehicle assets, and runs the SDK's own packager. Harmony 2.4.2 (MIT) is downloaded from NuGet with a pinned checksum and shipped in `Dependencies/`.

## Licence

MIT, see [LICENSE.txt](LICENSE.txt). The placeholder test truck reuses the SDK's example car, which Hovgaard Games publishes under MIT. Harmony is MIT, by Andreas Pardeike.
