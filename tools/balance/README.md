# Balance calculator

Runs the mod's own Core demand model over the city to price a cart day. The findings are in [docs/balance/cart-economics.md](../../docs/balance/cart-economics.md). Nothing here ships with the mod: `tools/build-mod.sh` packages only `unity/FoodTrucks`, and this project is not in `FoodTrucks.sln`.

Revenue and profit here are earned on the day sold, for player and crew alike. In game, all window takings are paid at midnight in one transaction per vehicle/day (17 October 2026); this calculator does not model the bank balance or funds available for restocking. The payment timing changes no sales, cost or profit calculation here.

## Run it

```sh
cd tools/balance
dotnet build
dotnet bin/Debug/net10.0/balance.dll                          # today's data, price at the ceiling
dotnet bin/Debug/net10.0/balance.dll --pricing default         # today's data, default price
dotnet bin/Debug/net10.0/balance.dll --reports ingame          # the in-game case: two offices and a nightclub
```

It prints Markdown tables. `--out FILE` also writes them to a file. A full run takes about five minutes (the `compare` report is most of it); `--reports archetypes,costs` is instant.

| Option | Meaning |
|---|---|
| `--reports LIST` | Any of `archetypes,spots,single,routes,compare,pricing,costs,ingame,premium,pitches,foods,storage,pass,checks` (default: `archetypes,spots,single,compare,pricing,costs`). `premium` lists each district's best pitches, the candidates for premium spots; `pitches` prices `data/pitches.json`: each premium spot's day with and without the spot held, and each event's hours with a booked place against the same hours without the event, less the fee. `compare` is staying all day against the best route over the same 16 hours, per district, and ends with the ladder table; `pricing` is the price sensitivity; `routes` the older fixed lunch/evening routes; `ingame` the spot found in game on 8 October (two offices at 20 and 40 m, a nightclub at 30 m) under the old share model and the additive model (the old-model column uses today's data, so it differs from the study's 0.3.0 column, which used 0.3.0's data), a pure office block, and customers per hour at every real pitch (p25, median, p90 per district). `foods` is the new-foods study ([docs/balance/new-foods.md](../../docs/balance/new-foods.md)): every cart's best day city-wide on a weekday and Saturday in May and July, a weekday in January and at the default price, its cost to start, its best summer park day and its rate at a few pure spots. It needs `--patch new-foods-proposal.json` for the new carts and takes about 20 minutes for all of them. `checks` runs the calculator's own regression checks and throws on a failure (a route must judge the stock a stay ended with, after any restock inside it; about 3 seconds). `storage` is the storage-per-piece study ([docs/balance/storage-per-piece.md](../../docs/balance/storage-per-piece.md)): carts and trucks with and without Extra Storage at their best spot, when the first item runs out, restocks, profit, and the cabinet's payback; with `--patch new-foods-proposal.json` for the grill rows, about 30 seconds. `pass` is the balance pass of 14 October 2026 ([docs/balance/balance-pass-2026-10-14.md](../../docs/balance/balance-pass-2026-10-14.md)): carts and trucks played and crewed at the poor, typical, good and best pitch and at premium spots, a day and a week with rent, wages and the delivery fee; how many vehicles the weekly phone limit (one for the fleet) supplies; the fee against hauling; parks. About 3 minutes |
| `--plan FILE`, `--json-out FILE` | For `--reports playthrough`: the plan `tools/playthrough report` writes (each vehicle's foods, crew size and week of stops by position and hours) and where its JSON goes. Each stop is priced at the city building nearest its position, crews loading once in the bay before the first stop (`RestockMode.None`); revenue, stock cost and units per weekday. Prices of the new foods come from `new-foods-proposal.json`'s game section |
| `--carts LIST` | Carts for `compare` and `foods`: any of `hotdog+cooler,coffee+bakery,truck-like` (default for `compare`: these three), and with the new-foods patch the keys in `Program.cs` (`dumplings`, `kabob`, `burger+kabob`, `hotdog+kabob`, `griddle`, `corn`, `concessions`, `slushie`, `concessions+slushie`, `truck-street` and more; default for `foods`: every cart). `truck-like` is a stand-in for the truck: two at the window, four pieces of 80 units, four items |
| `--no-archetypes` | Leave out the archetype park and subway-entrance pitches (one of each per district, at the middle of its buildings) |
| `--patch FILE` | A JSON merge patch over `data/demand.json` and `data/economy.json`: `{"demand": {...}, "economy": {...}}`, with arrays replaced. The data files are not changed. Use native fields: `economy.products.<item>.stockWeight` and `demand.areas.<area>.appetite`. A `null` removes a key. An optional `game` section adds item prices (`items.<item>.defaultMarketPrice`, `wholesalePrice`, `boxSize`), equipment prices (`equipment.<key>`) and the game's market demand per item and neighbourhood (`market.<item>.<neighbourhood>`, overriding `--market`). Legacy `rules` sections are rejected with a migration message |
| `--service queue\|fluid` | `queue` (default): sales follow the game's queue since 0.4.0, Core's `SalesSimulator` measured per order time and load (`QueueThroughput` in `CartModel.cs`). `fluid`: the old min(customers who buy, window capacity) |
| `--pricing ceiling\|default\|manager\|upper` | `ceiling` (default): reference × the neighbourhood's index, the most every customer class accepts. `default`: the item's default price. `manager`: a pricing manager, reference + (ceiling − reference) × capture. `upper`: the most the upper class accepts |
| `--price-factor N` | With `ceiling`, multiplies the price (0.9 is 10% under). With `manager`, the capture (0 to 1) |
| `--market N` | The game's demand for the cart's items, 0 to 100 (100: no shop sells them in the neighbourhood; 71.5: two shops do) |
| `--restock wholesaler\|hand` | `wholesaler`: refill by pushing the cart to the nearest wholesaler (all six sell the cart's food) and back or on. `hand`: refill on the spot, no walk (a what-if) |
| `--speed M/S` | Pushing speed, default 4 (the player's jog) |
| `--drive-speed M/S` | The player's truck in town, default 8 (a real second is a game minute, so 8 m a game minute). Trucks restock by driving |
| `--deliveries-per-week N`, `--fee-sharing N` | The `pass` report's delivery fee: the flat fee per delivery (`rules.json` `deliveryFeeCents`) × deliveries a week (default 1) ÷ the vehicles sharing them (default 1), charged to a vehicle whose stock comes by phone |
| `--crew-skill N`, `--crew-satisfaction N` | The `pass` report's hire: customer service 60 at satisfaction 85 by default (speed 1.0, $27.78 an hour) |
| `--cart-price N` | For the cost table, default 1200 |
| `--data`, `--city`, `--prices` | Other inputs than `data/`, `city.json`, `game-prices.json` |

Stock loads use Core's `ProductInfo.StockWeight` (coffee and tea 0.25, pastries 0.5, other shipped products 1). Storage is per piece, as in Core's `Storage`: each menu item is held by the one piece that serves it (`EquipmentForProduct`), each piece holds the vehicle's `unitsPerPiece` (doubled with Extra Storage, `Cart(storage: true)`), and a piece serving several items is filled in the proportions the spot sells them. A stay restocks when the first item that had stock runs out (10 October 2026; before, when everything had); a restock keeps the stock on board and fills each piece's free room, and when there is no time for a trip the other pieces sell on until closing. A route goes straight on only while every piece holds at least half its room. Area appetite is applied once by Core's `DemandModel`, including mixed-area spots. The calculator has no separate park appetite or stock-weight rules. The archetype park pitch has the park presence a spot inside one of the game's park areas gets (about 0.70); a waterfront archetype sits beside it.

Who serves (14 October 2026): `Cart.Plan` keeps `CartDemand.Plan`'s capacity for the vehicle's whole staff (economy.json), and selling uses the people at the window as `CartSelling` does: capacity per server × team speed × servers (`Cart(servers, speed)`, default the vehicle's staff at speed 1). A player alone at a truck is one server. Trucks restock by driving (`--drive-speed`), carts by pushing. `RestockMode.None` is a crew that loads in its commissary's bay and sells until everything is out (Core `CrewRestock` loads only in the bay); a `Planner` with `Depots` restocks at the commissaries instead of the wholesalers.

Like `CartDemand.Plan` and `CartSelling.ServeCustomer`, each category offers its stocked item with the highest price conversion, breaking ties by more stock, then ordinal item name. Its accepted weight is the category share times that conversion. The plan carries these `SaleItems` and the `AddOnItem` into selling. The add-on is the best-converting item across add-on categories, with equal conversions keeping the first category encountered in stock; its conversion scales drinks per food buyer.

## Verify shipped balance

Headlines on the additive crowd model with the fourth pass's data (8 October 2026; dry weekday in May, market demand 100, wholesale restocking, the 16-hour day), from `--reports compare`: hot dog grill + cooler $1,975 best route; coffee machine + display case $2,393; truck stand-in $4,123. `--reports ingame` gives the in-game case and the typical-spot table. See section 6 of the study. With storage per piece (10 October 2026, after the first review round's fixes) the same run gives $2,058, $2,202 and $4,130 ([storage-per-piece.md](../../docs/balance/storage-per-piece.md)).

## Files

- `CartModel.cs`: one cart's sales. `Cart.Plan` is `CartDemand.Plan` from `unity/FoodTrucks/Scripts/CartDemand.cs` with the game reads replaced by inputs; it calls Core's `DemandModel`, `PriceCurve`, `ServiceRate` and `SalesRate` unchanged.
- `City.cs`: spots, the classifier and walking times. `Days.cs`: single-spot days and routes.
- `city.json`: the city, from `make_city.py`. Rebuild it with `python3 make_city.py [--save SAVE.hsg]` (needs `~/big-copilot`). The committed file holds the fixed AI businesses and one late-game save's rival businesses.
- `game-prices.json`: item prices, wholesale prices, neighbourhood price indices and equipment prices, from Big Copilot.
- `new-foods-proposal.json`: the proposed data for the eight new foods (`--patch`); see the new-foods study.
- `proposed.json`: the third pass's patch, kept for reference. The fourth pass edited `data/demand.json` directly; applying this patch brings back third-pass values, so run without it.
- `store/`: the small-store yardstick. `extract_payload.py SAVE OUT.json` has Big Copilot read a save (read-only); `node store/store_yardstick.cjs OUT.json` runs Big Copilot's validated Open a store model on it at the default difficulty. Keep the extracted payload out of git: it comes from a personal save.
