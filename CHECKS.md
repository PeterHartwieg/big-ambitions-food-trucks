# Milestone 0 checks

Spike build `0.0.1-spike`. It answers the seven questions in the plan's Milestone 0. It is not a playable mod yet.

## 1. Automated

- [ ] `dotnet test` passes.
- [ ] `tools/build-mod.sh --generate` finishes without clicks. `build/FoodTrucks.sha256` lists `FoodTrucks.dll`, `Dependencies/0Harmony.dll`, `Dependencies/FoodTrucks.Core.dll`, `AssetBundles/Windows/foodtrucks.unity3d` and `Locales/en.json`.
- [ ] `tools/install-remote.sh` installs to `ModsLocal\FoodTrucks` on the game PC with the game closed. Then `tools/park-remote.sh park`, so that phase A starts without the mod.

## 2. In game (about 35 minutes, four launches)

Rules for the whole session:

- Only touch saves whose names start with **FT**. Never load the series save while this mod is installed. Don't let the game autosave over an FT save you still need; use **Save as** with the names below.
- Turn off the other gameplay mods (Rival Balance, Campaign Objectives) so the log only has this mod in it.
- Local mods can't be switched off in the Mods menu. To remove or restore this one, quit the game and tell me ("park" or "restore"), or move the folder yourself: `%USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\FoodTrucks` to `...\Big Ambitions\FoodTrucksParked\` and back.
- The game overwrites `Player.log` at every launch. After each phase, quit the game and tell me, so I can copy the log before the next launch.
- Optional: add `-console` to Steam's launch options for the backtick console. The hotkeys work without it.

Hotkeys, holding Ctrl: **F6** test truck, **F7** test cart, **F8** $10 test sale, **Shift+F8** tax test, **F9** test customer on and off, **F10** status, **Shift+F10** issue the tax statement now (bills the save; FT saves only).

### Phase A: a save without the mod (mod parked)

| # | Do | Expect | Spike |
|---|---|---|---|
| A1 | Start a new game. Sleep until day 2. Save as **FT baseline**. Quit the game | Normal game, no Food Trucks in the Mods menu | 3 |

Tell me "A done"; I copy the log and restore the mod.

### Phase B: the mod (mod installed)

| # | Do | Expect | Spike |
|---|---|---|---|
| B1 | Main menu, Mods | "Food Trucks" listed and on | 1 |
| B2 | Load **FT baseline**, Ctrl+F10 | Status says "data Fresh", save count 0 | 3 |
| B3 | At any vehicle dealer, open the list of vehicles you can order or buy (not the cars on the showroom floor) | "Food truck (test)" and "Food cart (test)" at $1, next to the normal stock. Write down the dealer's name | 1 |
| B4 | Buy the test truck | It appears at the dealer. **If nothing happens**, write that down (buying then counts as failed), close the dialog, leave the building, stand next to a clear road and press Ctrl+F6 | 1 |
| B5 | Drive the truck 100 m and park | It drives like the SDK's example car | 1 |
| B6 | On a pavement, Ctrl+F7 | A red box cart appears in front of you, handle towards you | 2 |
| B7 | Click the cart | You walk to the handle and grab it with both hands; the cart is in front of you | 2 |
| B8 | Walk 50 m, then run | The cart moves with you and stays in front | 2 |
| B9 | Release it (the park button in the item panel), then grab it again | Both work | 2 |
| B10 | Ctrl+F8 three times | Money +$10 each time. Econoview transactions: three "Street food" entries | 6 |
| B11 | Ctrl+Shift+F8 | Status shows taxable sales of at least $200,000, a "Street food" statement row of $200,000 and a projected tax above $0. No money changes (it only writes the mod's tax record) | 4 |
| B12 | Econoview, Taxes | Projected tax is shown (not "under the tax minimum") and matches the status figure | 4 |
| B12a | Ctrl+Shift+F10, then open the IRS message on the phone | A tax statement with a "Street food" income row of $200,030 (the tax test plus today's three test sales) and a tax to pay. Don't pay it. This bills only this FT save | 4 |
| B13 | On a pavement, Ctrl+F9 | A person walks to about 2 m in front of you and stays standing. Ctrl+F9 again removes them | 5 |
| B14 | Release the cart next to the truck. Ctrl+F10. Save as **FT parked**. Quit to menu, load **FT parked**, Ctrl+F10 | Save count went up by one; street food this tax year unchanged; truck and cart where you left them | 3 |
| B15 | Grab the cart. Save as **FT holding**. Quit the game | | 7 |

Tell me "B done"; I copy the log and park the mod.

### Phase C: uninstalled (mod parked)

| # | Do | Expect | Spike |
|---|---|---|---|
| C1 | Load **FT parked** | Write down: loads or not, any popup, truck and cart gone or visible | 7 |
| C2 | Walk around for a minute. Open the Persona app on the phone. Open Econoview | Write down anything broken | 7 |
| C3 | Save as **FT parked removed**. Quit to menu | | 7 |
| C4 | Load **FT holding** | Write down what happens. This is the worst case: holding a mod vehicle when the mod is removed | 7 |

Quit the game. Tell me "C done"; I copy the log and restore the mod.

### Phase D: reinstalled (mod installed)

| # | Do | Expect | Spike |
|---|---|---|---|
| D1 | Load **FT parked**, Ctrl+F10 | Same save count, street food figure, truck and cart as at B14 | 3, 7 |
| D2 | Load **FT parked removed** (skip if C3 failed), Ctrl+F10 | Write down: truck and cart back or gone; street food figure kept or lost | 3, 7 |
| D3 | Load **FT holding** | Loads, holding the cart | 7 |

If any step in phase C or D fails, write down what you saw and carry on with the next step. Quit the game and tell me "D done". Then you can delete the FT saves.

## 3. Log lines

The game prefixes this mod's lines with `[Mod:...]` and the mod's display name. At startup, in this order, lines containing:

```
Version 0.0.1-spike loading.
Vehicle registered: foodtrucks:vehicletype_spiketruck (hand vehicle tag: False, price 1).
Vehicle registered: foodtrucks:vehicletype_spikecart (hand vehicle tag: True, price 1).
Harmony 2.4.2.0.
Patch TaxCalculationHelper.GetBusinessIncome: Active.
Patch TaxCalculationHelper.GetBusinessSales: Active.
Patch TaxCalculationHelper.GetBusinessIncomeTotal: Active.
Patch VehicleContractSettings.SetListOfVehiclesForSale: Active.
```

After loading a save: one `Save data Fresh|Loaded|Migrated ...` line, one `Debug actions ready` line, and one `Vehicle dealer: contact ...` line per dealer. After each save: `Save data written: save count N.`
