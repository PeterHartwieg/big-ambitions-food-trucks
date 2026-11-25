# Cart economics: a calculated balance study

Written 8 October 2026. Nothing here was played unless marked [seen]. Cart figures come from running the mod's Core demand model in `tools/balance/`. The shop yardstick comes from the store model of Big Copilot, a sibling project. Delivery figures come from the game code and Peter's play. The governing tuning decisions now live in [the design](../design.md). This page keeps the benchmarks they were set against, the evidence behind additive crowds and the queue caveat.

## Labels

- [code]: read in the 1.0 decompile of 23 September 2026. Not verified in game.
- [BC data]: Big Copilot's data files (store rules, item prices, buildings, map outlines, wiki data), extracted from the game and checked against saves.
- [BC model]: Big Copilot's open-a-store model, validated in that project against 94 of the player's real shops: median actual / model 0.96, p25 0.80, p90 1.08. Its scope notes and model file are in the Big Copilot repository, not this one.
- [mod data]: this repo's `data/*.json` or Core.
- [seen]: seen in game.
- [Peter]: the mod author's play notes (opener research of 23 September 2026) and his answers of 8 October.
- [calc]: this study's tool.

"Default difficulty" means the code defaults in `GameVariables`: customer promotion 0.5, market prices 1.0, wages 1.0, tax 10% [code]. The Normal preset is asset data and wasn't checked.

## Benchmarks

### Delivery driving (D)

- A job pays `deliveryReward` per completed destination, 3 destinations by default. The code default is $100. Peter's footage shows $300 for a three-stop job [seen].
- Tips roll per completed destination from the location's tip table, better for a fast delivery. Peter averaged about $20 a job [Peter].
- The game lends a free delivery van with a full tank and a hand truck, so start-up cost is $0 [code, `DeliveryJobStartController`, `DeliveryDriverMission`].
- Each start location gives one job a day. Job pay is wages, not business income, so it is untaxed [Peter].
- Three to four jobs fit a waking day [Peter]: $960 to $1,280. The study uses D = $1,120 a day (3.5 jobs).

### A small vanilla shop (S)

Big Copilot's model, run unchanged by `tools/balance/store/store_yardstick.cjs` on the city of a late-game 1.0 save (build 3682). The script changes only the difficulty (to the defaults above), the cost of goods (plain wholesale: import index 1.0, no purchasing agent), and drops the player's own shops so satisfaction is the model's default of 95. Rent and deposit are the model's estimate [BC model]. Shops charge the most every customer class accepts.

The starter shop is a 75 m² "A" retail unit (111 in the city) with the fewest items, open 06:00 to 22:00, no marketing. With the player at the register instead of the model's cashier, cleaner and guard ($1,593 a day for 16 hours), owner-run medians run from $1,720 (burgers) to $3,600 (coffee and croissants with no other sellers). Opening costs $7,600 to $11,400: register, two counter tops, the product's machine, one box of each product, deposit and first day's rent [BC data].

The yardstick S is about $2,600 a day, the median of the six owner-run medians, and $7,600 to $11,400 to open. The full fast food outfit (whole range, 24/7, best marketing) has a median of $21,900 a day on $53,700. That is where shops go later. The cart is never compared with it.

### Cart inputs

The calculator runs Core's `AreaClassifier`, `DemandModel`, `PriceCurve`, `ServiceRate` and `SalesRate` unchanged, replacing each game read with an input:

- Buildings come from Big Copilot's map outlines, with one pitch in front of every building (883). Business types are fixed AI businesses plus one late-game save's rivals, without the player's own (`city.json`).
- Market demand 100 unless stated, promotion 1.0, reference price = the item's default price.
- Prices and wholesale: hot dog $11.65 [seen, BC data] / $4.45, coffee $3.90 / $0.90, croissant $7.20 / $2.91, soda $3.00 / $0.50 [BC data]. May, Wednesday or Saturday, 5-minute steps.
- The "ceiling" price is reference x the neighbourhood's price index, the most every customer class accepts (`ItemHelper.CalculateMaxAcceptablePriceByNeighborhood`) [code]. Going 10% over it loses almost every buyer, in the mod's price curve and in the game.
- Map scale: 1.0 m per map unit for Manhattan, 1.49 for Industry City and 3.2 for the Hamptons, set from same-sized buildings. Distances could be off by 15 to 35%.
- Walking: one in-game minute per real second and a 4 m/s default jog, so pushing the cart covers 240 m per in-game hour [code]. Streets add 30% to straight lines.
- Refills: push to the nearest of the six wholesalers, 10 minutes inside, push back. A Manhattan pitch is a median 160 to 200 m from one, about two in-game hours round trip [calc].

