# Balance calculator

Runs the mod's own Core demand model over the city to price a cart day. The findings are in [docs/balance/cart-economics.md](../../docs/balance/cart-economics.md). Nothing here ships with the mod: `tools/build-mod.sh` packages only `unity/FoodTrucks`, and this project is not in `FoodTrucks.sln`.

## Run it

```sh
cd tools/balance
dotnet build
dotnet bin/Debug/net10.0/balance.dll                          # today's data, price at the ceiling
dotnet bin/Debug/net10.0/balance.dll --pricing default         # today's data, default price
dotnet bin/Debug/net10.0/balance.dll --patch proposed.json     # the study's proposed curves
```

It prints Markdown tables. `--out FILE` also writes them to a file. A full run takes about five minutes (the `compare` report is most of it); `--reports archetypes,costs` is instant.

| Option | Meaning |
|---|---|
| `--reports LIST` | Any of `archetypes,spots,single,routes,compare,pricing,costs` (default: all but `routes`). `compare` is staying all day against the best route over the same 16 hours, per district, and ends with the ladder table; `pricing` is the price sensitivity; `routes` the older fixed lunch/evening routes |
| `--carts LIST` | Carts for `compare`: any of `hotdog+cooler,coffee+bakery,truck-like` (default: all three). `truck-like` is a stand-in for the truck: two at the window, 160 units, four items |
| `--no-archetypes` | Leave out the archetype park and subway-entrance pitches (one of each per district, at the middle of its buildings) |
| `--patch FILE` | A JSON merge patch over `data/demand.json` and `data/economy.json`: `{"demand": {...}, "economy": {...}, "rules": {...}}`. The data files are not changed. `rules` holds proposals Core can't read yet, which the tool emulates: `stockWeight` (item → share of a unit of the stock limit) and `areaAppetite` (area → category → factor; applied to pure park spots only) |
| `--pricing ceiling\|default\|manager\|upper` | `ceiling` (default): reference × the neighbourhood's index, the most every customer class accepts. `default`: the item's default price, as `CartDemand.PriceOf` does today. `manager`: a pricing manager, reference + (ceiling − reference) × capture. `upper`: the most the upper class accepts |
| `--price-factor N` | With `ceiling`, multiplies the price (0.9 is 10% under). With `manager`, the capture (0 to 1) |
| `--market N` | The game's demand for the cart's items, 0 to 100 (100: no shop sells them in the neighbourhood; 71.5: two shops do) |
| `--restock wholesaler\|hand` | `wholesaler`: refill by pushing the cart to the nearest wholesaler (all six sell the cart's food) and back or on. `hand`: refill on the spot, no walk (a what-if) |
| `--speed M/S` | Pushing speed, default 4 (the player's jog) |
| `--cart-price N` | For the cost table, default 1200 |
| `--data`, `--city`, `--prices` | Other inputs than `data/`, `city.json`, `game-prices.json` |

## Files

- `CartModel.cs`: one cart's sales. `Cart.Plan` is `CartDemand.Plan` from `unity/FoodTrucks/Scripts/CartDemand.cs` with the game reads replaced by inputs; it calls Core's `DemandModel`, `PriceCurve`, `ServiceRate` and `SalesRate` unchanged.
- `City.cs`: spots, the classifier and walking times. `Days.cs`: single-spot days and routes.
- `city.json`: the city, from `make_city.py`. Rebuild it with `python3 make_city.py [--save SAVE.hsg]` (needs `~/big-copilot`). The committed file holds the fixed AI businesses and one late-game save's rival businesses.
- `game-prices.json`: item prices, wholesale prices, neighbourhood price indices and equipment prices, from Big Copilot.
- `proposed.json`: the study's proposed data changes: footfall and appetite curves, `passersPerHour`, the classifier's venue votes, the `transit` area, coffee and bakery service times, the coffee machine mapping, and the two emulated rules.
- `store/`: the small-store yardstick. `extract_payload.py SAVE OUT.json` has Big Copilot read a save (read-only); `node store/store_yardstick.cjs OUT.json` runs Big Copilot's validated Open a store model on it at the default difficulty. Keep the extracted payload out of git: it comes from a personal save.
