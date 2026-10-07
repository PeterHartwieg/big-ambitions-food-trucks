# Milestone 0 checks

Spike build `0.0.1-spike`. It answers the seven questions in the plan's Milestone 0. It is not a playable mod yet.

## 1. Automated

- [ ] `dotnet test` passes.
- [ ] `tools/build-mod.sh --generate` finishes without clicks. `build/FoodTrucks.sha256` lists `FoodTrucks.dll`, `Dependencies/0Harmony.dll`, `Dependencies/FoodTrucks.Core.dll`, `AssetBundles/Windows/foodtrucks.unity3d` and `Locales/en.json`.
- [ ] `tools/install-remote.sh` installs to `ModsLocal\FoodTrucks` on the game PC, with the game closed.

## 2. In game (about 20 minutes)

Before you start:

- Use a new, throwaway save. Never load the series save while this mod is enabled.
- Disable the other gameplay mods (Rival Balance, Campaign Objectives) so the log only has this mod in it.
- Launch with `-console` in Steam's launch options if you want the backtick console. The hotkeys work without it.

Hotkeys: hold Ctrl and press F6 (spawn test truck), F7 (spawn test cart), F8 ($10 test sale), F9 (test customer on and off), F10 (status).

| # | Do | Expect | Answers spike |
|---|---|---|---|
| 1 | Main menu, Mods | "Food Trucks" listed and enabled | 1 |
| 2 | Start a new game, get to the street | No error popup | 1 |
| 3 | Open any vehicle dealer's buy dialog | "Food truck (test)" and "Food cart (test)" listed at $1, next to the normal stock | 1 |
| 4 | Buy the test truck, drive it 100 m, park | It drives like the example car | 1 |
| 5 | Ctrl+F7 next to a pavement | A red box cart appears | 2 |
| 6 | Click the cart | You grab it with both hands on the handle | 2 |
| 7 | Walk 50 m, then run | The cart follows at your speed | 2 |
| 8 | Park it (the release key in the item panel), grab it again | Release and grab both work | 2 |
| 9 | Ctrl+F8 three times | Money goes up by $10 each time; Econoview transactions show three "Street food" entries | 6 |
| 10 | Sleep or skip to the next day, open Econoview taxes | Projected income includes the $30 (tax is 10% of it on default settings) | 4 |
| 11 | Ctrl+F9 on a pavement | A person walks to about 2 m in front of you and stays standing; Ctrl+F9 again removes them | 5 |
| 12 | Ctrl+F10 | Status shows "data Fresh" or "Loaded", save count, street food $30 | 3 |
| 13 | Save, quit to menu, load the save, Ctrl+F10 | Save count is one higher, street food still $30, truck and cart still there | 3 |
| 14 | Load a save made before this mod was installed | Loads normally; Ctrl+F10 shows "data Fresh" | 3 |
| 15 | Quit. Disable Food Trucks in Mods. Load the save from step 13 | Note exactly what happens: loads or not, truck and cart gone or present, any popup | 7 |
| 16 | Re-enable the mod, load the step 13 save again | Note whether the vehicles and the $30 are back | 7 |
| 17 | Optional, only in the throwaway save: grab the cart, save while holding it, quit, disable the mod, load | Note what happens; this is the worst case for uninstalling | 7 |

Then send me `Player.log` (or just say "done", and I'll fetch it over ssh).

## 3. Log lines

`Player.log` must contain, in this order at startup:

```
[Food Trucks] Version 0.0.1-spike loading.
[Food Trucks] Vehicle registered: foodtrucks:vehicletype_spiketruck (hand vehicle tag: False, price 1).
[Food Trucks] Vehicle registered: foodtrucks:vehicletype_spikecart (hand vehicle tag: True, price 1).
[Food Trucks] Harmony 2.4.2.0.
[Food Trucks] Patch TaxCalculationHelper.GetBusinessIncome: Active.
[Food Trucks] Patch TaxCalculationHelper.GetBusinessSales: Active.
[Food Trucks] Patch TaxCalculationHelper.GetBusinessIncomeTotal: Active.
[Food Trucks] Patch VehicleContractSettings.SetListOfVehiclesForSale: Active.
```

After loading a save: one `Save data Fresh|Loaded ...` line, one `Debug actions ready` line, and one `Vehicle dealer: contact ...` line per dealer. After each save: `Save data written: save count N.`
