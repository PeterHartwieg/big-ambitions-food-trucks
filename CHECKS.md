# Milestone 2 checks

Build `0.2.0`, demand and menu: equipment decides the menu, the cart holds 80 items of food, it opens only on a legal spot, and sales follow the demand model. This session is also the first balance check (implementation plan, Milestone 2).

## 1. Automated

- [ ] `dotnet test` passes.
- [ ] `tools/build-mod.sh --generate` finishes without clicks. `build/FoodTrucks.sha256` lists `FoodTrucks.dll`, `Dependencies/0Harmony.dll`, `Dependencies/FoodTrucks.Core.dll`, `AssetBundles/Windows/foodtrucks.unity3d`, `Locales/en.json` and `data/demand.json`, `data/economy.json`, `data/zones.json`.
- [ ] With the game closed: `tools/park-remote.sh restore homepc` (Milestone 1 is parked), then `tools/install-remote.sh homepc`.

## 2. In game (about 40 minutes, two launches)

Rules for the whole session:

- Only touch saves whose names start with **FT**. Never load the series save while this mod is installed.
- Turn off the other gameplay mods (Rival Balance, Campaign Objectives, Delivery Drive).
- Debug keys, holding Ctrl: **F6** test truck, **F7** free food cart, **F9** demand here, **F10** status, **Shift+F12** uninstall step.
- Write down anything odd and carry on. For the balance part, write down what you think, not only what you see.

### Phase A: playing (mod installed)

| # | Do | Expect |
|---|---|---|
| A1 | Main menu, Mods | "Food Trucks" on |
| A2 | New game. Ctrl+F10 | "Food Trucks 0.2.0: data Fresh" |
| A3 | Buy the cart at General US Trucks (or Ctrl+F7 for a free one). Grab it | Both hands on the handle, fingers around the bar |
| A4 | Push it 50 m with turns. Move the mouse over it after letting go | Swings round its wheels, no sideways slide. Outlined on hover |
| A5 | Without equipment, while pushing: **Open here** | "Fit equipment first…" |
| A6 | Buy a hot dog grill (whichever store sells it), carry it out with no vehicle, click the cart | The grill goes into the cart: 1 of 6 slots |
| A7 | Buy a box of hot dogs, carry it out, click the cart | "This vehicle holds 80 items of food. What didn't fit stays where it was." The cart shows 80 hot dogs; the box in your hands still has the rest |
| A8 | Put the box down next to the cart (you can't grab the cart with it in your hands). Push the cart to stand right in front of a building entrance; Open here | "Can't open in front of a building entrance." |
| A9 | Next to a crosswalk; Open here | "Can't open within 3 m of a crosswalk." |
| A10 | In a parking lane at the kerb; Open here | "Can't open in a parking lane…" |
| A10a | Within 3 m of a subway entrance (away from crosswalks and doors); Open here | "Can't open next to a subway entrance." |
| A11 | A clear stretch of pavement; Open here | "Cart open with 80 items, about N sales an hour here right now." Write down N |

### Balance check: three kinds of spot

Do this around lunch (11:00 to 14:30) on a weekday, at three spots of different character. Suggested: offices (Midtown or Lower Manhattan), homes (a residential street), and nightlife or warehouses (Hell's Kitchen or Industry City). Speeding up time is fine.

| # | Do | Write down |
|---|---|---|
| B1 | At spot 1, before opening: Ctrl+F9 | The whole readout (screenshot): areas, neighbourhood, customers an hour, buy %, window, sales an hour |
| B2 | Open, sell for 30 in-game minutes, grab the cart | The close summary: sales and money. Does the number feel right for the place and time? |
| B3 | Same at spot 2 | Readout, summary, your verdict |
| B4 | Same at spot 3 | Readout, summary, your verdict |
| B5 | At one spot, Ctrl+F9 again in the evening (17:00 to 21:00) and late (after 21:00) | Both readouts. Does lunch beat evening at offices, and evening beat lunch at nightlife? |
| B6 | Optional: once some hot dogs have sold (drinks count towards the 80 too), fit a drinks fridge as a second piece, load soda cans, sell for a while | Some customers buy "Hot dog + Soda can" |
| B7 | Optional: rain. If it rains, Ctrl+F9 | Fewer passers than in the dry |

Your verdict matters more than the numbers: too many or too few customers, the wrong places busy, or prices that feel off. That's what the tables get tuned on.

### Loading and saves

| # | Do | Expect |
|---|---|---|
| R1 | With the cart below 80 after selling, pick up the leftover box and click the cart | Only the gap goes in; the box keeps the rest |
| R2 | Fit a second piece if B6 didn't (a drinks fridge, or any other), then carry a third piece of equipment to the cart and click it | "This vehicle has room for 2 pieces of equipment." It stays in your hands |
| R3 | With the cart below 80, push it into a wholesale store and click a shelf of hot dogs once. Pay at the register | Only the gap goes into the cart, and you pay only for that. The game may also show its own "vehicle is full" message; that's expected |
| R4 | Save as **FT2 open** with the cart open. Quit to the menu, load it, Ctrl+F10 | Cart open, equipment and stock as before, lifetime sales kept |

Not covered by this session, because a new game has none of them: unloading a hand truck into the cart (hand trucks come from a rented building), a drinks fridge with drinks inside it, and carts carried over from 0.1.0 saves (with more than 80 items, or open without equipment). The code handles them; they stay unchecked.

### Phase A, end

| # | Do | Expect |
|---|---|---|
| A12 | Let go of the cart. Ctrl+Shift+F12 twice | "Removed … vehicles, refunded …" |
| A13 | Save as **FT2 removed**. Quit the game | |

Tell me "A done"; I copy the log and park the mod.

### Phase B: the mod removed (mod parked)

| # | Do | Expect |
|---|---|---|
| B8 | Load **FT2 removed** | Loads where you saved; walking and interaction work |

Quit and tell me "B done"; I copy the log. The mod stays parked.

## 3. Log lines

At startup, lines containing:

```
Version 0.2.0 loading.
Demand data loaded from ...\data: 0 zones.
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
Patch VehicleInstance.MergeIntoCargo: Active.
Patch VehicleInstance.TryToAddToCargo: Active.
```

After loading a save: `Save data ...`, `Debug actions ready`, `Vanilla hand truck ...`. While playing: `World: N buildings, M entrances cached.` once, one `Cargo limit: ...` line each time a load is capped, one `Open: cart ... road layer below: yes|no` per Open that gets as far as the spot check, A8 onwards (this answers whether the game's road layer tells pavement from road), `Cart ... open on day ...` with the spot and the plan, `Sale: ...` per item, and `Demand here: ...` per Ctrl+F9.
