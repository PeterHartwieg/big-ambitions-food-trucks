# Balance calculator

Runs the mod's own Core demand model over the city to price a cart or truck day. The benchmarks and the additive crowd evidence are in [docs/balance/cart-economics.md](../../docs/balance/cart-economics.md), and the tuning decisions in [docs/design.md](../../docs/design.md). Nothing here ships with the mod: `tools/build-mod.sh` packages only `unity/FoodTrucks`, and this project is not in `FoodTrucks.sln`.

Revenue and profit here are earned on the day sold, for player and crew alike. In game, window takings are paid at midnight in one transaction per vehicle and day. The calculator does not model the bank balance or the cash available for restocking. That timing changes no sales, cost or profit figure here.

## Quick start

```sh
cd tools/balance
dotnet build
dotnet bin/Debug/net10.0/balance.dll                          # current data, price at the ceiling
dotnet bin/Debug/net10.0/balance.dll --pricing default         # current data, default price
dotnet bin/Debug/net10.0/balance.dll --reports ingame          # the in-game case: two offices and a nightclub
```

From the repository root, `dotnet run --project tools/balance -- --reports pitches` works too. Output is Markdown tables on stdout. `--out FILE` also writes them to a file. A default run takes about five minutes, mostly `compare`. `--reports archetypes,costs` is instant. An unknown option stops the run and points here.

## Reports

`--reports LIST` takes a comma-separated list. The default is `archetypes,spots,single,compare,pricing,costs`.

| Report | What it prints |
|---|---|
| `archetypes` | Customers and profit per hour at a pure spot of each area type, per district and daypart |
| `spots` | What the classifier makes of the real city: presence per area at every pitch |
| `single` | The best single spot per district for a day |
| `routes` | The older fixed lunch and evening routes |
| `compare` | Staying all day against the best route over the same 16 hours, per district, ending with the ladder table |
| `pricing` | Price sensitivity around the ceiling |
| `costs` | Cost to start each cart |
| `ingame` | The in-game fixture (two offices at 20 and 40 m, a nightclub at 30 m) under the old share model and the additive model, a pure office block, and customers per hour at every real pitch (p25, median, p90 per district). The share-model column uses current data, so it differs from the 0.3.0 column in the cart study |
| `premium` | Each district's best pitches, the candidates for premium spots |
| `pitches` | Prices `data/pitches.json`: each premium spot's day with and without the spot held, and each event's hours with a booked place against the same hours without the event, less the fee. See [park-premium-pitches.md](../../docs/balance/park-premium-pitches.md) |
| `foods` | Every food cart's best day city-wide on a weekday and Saturday in May and July, a January weekday and at the default price, with cost to start, best summer park day and rates at a few pure spots. Needs `--patch new-foods-proposal.json` for the commissary foods. About 20 minutes for all carts |
| `storage` | Carts and trucks with and without Extra Storage at their best spot: when the first item runs out, restocks, profit and the cabinet's payback. Use `--patch new-foods-proposal.json` for the grill rows. About 30 seconds |
| `pass` | Carts and trucks played by the player and crewed, at poor, typical, good, best and premium pitches, for a day and a week with rent, wages and the delivery fee; how many vehicles the weekly phone limit supplies; the fee against hauling; parks. About 3 minutes |
| `checks` | The calculator's own regression checks. Throws on failure. About 3 seconds |
| `playthrough` | Prices a playthrough plan. Needs `--plan FILE`, writes JSON with `--json-out FILE` |
| `crew-economics` | The weekly crew-route study (below) |
| `crew-economics-checks` | The crew study's regression checks. Throws on failure |

The crew reports cannot be combined with any other report.

## Options