## Additive crowds

In game on build 0.3.0 (8 October 2026), a spot with two offices and a nightclub read as 88% nightlife and 3 passers an hour at lunch [seen]. Under the share model areas were shares of one crowd, so the club pushed the offices out.

An earlier Milestone 1 session on build 0.1.0 (8 October 2026, Monday of day 1) gives a second low-demand witness [seen]. Between 15:35 and 16:24 the cart's stock went from 498 to 488 hot dogs at $11.65, roughly one sale every five minutes. The first close at 16:44 reported 15 sales for $174.75. The day ended at 34 sales for $396.10. The session did not record the pitch's location, classification or the player's distance, so this shows a quiet cart, not a measured rate for a known spot. The full session record is in Git history at commit `cd33755`.

Peter decided that areas must not be exclusive. Since 0.3.1 crowds add up:

- Each area's presence is 1 - exp(-votes / `classifier.saturation`), with no cut to two areas.
- Footfall is passersPerHour x the existing factors x (`streetBaseline` + the sum of presence x area curve x day factor).
- Appetite is weighted by each crowd's share of the hour's footfall.

He also dropped the "staying at most 70% of the best route" target: finding a good spot should pay, and staying there is fine.

Calculated contrast for the same fixture: two offices at 20 and 40 m and a nightclub at 30 m, giving office presence 0.67 and nightlife 0.89. Hot dog grill and cooler, weekday, ceiling price. Each cell is passers per hour / customers per hour [calc]:

| District, hour | Share model (0.3.0) | Additive (0.3.1) | Pure office block (eight offices, presence 0.99) |
|---|---|---|---|
| Midtown lunch 12:00 | 17 / 5.2 | 65 / 20.0 (window full) | 83 / 25.5 |
| Midtown evening 19:00 | 34 / 7.3 | 35 / 5.7 | 10 / 1.7 |
| Midtown late 22:00 | 25 / 6.5 | 21 / 4.4 | |
| Murray Hill lunch 12:00 | 8 / 2.2 | 31 / 8.4 | |
| Murray Hill evening 19:00 | 24 / 5.0 | 24 / 3.9 | |

At lunch the additive spot is over the 60 passers aimed for in Midtown, and the cart sells as fast as one person can serve. In the evening it is three and a half times as busy as a pure office block. The Core tests assert this behaviour, not the exact tuning values.

## Queue calibration caveat

From 0.4.0 the game sells through Core's `SalesSimulator`. Buyers arrive at random (Poisson) at the modelled hourly rate, wait in a queue of at most 6 at a cart and 10 at a truck (`data/rules.json`), and give up after about 20 minutes (12 in rain). At customer service skill 50, the starting value, the window runs at the base speed the study uses.

The calculator mirrors this with `--service queue`, the default. Each step's sales are scaled by the share of customers the queue serves at that load, measured with `SalesSimulator` itself. Far below or far above capacity the queue serves what the old fluid rule did. Close to capacity, random bunching costs up to about 8%: a customer finds the queue full while the window was idle a minute earlier. On the best weekday routes this cost the hot dog cart nothing, coffee 2.8% and the truck stand-in 0.2%. The queue length, patience and bunching loss are model values. Nobody has calibrated them against native customers.

## Assumptions

1. Default difficulty is `GameVariables`' code defaults. The Normal preset may differ.
2. Delivery: $300 a job, $20 tips, 3 to 4 jobs a day, from Peter's play on harder settings.
3. Map scale 1.0 / 1.49 / 3.2 m per map unit. Streets add 30%. Doors are on the outline boxes.
4. The player pushes the cart at the default jog, 4 m/s. A refill takes 10 minutes in the store.
5. Hot dogs cost $4.45 at every wholesaler (index 1.0, store 100). The real price is 10% either side.
6. Market demand 100 (no shop sells the cart's items in the neighbourhood), and no cheaper shop sets the reference price. A shop selling below the default price lowers the reference, and with it the ceiling.
7. May: footfall 1.0, no season factors.
8. The cart day is 16 hours: hot dogs 09:00 to 01:00, coffee 06:00 to 22:00. A best route is the best the search found, not a proven optimum.
9. Store yardstick: Big Copilot's model on one late-game city, A units, 06:00 to 22:00, satisfaction 95, no marketing.
10. Business types: fixed AI businesses plus one save's rivals. A new game has different rivals, so its nightlife spots differ.
11. All six wholesalers sell hot dogs, coffee, croissants and soda (Big Copilot's wiki data).
