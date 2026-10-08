# Balance calculator

Runs the mod's own Core demand model over the city to price a cart day. The findings are in [docs/balance/cart-economics.md](../../docs/balance/cart-economics.md). Nothing here ships with the mod: `tools/build-mod.sh` packages only `unity/FoodTrucks`, and this project is not in `FoodTrucks.sln`.

## Run it

```sh
cd tools/balance
dotnet build
dotnet bin/Debug/net10.0/balance.dll                          # today's data, price at the ceiling
dotnet bin/Debug/net10.0/balance.dll --pricing default         # today's data, default price
dotnet bin/Debug/net10.0/balance.dll --patch proposed.json     # already applied; matches today's data
```

It prints Markdown tables. `--out FILE` also writes them to a file. A full run takes about five minutes (the `compare` report is most of it); `--reports archetypes,costs` is instant.

| Option | Meaning |
|---|---|
| `--reports LIST` | Any of `archetypes,spots,single,routes,compare,pricing,costs` (default: all but `routes`). `compare` is staying all day against the best route over the same 16 hours, per district, and ends with the ladder table; `pricing` is the price sensitivity; `routes` the older fixed lunch/evening routes |
| `--carts LIST` | Carts for `compare`: any of `hotdog+cooler,coffee+bakery,truck-like` (default: all three). `truck-like` is a stand-in for the truck: two at the window, 160 units, four items |
| `--no-archetypes` | Leave out the archetype park and subway-entrance pitches (one of each per district, at the middle of its buildings) |
| `--patch FILE` | A JSON merge patch over `data/demand.json` and `data/economy.json`: `{"demand": {...}, "economy": {...}}`, with arrays replaced. The data files are not changed. Use native fields: `economy.products.<item>.stockWeight` and `demand.areas.<area>.appetite`. Legacy `rules` sections are rejected with a migration message |
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

After building, run these from `tools/balance/` to compare the two cart headlines at the ceiling price:

```sh
dotnet bin/Debug/net10.0/balance.dll --reports compare --carts hotdog+cooler,coffee+bakery --pricing ceiling
dotnet bin/Debug/net10.0/balance.dll --reports compare --carts hotdog+cooler,coffee+bakery --pricing ceiling --patch proposed.json
```

Checked on 8 October 2026: all report results match; only the header naming the patch differs. The city-wide headlines for a dry weekday in May, market demand 100, wholesale restocking and the same 16-hour day are:

| Cart | No patch: best route profit | No patch: staying ÷ route | `--patch proposed.json`: best route profit | `--patch proposed.json`: staying ÷ route |
|---|---|---|---|---|
| Hot dog grill + cooler | $1,946 | 64% | $1,946 | 64% |
| Coffee machine + display case (croissants) | $2,353 | 72% | $2,353 | 72% |

Rechecked on 8 October 2026 before and after mirroring `BestItem`, `BestAddOn` and the plan's sale selection: the full `compare` reports are unchanged both ways. These two carts have one item per category, so their headline results are unaffected by the selection change.

The archetype demand and initial-stock cost reports (`--reports archetypes,costs`) also match both ways.

## Files

- `CartModel.cs`: one cart's sales. `Cart.Plan` is `CartDemand.Plan` from `unity/FoodTrucks/Scripts/CartDemand.cs` with the game reads replaced by inputs; it calls Core's `DemandModel`, `PriceCurve`, `ServiceRate` and `SalesRate` unchanged.
- `City.cs`: spots, the classifier and walking times. `Days.cs`: single-spot days and routes.
- `city.json`: the city, from `make_city.py`. Rebuild it with `python3 make_city.py [--save SAVE.hsg]` (needs `~/big-copilot`). The committed file holds the fixed AI businesses and one late-game save's rival businesses.
- `game-prices.json`: item prices, wholesale prices, neighbourhood price indices and equipment prices, from Big Copilot.
- `proposed.json`: the study's patch, already applied in `data/demand.json` and `data/economy.json`. Retained with native stock-weight and area-appetite fields so reapplying it gives the same results.
- `store/`: the small-store yardstick. `extract_payload.py SAVE OUT.json` has Big Copilot read a save (read-only); `node store/store_yardstick.cjs OUT.json` runs Big Copilot's validated Open a store model on it at the default difficulty. Keep the extracted payload out of git: it comes from a personal save.
