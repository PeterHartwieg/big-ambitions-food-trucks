# Balance calculator

Runs the mod's own Core demand model over the city to price a cart day. The findings are in [docs/balance/cart-economics.md](../../docs/balance/cart-economics.md). Nothing here ships with the mod: `tools/build-mod.sh` packages only `unity/FoodTrucks`, and this project is not in `FoodTrucks.sln`.

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
| `--reports LIST` | Any of `archetypes,spots,single,routes,compare,pricing,costs,ingame,premium,pitches` (default: `archetypes,spots,single,compare,pricing,costs`). `premium` lists each district's best pitches, the candidates for premium spots; `pitches` prices `data/pitches.json`: each premium spot's day with and without the spot held, and each event's hours with a booked place against the same hours without the event, less the fee. `compare` is staying all day against the best route over the same 16 hours, per district, and ends with the ladder table; `pricing` is the price sensitivity; `routes` the older fixed lunch/evening routes; `ingame` the spot found in game on 8 October (two offices at 20 and 40 m, a nightclub at 30 m) under the old share model and the additive model (the old-model column uses today's data, so it differs from the study's 0.3.0 column, which used 0.3.0's data), a pure office block, and customers per hour at every real pitch (p25, median, p90 per district) |
| `--carts LIST` | Carts for `compare`: any of `hotdog+cooler,coffee+bakery,truck-like` (default: all three). `truck-like` is a stand-in for the truck: two at the window, 160 units, four items |
| `--no-archetypes` | Leave out the archetype park and subway-entrance pitches (one of each per district, at the middle of its buildings) |
| `--patch FILE` | A JSON merge patch over `data/demand.json` and `data/economy.json`: `{"demand": {...}, "economy": {...}}`, with arrays replaced. The data files are not changed. Use native fields: `economy.products.<item>.stockWeight` and `demand.areas.<area>.appetite`. Legacy `rules` sections are rejected with a migration message |
| `--service queue\|fluid` | `queue` (default): sales follow the game's queue since 0.4.0, Core's `SalesSimulator` measured per order time and load (`QueueThroughput` in `CartModel.cs`). `fluid`: the old min(customers who buy, window capacity) |
| `--pricing ceiling\|default\|manager\|upper` | `ceiling` (default): reference × the neighbourhood's index, the most every customer class accepts. `default`: the item's default price. `manager`: a pricing manager, reference + (ceiling − reference) × capture. `upper`: the most the upper class accepts |
| `--price-factor N` | With `ceiling`, multiplies the price (0.9 is 10% under). With `manager`, the capture (0 to 1) |
| `--market N` | The game's demand for the cart's items, 0 to 100 (100: no shop sells them in the neighbourhood; 71.5: two shops do) |
| `--restock wholesaler\|hand` | `wholesaler`: refill by pushing the cart to the nearest wholesaler (all six sell the cart's food) and back or on. `hand`: refill on the spot, no walk (a what-if) |
| `--speed M/S` | Pushing speed, default 4 (the player's jog) |
| `--cart-price N` | For the cost table, default 1200 |
| `--data`, `--city`, `--prices` | Other inputs than `data/`, `city.json`, `game-prices.json` |

Stock loads use Core's `ProductInfo.StockWeight` (coffee and tea 0.25, pastries 0.5, other shipped products 1). Area appetite is applied once by Core's `DemandModel`, including mixed-area spots. The calculator has no separate park appetite or stock-weight rules.

Like `CartDemand.Plan` and `CartSelling.ServeCustomer`, each category offers its stocked item with the highest price conversion, breaking ties by more stock, then ordinal item name. Its accepted weight is the category share times that conversion. The plan carries these `SaleItems` and the `AddOnItem` into selling. The add-on is the best-converting item across add-on categories, with equal conversions keeping the first category encountered in stock; its conversion scales drinks per food buyer.

## Verify shipped balance

Headlines on the additive crowd model with the fourth pass's data (8 October 2026; dry weekday in May, market demand 100, wholesale restocking, the 16-hour day), from `--reports compare`: hot dog grill + cooler $1,975 best route; coffee machine + display case $2,393; truck stand-in $4,123. `--reports ingame` gives the in-game case and the typical-spot table. See section 6 of the study.

## Files

- `CartModel.cs`: one cart's sales. `Cart.Plan` is `CartDemand.Plan` from `unity/FoodTrucks/Scripts/CartDemand.cs` with the game reads replaced by inputs; it calls Core's `DemandModel`, `PriceCurve`, `ServiceRate` and `SalesRate` unchanged.
- `City.cs`: spots, the classifier and walking times. `Days.cs`: single-spot days and routes.
- `city.json`: the city, from `make_city.py`. Rebuild it with `python3 make_city.py [--save SAVE.hsg]` (needs `~/big-copilot`). The committed file holds the fixed AI businesses and one late-game save's rival businesses.
- `game-prices.json`: item prices, wholesale prices, neighbourhood price indices and equipment prices, from Big Copilot.
- `proposed.json`: the third pass's patch, kept for reference. The fourth pass edited `data/demand.json` directly; applying this patch brings back third-pass values, so run without it.
- `store/`: the small-store yardstick. `extract_payload.py SAVE OUT.json` has Big Copilot read a save (read-only); `node store/store_yardstick.cjs OUT.json` runs Big Copilot's validated Open a store model on it at the default difficulty. Keep the extracted payload out of git: it comes from a personal save.