| Option | Meaning |
|---|---|
| `--pricing ceiling\|index\|default\|manager\|upper` | `ceiling` (default, alias `index`): reference x the neighbourhood's index, the most every customer class accepts. `default`: the item's default price. `manager`: a pricing manager, reference + (ceiling - reference) x capture. `upper`: the most the upper class accepts. Any other value means `default` |
| `--price-factor N` | With `ceiling`, multiplies the price (0.9 is 10% under). With `manager`, the capture from 0 to 1 |
| `--market N` | The game's demand for the vehicle's items, 0 to 100 (100: no shop sells them in the neighbourhood; 71.5: two shops do) |
| `--service queue\|fluid` | `queue` (default): sales follow the game's queue, Core's `SalesSimulator` measured per order time and load (`QueueThroughput` in `CartModel.cs`). `fluid`: the old min(customers who buy, window capacity) |
| `--carts LIST` | Carts for `compare` and `foods`. `hotdog+cooler`, `coffee+bakery` and `truck-like` are the `compare` default. With the new-foods patch, the keys in `Program.cs` (`dumplings`, `kabob`, `burger+kabob`, `hotdog+kabob`, `griddle`, `corn`, `concessions`, `slushie`, `concessions+slushie`, `truck-street` and more). `foods` defaults to every cart. `truck-like` is a truck stand-in: two at the window, four pieces of 80 units, four items |
| `--no-archetypes`, `--no-parks` | Leave out the archetype park and subway-entrance pitches (one of each per district, at the middle of its buildings) |
| `--patch FILE` | A JSON merge patch over `data/demand.json` and `data/economy.json` (details below) |
| `--restock wholesaler\|hand` | `wholesaler`: push the cart to the nearest wholesaler and back or on. `hand`: refill on the spot with no walk, a what-if |
| `--speed M/S` | Pushing speed, default 4 (the player's jog) |
| `--drive-speed M/S` | The player's truck in town, default 8. A real second is a game minute, so that is 8 m a game minute. Trucks restock by driving |
| `--deliveries-per-week N`, `--fee-sharing N` | The delivery fee for `pass` and the crew study: the flat fee per delivery (`rules.json` `deliveryFeeCents`) x deliveries a week (default 1) / the vehicles sharing them (default 1, minimum 1) |
| `--crew-skill N`, `--crew-satisfaction N` | The hire: customer service 60 at satisfaction 85 by default (speed 1.0, $27.78 an hour) |
| `--cart-price N` | Cart price for the cost table, default 1200 |
| `--plan FILE` | For `playthrough`: the plan `tools/playthrough report` writes (each vehicle's foods, crew size and week of stops by position and hours) |
| `--json-out FILE` | JSON output for `playthrough` and the crew reports |
| `--crew-economics-config FILE` | Assumptions for the crew study, default `crew-economics.json` |
| `--out FILE` | Also write the Markdown to a file |
| `--data DIR`, `--city FILE`, `--prices FILE` | Inputs other than `data/`, `city.json` and `game-prices.json` |

`playthrough` prices each stop at the city building nearest its position. Crews load once in the bay before the first stop (`RestockMode.None`). It reports revenue, stock cost and units per weekday. New-food prices come from the `game` section of `new-foods-proposal.json`.

## Inputs and overlays

- `data/*.json`: the shipped tuning, read as the mod reads it.
- `city.json`: the city, built by `make_city.py` from Big Copilot, a sibling project. Rebuild with `python3 make_city.py [--save SAVE.hsg]` (needs `~/big-copilot`). The committed file holds the fixed AI businesses and one late-game save's rival businesses.
- `game-prices.json`: item, wholesale and equipment prices and neighbourhood price indices, from Big Copilot.
- `--patch FILE`: `{"demand": {...}, "economy": {...}}` merged over the data files in memory, arrays replaced, `null` removing a key. The data files are not changed. Use native fields such as `economy.products.<item>.stockWeight` and `demand.areas.<area>.appetite`. An optional `game` section adds item prices (`items.<item>.defaultMarketPrice`, `wholesalePrice`, `boxSize`), equipment prices (`equipment.<key>`) and market demand per item and neighbourhood (`market.<item>.<neighbourhood>`, overriding `--market`). Legacy `rules` sections are rejected with a migration message.
- `new-foods-proposal.json`: the overlay for the commissary and new foods. It adds their items, equipment and prices for `foods`, `storage` and `playthrough`. Calculated, not measured in game.
- `proposed.json`: the historical third-pass patch, kept for reference. Applying it brings back third-pass values, so run without it for current data.

## Model assumptions

- Stock loads use Core's `ProductInfo.StockWeight` (coffee and tea 0.25, pastries 0.5, other shipped products 1).
- Storage is per piece, as in Core's `Storage`. Each menu item is held by the piece that serves it (`EquipmentForProduct`). Each piece holds the vehicle's `unitsPerPiece`, doubled with Extra Storage (`Cart(storage: true)`). A piece serving several items fills in the proportions the spot sells them.
- A stay restocks when the first stocked item runs out. A restock keeps the stock on board and fills each piece's free room. With no time for a trip, the other pieces sell on until closing. A route goes straight on only while every piece holds at least half its room.
- Area appetite is applied once by Core's `DemandModel`, mixed-area spots included. There are no separate park appetite or stock-weight rules in the calculator. The archetype park pitch has the presence a spot inside a game park area gets (about 0.70), with a waterfront archetype beside it.
- Who serves: `Cart.Plan` keeps `CartDemand.Plan`'s capacity for the vehicle's whole staff, and selling uses the people at the window as `CartSelling` does: capacity per server x team speed x servers (`Cart(servers, speed)`). A player alone at a truck is one server. Trucks restock by driving, carts by pushing. `RestockMode.None` is a crew that loads in its commissary bay and sells until everything is out. A `Planner` with `Depots` restocks at commissaries instead of wholesalers.
- Item choice follows `CartDemand.Plan` and `CartSelling.ServeCustomer`: each category offers its stocked item with the highest price conversion, ties broken by more stock, then ordinal item name. The add-on is the best-converting item across add-on categories and scales drinks per food buyer.

## Files

- `CartModel.cs`: one cart's sales. `Cart.Plan` is `CartDemand.Plan` from `unity/FoodTrucks/Scripts/CartDemand.cs` with the game reads replaced by inputs. It calls Core's `DemandModel`, `PriceCurve`, `ServiceRate` and `SalesRate` unchanged.
- `City.cs`: spots, the classifier and walking times. `Days.cs`: single-spot days and routes.
- `CrewEconomics.cs`, `crew-economics.json`: the weekly crew-route simulator, its versioned assumptions and checks.
- `store/`: the small-store yardstick (below).

## Crew-route study

`crew-economics` is an offline study. It changes no shipped runtime or data and needs no game install. Results and caveats are in [crew-route-economics-2026-10-21.md](../../docs/balance/crew-route-economics-2026-10-21.md), with the tracked numbers in [crew-route-economics-2026-10-21.json](../../docs/balance/crew-route-economics-2026-10-21.json). Run from the repository root:

```sh
dotnet build tools/balance/Balance.csproj
mkdir -p build/crew-economics
dotnet tools/balance/bin/Debug/net10.0/balance.dll \
  --reports crew-economics,crew-economics-checks --pricing default \
  --out build/crew-economics/crew-study.md \
  --json-out build/crew-economics/crew-study.json
```

The CLI defaults to ceiling pricing, so the command asks for default prices explicitly. Use `--pricing ceiling` for the second case. `crew-economics-checks` can run alone. The study honours `--pricing`, `--price-factor`, `--crew-skill`, `--crew-satisfaction`, `--market`, `--service`, `--data`, `--city`, `--prices`, `--patch`, `--deliveries-per-week` and `--fee-sharing`. Its menu, two employees, 07:00 to 19:00 shifts and service windows are fixed study policy. `--carts`, `--restock`, `--speed` and `--drive-speed` do not configure it.

`--crew-economics-config` takes a complete schema-version-2 assumptions file. The default is [crew-economics.json](crew-economics.json). Fields, units, ranges and finite values are validated, and unknown or missing fields are errors. Keep its explicit `units` string. The file sets road factor, normal and heavy speeds and waits, tank, reserve, stress starting fuel, consumption, fuel price, the synthetic station loop, loading time, districts, demand quantiles, the sensitivity grids and the short and long distance bands.

The JSON written by `--json-out` has, per study: `schemaVersion` (2), `baselineLabel`, `assumptions`, `inputs` (with SHA256 hashes of the input files and calculator and Core sources), `policy`, `caveats`, `selectionOmissions`, `routes` (addresses, coordinates, windows, scores and leg lengths), `checks`, `scenarios`, `legacyComparisonSummary`, `results` (per-week and per-day economics, stock boundaries, trips and refuels), `sensitivity` and `waitSensitivity`. The tracked compact export wraps both price cases in a schema-1 envelope with `evidenceStatus`, `date`, `notes` and `studies`, and names the full ignored file with `fullEvidenceFile` and `fullEvidenceSha256`.

Daily `soldOutItems` needs positive opening stock, positive sales and ending stock at or below 1e-6. Weekly `soldOutItemDays` counts those days per item. A failed home return or a day with no possible service gives a null weekly profit, with the daily reasons and costs kept in `realizedOperatingBalance`. Results are conditional tuning evidence. Coordinates are the balance map's regional frame, not native world coordinates, and distances are straight lines times a road factor, not measured road geometry.

## Store comparison

`store/extract_payload.py SAVE OUT.json` has Big Copilot read a save, read-only. `node store/store_yardstick.cjs OUT.json` runs Big Copilot's validated open-a-store model on it at default difficulty. Keep the extracted payload out of Git: it comes from a personal save. The yardstick and its settings are described in the cart study's benchmarks.

## Current limits

- Prices, map outlines and rival businesses come from one late-game save via Big Copilot. A new game has different rivals and nightlife spots.
- Map distances could be off by 15 to 35%. Streets add an assumed 30% to straight lines.
- Parks and subway entrances are archetype pitches at each district's centre, because `city.json` has no park volumes or subway entrances.
- Sales are expected quantities from 5-minute steps, not native customer events. The queue loss is measured on Core's simulator, not calibrated against the game.
- There is no bank balance, midnight payout, delivery arrival time or commissary stock limit.
- Native road routing, parking, traffic, fuel use and station access are not calibrated. The crew study's travel and fuel inputs are assumptions.
- Regulars, reputation, health, competition between own vehicles, catering, spoilage, fines and tax are outside every report.
