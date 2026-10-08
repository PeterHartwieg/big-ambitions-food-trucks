# Balance calculator

Runs the mod's own Core demand model over the city to price a cart day. The findings are in [docs/balance/cart-economics.md](../../docs/balance/cart-economics.md). Nothing here ships with the mod: `tools/build-mod.sh` packages only `unity/FoodTrucks`, and this project is not in `FoodTrucks.sln`.

## Run it

```sh
cd tools/balance
dotnet build
dotnet bin/Debug/net10.0/balance.dll                       # today's data
dotnet bin/Debug/net10.0/balance.dll --patch proposed.json --pricing index   # the study's proposal
```

It prints Markdown tables. `--out FILE` also writes them to a file. A full run takes about a minute; `--reports archetypes,costs` is instant.

| Option | Meaning |
|---|---|
| `--reports LIST` | Any of `archetypes,spots,single,routes,costs` (default: all) |
| `--patch FILE` | A JSON merge patch over `data/demand.json` and `data/economy.json`: `{"demand": {...}, "economy": {...}}`. The data files are not changed |
| `--pricing default\|index` | `default`: the cart charges the item's default price, as `CartDemand.PriceOf` does today. `index`: default × the neighbourhood's price index, what vanilla shops charge |
| `--market N` | The game's demand for the cart's items, 0 to 100 (100: no shop sells them in the neighbourhood; 71.5: two shops do) |
| `--restock wholesaler\|hand` | `wholesaler`: walk to the nearest wholesaler and back. `hand`: reload on the spot from a box in hand |
| `--speed M/S` | Pushing speed, default 4 (the player's jog) |
| `--cart-price N` | For the cost table, default 1200 |
| `--data`, `--city`, `--prices` | Other inputs than `data/`, `city.json`, `game-prices.json` |

## Files

- `CartModel.cs`: one cart's sales. `Cart.Plan` is `CartDemand.Plan` from `unity/FoodTrucks/Scripts/CartDemand.cs` with the game reads replaced by inputs; it calls Core's `DemandModel`, `PriceCurve`, `ServiceRate` and `SalesRate` unchanged.
- `City.cs`: spots, the classifier and walking times. `Days.cs`: single-spot days and routes.
- `city.json`: the city, from `make_city.py`. Rebuild it with `python3 make_city.py [--save SAVE.hsg]` (needs `~/big-copilot`). The committed file holds the fixed AI businesses and one late-game save's rival businesses.
- `game-prices.json`: item prices, wholesale prices, neighbourhood price indices and equipment prices, from Big Copilot.
- `proposed.json`: the study's proposed data changes.
- `store/`: the small-store yardstick. `extract_payload.py SAVE OUT.json` has Big Copilot read a save (read-only); `node store/store_yardstick.cjs OUT.json` runs Big Copilot's validated Open a store model on it at the default difficulty. Keep the extracted payload out of git: it comes from a personal save.
