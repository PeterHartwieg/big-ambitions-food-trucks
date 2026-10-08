# Milestone 1 checks

Build `0.1.0`, the ugly cart: buy a cart, load food at a wholesaler, open on the street, sell at a fixed rate. Plus the fixes carried over from Milestone 0.

## 1. Automated

- [ ] `dotnet test` passes.
- [ ] `tools/build-mod.sh --generate` finishes without clicks. `build/FoodTrucks.sha256` lists `FoodTrucks.dll`, `Dependencies/0Harmony.dll`, `Dependencies/FoodTrucks.Core.dll`, `AssetBundles/Windows/foodtrucks.unity3d` and `Locales/en.json`.
- [ ] With the game closed, delete the parked spike build (`...\Big Ambitions\FoodTrucksParked\FoodTrucks`; it can be rebuilt from git), then `tools/install-remote.sh homepc`. `ModsLocal\FoodTrucks` holds the new build and `FoodTrucksParked` is empty, so `tools/park-remote.sh park` works later.

## 2. In game (about 20 minutes, three launches)

Rules for the whole session:

- Only touch saves whose names start with **FT**. Never load the series save while this mod is installed. Use **Save as** with the names below.
- Turn off the other gameplay mods (Rival Balance, Campaign Objectives, Delivery Drive) so the log only has this mod in it.
- The old spike saves (FT baseline, FT parked, FT holding, FT parked removed) hold spike vehicles that no longer exist. Don't load them; delete them when convenient.
- Debug keys, holding Ctrl: **F6** test truck, **F7** free food cart, **Shift+F8** tax fixture, **F10** status, **Shift+F10** tax statement now, **Shift+F12** uninstall step.
- Phase B needs the game's console: add `-console` to Big Ambitions' launch options in Steam before the session. The backtick key opens it.
- Write down anything odd and carry on with the next step.

### Phase A: playing (mod installed)

| # | Do | Expect |
|---|---|---|
| A1 | Main menu, Mods | "Food Trucks" listed and on |
| A2 | Start a new game. Once you can move, Ctrl+F10 | Status says "data Fresh", save count 0 |
| A3 | Go to General US Trucks, 1 7th Avenue, and open the list of vehicles you can order (in person, not by phone) | "Food cart" at $1,200 next to the normal stock. No "Food truck (test)" |
| A4 | Select the cart, tick delivery, pick any address and confirm | "The food cart can't be delivered…"; no money taken. A new game has no warehouse, so the game will probably untick delivery itself; then skip this, and the delivery refusal stays pending |
| A5 | Buy the cart with delivery unticked | It's outside the dealer |
| A6 | Move the mouse over the cart | The cart is outlined, like a vanilla vehicle |
| A7 | Click it | You walk behind the handle and grab it. Hands and fingers look normal |
| A8 | Push it 50 m, with a few left and right turns | It stays in front of you and swings round its wheels. It no longer slides sideways to stay in front. Let go: the green map icon sits on the cart (the game hides it while you push). Grab it again |
| A9 | Push it to the nearest wholesale store and leave it outside. Inside, with empty hands, take a box of hot dogs, burgers, fries, pizza, coffee, croissants, donuts, cupcakes, ice cream or soda cans and pay. Carry it out and click the cart. Do this twice | Pointing at the cart with the box shows the game's add-to-storage prompt, and the box goes in. Grab the cart: the item panel shows 2/6. If the walk is long: Ctrl+F7 spawns a free cart outside the store, and the next steps can use that one |
| A10 | Outside on a pavement, while pushing the cart: **Open here** in the item panel | The cart is let go where it stands. "Cart open with N items." No sleep panel opens |
| A11 | Wait about 30 in-game minutes next to the cart (speeding up is fine) | About one "Sold … for $… N left." every 5 in-game minutes, and money goes up |
| A12 | Phone, Econoview, transactions | "Street food sale: Hot dog" (or whichever item) for each sale |
| A13 | Walk about 20 m away and wait 10 in-game minutes, then come back | "Nobody at the cart: sales paused…" once; no sales while away; sales resume when you're back |
| A14 | Grab the cart | "Cart closed: N sales, $X." |
| A15 | Still holding it: **Open here** again. Then Ctrl+F10 | Open again. The status shows the cart open, its stock, and the lifetime sales so far |
| A16 | Save as **FT1 open**. Quit to the menu, load **FT1 open**, Ctrl+F10 | Cart open, same lifetime sales. No burst of sales right after loading; sales carry on at the normal rate |
| A17 | Ctrl+F6. Go into a wholesale store with empty hands and no vehicle, take one box from a shelf and pay, so it's in your hands. Click the test truck | The box goes into the truck's storage |
| A18 | Drive the truck to a legal parking spot within about 5 m of the open cart and stay in it. The item panel | Shows the truck's cargo with the box |
| A19 | Still in the truck: Sleep, for 1 hour | The sleep panel opens without an error. No sales while you sleep, and no burst of sales right after you wake; then they carry on at the normal rate. (Sales count only within 8 m of the cart; if none come at all, check the distance first) |
| A20 | Ctrl+Shift+F8, then Ctrl+Shift+F10 | "Tax fixture…", then "Tax statement sent". The IRS message on the phone shows a tax to pay. Don't pay it |
| A21 | Ctrl+Shift+F10 again | "A tax bill is still unpaid; no new statement issued." |
| A22 | Get out of the truck and make sure you're not holding the cart. Ctrl+Shift+F12 | "Remove and refund 2 Food Trucks vehicles and cancel 0 deliveries? Press again within 10 seconds." (3 if you used Ctrl+F7) |
| A23 | Ctrl+Shift+F12 again | "Removed 2 vehicles, refunded $…". Cart and truck gone; money up by the refund |
| A24 | Save as **FT1 removed**. Quit the game | |

Tell me "A done"; I copy the log and park the mod.

### Phase B: the mod removed (mod parked)

| # | Do | Expect |
|---|---|---|
| B1 | Load **FT1 removed** | Loads where you saved, you can walk and interact. Persona and Econoview look normal; street food transactions show the raw key `foodtrucks:transaction_sale`, and the unpaid tax bill is still there |
| B1a | Open the console (backtick) and run `IRSForcePayment 1` | The game's forced tax payment runs without an error in the console. In Milestone 0 this path threw on a save holding a mod vehicle |
| B2 | Start a new game. Once you can move, save as **FT1 plain**. Quit the game | A normal game, no Food Trucks in the Mods menu |

Tell me "B done"; I copy the log and restore the mod.

### Phase C: a save made without the mod (mod installed)

| # | Do | Expect |
|---|---|---|
| C1 | Load **FT1 plain**, Ctrl+F10 | Loads normally. Status says "data Fresh", save count 0 |

Quit the game and tell me "C done"; I copy the log and park the mod again, so no save loads it by accident. The FT1 saves can go after that.

## 3. Log lines

The game prefixes this mod's lines with `[Mod:...]`. At startup, in this order, lines containing:

```
Version 0.1.0 loading.
Vehicle registered: foodtrucks:vehicletype_testtruck (hand vehicle tag: False, price 1).
Vehicle registered: foodtrucks:vehicletype_foodcart (hand vehicle tag: True, price 1200).
Harmony 2.4.2.0.
Patch TaxCalculationHelper.GetBusinessIncome: Active.
Patch TaxCalculationHelper.GetBusinessSales: Active.
Patch TaxCalculationHelper.GetBusinessIncomeTotal: Active.
Patch VehicleContractSettings.SetListOfVehiclesForSale: Active.
Patch VehicleStoreDialog.OnVehicleSettingsSet: Active.
Patch ItemPanelUI.SetVehicle: Active.
Patch ItemPanelUI.SetItemInstance: Active.
Patch ItemPanelUI.SetMiniGameMode: Active.
```

After loading a save: one `Save data Fresh|Loaded|Migrated ...` line, one `Debug actions ready` line, and one `Vanilla hand truck: offset ..., left hand ..., right hand ...` line (the first load only). No `Prefab fixes:` error lines.

While playing: `Open button added to the item panel.` once per city load, `Cart ... open on day ...`, one `Sale: ba:itemname_... for $...` per sale, `Cart ... closed (...)`, `Uninstall: removed ...` per vehicle (and `Uninstall: cancelled ...` if a delivery was pending), and `Save data written: save count N.` after each save.
