# Cart economics: a calculated balance study

Written 8 October 2026. Nothing in this study was played. The cart figures come from running the mod's own Core demand model (`tools/balance/`), the store figures from Big Copilot's validated store model, and the delivery figures from the game code and Peter's play.

## The answer

| Daily figure (one player, default difficulty, weekday, dry) | Profit a day | Cost to start | Source |
|---|---|---|---|
| Delivery driving, 3 to 4 jobs | $960 to $1,280 | $0 | [code], [Peter] |
| Food cart today, best route in the city | $837 | $5,625 to $6,750 | [calc] |
| Food cart today, typical neighbourhood | $580 to $820 | same | [calc] |
| Food cart with the proposed changes, best route | $1,874 | about $4,700 | [calc] |
| Food cart with the proposed changes, typical neighbourhood | $820 to $1,460 | same | [calc] |
| Small starter shop run by the player (A unit, 06:00 to 22:00), city median | $1,720 to $3,600, about $2,600 | $7,100 to $9,400 | [BC model] |

**Verdict.** Today the cart is a worse delivery job that costs nearly as much as a shop. Its best day ($837) is below three delivery runs ($960), and its start-up cost is 60 to 95% of a starter shop's. Two things hold it down: it sells at the item's default price while every vanilla shop charges default × the neighbourhood's price index, and one person at a window serves at most 20 hot dogs an hour, so a day tops out near 150 units whatever the spot.

**Top changes.**

1. **Price like a shop.** Let the cart charge default price × the neighbourhood's price index, the price the game's customers all accept and the price vanilla shops charge. Doubles the margin in Midtown, +40% in Industry City. Game-side rule in `CartDemand.PriceOf`, no data change.
2. **`stockUnits` 80 → 120 for the cart** (truck 160 → 240). A good office lunch sells 85 to 90 units; 80 runs out before the rush ends.
3. **Load loose units, not whole boxes.** Charge only what fits in the cart at the wholesaler. Cuts the start-up cost by $1,700 to $2,000 and settles what happens to the rest of a 500-unit box.
4. **Keep `passersPerHour` at 30 and the service times as they are.** The window is the limit at every good pitch, so more footfall mostly inflates the truck later.
5. **Fees: vehicle permit $500, vendor licence $250 per 60 days.** Entry then costs about $4,700 for a hot dog cart: four delivery days, half a starter shop.

With 1 and 2 the best route makes $1,874 (1.7 delivery days, 72% of the starter shop), a typical neighbourhood $820 to $1,460, and a truck with one hire about $2,550, the level of the starter shop. That's the band Peter asked for, with room above the cart for the truck.

## Labels

- **[code]**: read in the 1.0 decompile of 23 September 2026 (`BigAmbitionsMCP/lib/decompiled-1.0-build-2026-09-23`). Unverified in game.
- **[BC data]**: Big Copilot's data files (`ba_store_rules.json`, `ba_item_prices.json`, `ba_buildings.json`, map outlines), extracted from the game and checked against saves.
- **[BC model]**: Big Copilot's Open a store model (`template/open-store-model.js`). Validated against 94 of the player's real shops: median actual ÷ model 0.96, p25 0.80, p90 1.08 (`docs/open-a-store-scope.md`, "Expected profit").
- **[mod data]**: this repo's `data/*.json` or Core.
- **[seen]**: seen in game (Milestone 1 session, `docs/evidence/milestone-1-session-1.md`).
- **[Peter]**: the mod author's own play notes (opener research of 23 September 2026).
- **[calc]**: this study's tool, from the above.
- **[assumption]**: a judgement of mine, listed at the end.

"Default difficulty" means the code defaults in `GameVariables`: customer promotion 0.5, market prices 1.0, wages 1.0, tax 10% [code]. The preset Normal is asset data and wasn't checked.

## 1. Delivery driving

How it works [code, `DeliveryJobStartController`, `DeliveryJobHelper`, `DeliveryDriverMission`]:

- A job pays `deliveryReward` per completed destination. The code default is $100; the asset value isn't in the code, but Peter's Episode 1 footage shows $300 for a three-stop job [seen, tips video script]. Destinations per job default to 3.
- Tips: a roll per completed destination from the location's tip table, with a better chance for a fast delivery (`DeliveryJobTipsConfig`). The table is asset data. Peter's average was about $20 a job [Peter].
- The game spawns a free delivery van with a full tank and a hand truck (`OnAcceptJob`: `fuel = maxFuel`, `zeroSellingPrice`). No vehicle, fuel or deposit to pay. Start-up cost: $0.
- Each start location gives one job a day (`deliveryJobLocationsDoneToday`), so a second job means going to another start location.
- The time limit is the straight-line route length × `minutesPerMeter`, clamped to `minutesMin` to `minutesMax`. All three are asset data.
- Job pay is wages, not business income, so it's untaxed [Peter's opener research: "The 51% tax applies to business income only"].

| Delivery day | Pay | Tips | Total | Time |
|---|---|---|---|---|
| 3 jobs | $900 | about $60 | **$960** | most of a waking day [Peter: "Three to four delivery jobs fit into a day around sleep"] |
| 4 jobs | $1,200 | about $80 | **$1,280** | a full waking day |

Per waking hour that's about $70 to $90. I use $1,120 (3.5 jobs) as the delivery day below.

## 2. A small vanilla shop

Big Copilot's model, run unchanged by `tools/balance/store/store_yardstick.cjs` on the city of a late-game 1.0 save (build 3682). The script changes only the difficulty (to the defaults above), the cost of goods (the plain wholesale price: import index 1.0, no purchasing agent), and drops the player's own shops so satisfaction is the model's well-run default of 95. Rent and deposit per building are Big Copilot's rent estimate [BC model]. Market: "save's sellers" uses that save's count of shops per product and neighbourhood; "no other sellers" sets every count to 0, the best case for a newcomer.

**Starter shops.** A 75 m² "A" retail unit, the fewest items, open 06:00 to 22:00, no marketing. 111 A units in the city.

| Starter shop | Market | Model profit/day p25 / median / p90 (with its staffing) | Player at the register instead (no wages) p25 / median / p90 | Cost to open, median |
|---|---|---|---|---|
| Hot dogs + soda | save's sellers | -$768 / $470 / $1,023 | $825 / $2,063 / $2,616 | $9,424 |
| Hot dogs + soda | no other sellers | $941 / $1,316 / $1,617 | $2,534 / $2,909 / $3,210 | $9,424 |
| Burgers | save's sellers | -$192 / $126 / $932 | $1,401 / $1,719 / $2,525 | $7,649 |
| Burgers | no other sellers | $808 / $1,160 / $1,446 | $2,401 / $2,753 / $3,039 | $7,649 |
| Coffee + croissants | save's sellers | $678 / $879 / $2,034 | $2,271 / $2,472 / $3,627 | $7,074 |
| Coffee + croissants | no other sellers | $1,520 / $2,004 / $2,376 | $3,113 / $3,597 / $3,969 | $7,074 |

- The model's staffing is its structural estimate: a cashier, a cleaner and a guard every open hour, $1,593 a day for 16 hours [BC model]. A beginner usually stands at the register, so the right-hand column is the fair match for a cart, which is also the player's own day.
- Cost to open: register $900, two counter tops at $400, the product's machine (hot dog grill $2,200, drinks fridge $875, industrial grill $1,400, cheap coffee machine $400, bakery showcase $895), one box of each product at wholesale (500 hot dogs $2,225), the deposit and the first day's rent [BC data, opener research]. Seating, toilets and music are left out, which the 95 satisfaction assumes are there; that flatters the starter a little.
- The median shop for hot dogs (no other sellers) sells to 97 customers a day: revenue $3,756, goods $815, rent $33.

**For scale: the full outfit.** The same model with the whole fast food range, 24/7 and the best marketing mix (Big Copilot's planner default) gives a median of $21,900 a day on a $53,700 investment in the late-game city. That's where shops go once the player can afford the full range. The cart should never be compared with it.

**The yardstick.** A starter shop run by the player makes about **$2,600 a day** (the median of the six owner-run medians above, which run from $1,720 to $3,600) and costs **$7,100 to $9,400** to open.

## 3. The cart today

### How the tool computes a cart day

`tools/balance/` runs Core unchanged: `AreaClassifier`, `DemandModel`, `PriceCurve`, `ServiceRate` and `SalesRate`. `Cart.Plan` copies `CartDemand.Plan` from the game side, replacing each game read with an input:

| Game side reads | The tool uses |
|---|---|
| Buildings within the classifier radius of the cart, by distance to their doors (`World.Read`) | Each building's outline box from Big Copilot's map, in metres; the distance is to the nearest point of the box (doors sit on the outline, so this is the closest a door can be). One pitch in front of every building, 883 in all |
| Business types | Fixed AI businesses (wholesalers, gas stations, banks...) and one late-game save's rival businesses; the player's own left out (`city.json`) |
| Neighbourhood | The building's [BC data] |
| Market demand, only for `isADemandedProduct` items | 100 (no shop sells hot dogs in the neighbourhood) unless stated |
| Promotion = `baseCustomerPromotionMultiplier` / 0.5 | 1.0 |
| Price = `Item.DefaultMarketPrice` | Hot dog $11.65 [seen, BC data], coffee $3.90, croissant $7.20, soda $3.00 [BC data] |
| Reference = min(default price, cheapest shop in the neighbourhood) | The default price |
| Staff 1, stock 80 units, 2 equipment slots | From `data/economy.json` |

Also: wholesale prices hot dog $4.45, coffee $0.90, croissant $2.91, soda $0.50 [BC data], May (spring, footfall 1.0), Wednesday or Saturday. A day runs in 5-minute steps; when the cart sells out, the player pushes it to the nearest of the six wholesalers and back.

**The map scale is an approximation.** Big Copilot's map draws Manhattan, Industry City and the Hamptons in separate frames. I set the scale from same-sized buildings: 1.0 m per map unit for Manhattan (a 225 m² "C" unit's outline says at least 0.85, a 2,000 m² cinema's at most 1.36), 1.49 for Industry City and 3.2 for the Hamptons (from the same building sizes). Manhattan comes out about 0.9 km across. Distances could be off by 15 to 35%.

**Walking speed.** The game runs one in-game minute per real second at normal speed (`GameManager`: `Time.deltaTime × MinutesMultiplier`), and the player jogs at 4 m/s by default, 6 m/s when running (`ThirdPersonCharacter`, `GlobalReferences`) [code]. So pushing the cart covers 240 m per in-game hour, 360 m running. The worktree's `RouteDay` used 1.2 m/s for walking; that's the `Walk` speed, which the player doesn't use by default. Streets add 30% to straight lines [assumption].

### Hourly rates by spot type

A pure spot of each area type, hot dog grill + cooler, weekday, dry, unlimited stock. Each cell is the model's customers per hour (before price and the window) / profit per hour (sales at the window's limit, price minus wholesale) [calc].

| Neighbourhood | Area | Early 05-08 | Morning 08-11 | Lunch 11-14:30 | Afternoon | Evening 17-21 | Late 21-02 |
|---|---|---|---|---|---|---|---|
| Midtown | office | 1 / $7 | 3 / $18 | 43 / $157 | 2 / $13 | 5 / $36 | 1 / $6 |
| Midtown | residential | 1 / $5 | 1 / $9 | 6 / $48 | 2 / $12 | 7 / $54 | 3 / $22 |
| Midtown | nightlife | 0 / $0 | 0 / $1 | 4 / $30 | 1 / $5 | 9 / $63 | 12 / $87 |
| Midtown | industrial | 2 / $14 | 1 / $6 | 21 / $157 | 1 / $8 | 4 / $30 | 8 / $62 |
| Midtown | retail | 0 / $1 | 1 / $7 | 15 / $116 | 5 / $30 | 7 / $54 | 2 / $11 |
| Lower Manhattan | office | 1 / $6 | 2 / $15 | 35 / $155 | 2 / $11 | 2 / $12 | 0 / $2 |
| Lower Manhattan | residential | 1 / $4 | 1 / $8 | 5 / $39 | 1 / $10 | 3 / $18 | 1 / $6 |
| Lower Manhattan | nightlife | 0 / $0 | 0 / $1 | 3 / $24 | 1 / $5 | 3 / $21 | 3 / $24 |
| Lower Manhattan | industrial | 2 / $12 | 1 / $5 | 17 / $129 | 1 / $7 | 1 / $10 | 2 / $17 |
| Lower Manhattan | retail | 0 / $1 | 1 / $6 | 12 / $93 | 4 / $26 | 3 / $18 | 0 / $3 |
| Murray Hill | office | 1 / $5 | 2 / $12 | 18 / $135 | 1 / $9 | 3 / $25 | 1 / $7 |
| Murray Hill | residential | 1 / $3 | 1 / $7 | 3 / $20 | 1 / $8 | 5 / $38 | 3 / $26 |
| Murray Hill | nightlife | 0 / $0 | 0 / $1 | 2 / $13 | 1 / $4 | 6 / $44 | 14 / $100 |
| Murray Hill | industrial | 1 / $10 | 1 / $4 | 9 / $67 | 1 / $5 | 3 / $21 | 10 / $72 |
| Murray Hill | retail | 0 / $1 | 1 / $5 | 7 / $48 | 3 / $21 | 5 / $38 | 2 / $13 |
| Hell's Kitchen | office | 0 / $3 | 1 / $10 | 29 / $155 | 1 / $10 | 5 / $40 | 2 / $12 |
| Hell's Kitchen | residential | 0 / $2 | 1 / $5 | 4 / $32 | 1 / $9 | 8 / $60 | 6 / $45 |
| Hell's Kitchen | nightlife | 0 / $0 | 0 / $1 | 3 / $20 | 1 / $4 | 10 / $70 | 23 / $150 |
| Hell's Kitchen | industrial | 1 / $5 | 0 / $3 | 14 / $107 | 1 / $6 | 5 / $33 | 17 / $124 |
| Hell's Kitchen | retail | 0 / $0 | 1 / $4 | 10 / $77 | 3 / $23 | 8 / $60 | 3 / $22 |
| Garment District | office | 1 / $5 | 2 / $12 | 33 / $155 | 1 / $9 | 3 / $25 | 1 / $9 |
| Garment District | residential | 1 / $3 | 1 / $7 | 5 / $37 | 1 / $8 | 5 / $38 | 5 / $33 |
| Garment District | nightlife | 0 / $0 | 0 / $1 | 3 / $23 | 1 / $4 | 6 / $44 | 18 / $130 |
| Garment District | industrial | 1 / $10 | 1 / $4 | 17 / $125 | 1 / $5 | 3 / $21 | 13 / $93 |
| Garment District | retail | 0 / $1 | 1 / $5 | 12 / $90 | 3 / $21 | 5 / $38 | 2 / $17 |
| Industry City | office | 1 / $5 | 2 / $11 | 25 / $155 | 1 / $5 | 3 / $22 | 1 / $9 |
| Industry City | residential | 0 / $3 | 1 / $6 | 4 / $28 | 1 / $4 | 5 / $33 | 4 / $32 |
| Industry City | nightlife | 0 / $0 | 0 / $1 | 2 / $18 | 0 / $2 | 5 / $39 | 17 / $124 |
| Industry City | industrial | 1 / $9 | 1 / $4 | 13 / $94 | 0 / $3 | 3 / $19 | 12 / $89 |
| Industry City | retail | 0 / $1 | 1 / $4 | 9 / $68 | 2 / $11 | 5 / $33 | 2 / $16 |
| Hamptons | office | 0 / $3 | 1 / $8 | 16 / $120 | 1 / $6 | 2 / $16 | 1 / $4 |
| Hamptons | residential | 0 / $2 | 1 / $4 | 2 / $18 | 1 / $5 | 3 / $23 | 2 / $16 |
| Hamptons | nightlife | 0 / $0 | 0 / $0 | 1 / $11 | 0 / $2 | 4 / $27 | 8 / $62 |
| Hamptons | industrial | 1 / $6 | 0 / $3 | 8 / $60 | 0 / $3 | 2 / $13 | 6 / $45 |
| Hamptons | retail | 0 / $0 | 0 / $3 | 6 / $43 | 2 / $13 | 3 / $23 | 1 / $8 |

The ceiling is about $155 an hour: one person serves 20 hot dogs an hour (3 minutes each [mod data]) at a $7.20 margin, plus a few sodas. Office lunches want 25 to 43 customers an hour, so they all hit it; the model's 3× lunch peak is there in demand but flattened in sales. Hell's Kitchen's late nightlife is the only other daypart that fills the window.

The coffee + bakery cart (coffee machine + display case with croissants) does best at offices and industrial spots early: Midtown office 20 customers/h early and 26 in the morning for $65 to $66 an hour; industrial spots 21 to 37 customers early for $65 [calc]. Its ceiling is about $66 an hour: the same 3-minute service at a $3.00 to $4.29 margin.

**Days and weather** (hot dog grill + cooler) [calc]:

| Conditions | Midtown office lunch: customers/h, served/h, profit/h | Hell's Kitchen nightlife late |
|---|---|---|
| Weekday, dry | 43, 20.8, $157 | 23, 20.3, $150 |
| Weekday, rain | 21, 20.8, $157 | 12, 11.7, $87 |
| Weekend, dry | 6, 6.0, $45 | 50, 20.3, $150 |
| Weekend, rain | 3, 3.0, $23 | 25, 20.3, $150 |

Rain halves footfall, but the busiest pitches had twice the window's demand, so rain barely touches them.

### What the classifier makes of the real city

One pitch in front of each building; every building whose outline is within 50 m votes [calc]. A spot is "mostly X" at a share of 0.5 or more.

| Neighbourhood | Spots | Mostly office | Mostly residential | Mostly retail | Mostly industrial | Mostly nightlife | Highest office share |
|---|---|---|---|---|---|---|---|
| Midtown | 94 | 33 | 22 | 36 | 0 | 3 | 0.82 |
| Lower Manhattan | 117 | 16 | 61 | 21 | 23 | 0 | 0.76 |
| Murray Hill | 103 | 7 | 36 | 57 | 4 | 1 | 0.65 |
| Hell's Kitchen | 146 | 5 | 107 | 34 | 0 | 0 | 0.69 |
| Garment District | 165 | 6 | 71 | 84 | 6 | 0 | 0.57 |
| Industry City | 200 | 0 | 73 | 83 | 44 | 0 | 0.26 |
| Hamptons | 58 | 0 | 28 | 29 | 0 | 1 | 0.00 |

Nightlife is rare: only theatres (a building type), the casino boat and rival nightclubs vote for it. Hell's Kitchen, the design's nightlife district, has no nightlife spot at all; the classifier reads it as residential. The hand-drawn zones in the design are needed for the late peak to exist where the design puts it.

### A good single-spot day

The best spot per neighbourhood, open 07:00 to 23:00, hot dog grill + cooler, restocking at the nearest wholesaler when it sells out [calc].

| Neighbourhood | Spot (area mix) | Weekday dry | Weekday rain | Weekend dry | Weekend rain | Units, weekday dry |
|---|---|---|---|---|---|---|
| Midtown | 1 Sixth Street (residential 0.5, office 0.5) | $798 | $467 | $348 | $174 | 129 |
| Lower Manhattan | 14 Broadway Street (residential 0.55, office 0.45) | $573 | $309 | $93 | $47 | 92 |
| Murray Hill | 3 Pier (industrial 0.5, office 0.5) | $499 | $287 | $157 | $79 | 80 |
| Hell's Kitchen | 5 Second Avenue (office 0.51, retail 0.49) | $760 | $405 | $532 | $289 | 122 |
| Garment District | 51 Second Street (industrial 0.61, office 0.39) | $723 | $432 | $173 | $86 | 114 |
| Industry City | 4 Twenty-fifth Street (industrial 0.63, retail 0.37) | $534 | $273 | $433 | $216 | 85 |
| Hamptons | 14 Seventh Avenue (retail 0.59, industrial 0.41) | $331 | $166 | $429 | $214 | 53 |

The coffee + bakery cart's best single spot makes $416 (Midtown), and $176 to $284 elsewhere [calc].

### A good route

Each pitch's best 20 spots on their own, then every combination in one map region; between pitches the cart goes through the nearest wholesaler and arrives full. Restock walks count against the day [calc].

| Route | Cart | Weekday dry | Weekday rain | Weekend dry | Weekend rain | Best weekday route |
|---|---|---|---|---|---|---|
| Lunch 11:00-14:30, evening 17:00-23:00 | hot dog + cooler | **$837** | $661 | $978 | $721 | Garment District office/industrial lunch (80 units, sold out), Murray Hill casino-boat nightlife evening (53 units); 133 min walking |
| Breakfast 06:00-10:30, lunch, evening | hot dog + coffee | $705 | $649 | $1,060 | $751 | Breakfast walk eats into lunch: the lunch pitch opens at 13:20 |
| Breakfast 06:00-11:00, afternoon 14:30-17:00 | coffee + bakery | $339 | $260 | $365 | $222 | Midtown office breakfast, Midtown retail afternoon |

Best lunch + evening route inside each neighbourhood, weekday dry: Midtown $837, Hell's Kitchen $820, Garment District $727, Murray Hill $679, Industry City $588, Lower Manhattan $583; the Hamptons has no wholesaler, so no restock [calc].

Two findings about the day's shape:

- **Moving barely beats staying.** The best route ($837) makes 5% more than the best single spot ($798). A spot that is half office and half residential fills the window at lunch with half the office crowd and gets the residential evening, while a move costs about two hours of pushing. The design wants staying put to earn noticeably less.
- **Stock binds at lunch.** Every best lunch sells all 80 units before 14:30; the evening pitch then sells 50 to 55 units in six hours.

### Cost to start

| Menu | Cart | Equipment [BC data] | First stock: whole boxes at wholesale | Total |
|---|---|---|---|---|
| Hot dog grill | $1,200 | $2,200 | $2,225 (500 hot dogs) | **$5,625** |
| Hot dog grill + cooler | $1,200 | $3,075 | $2,475 | **$6,750** |
| Hot dog grill + coffee machine | $1,200 | $2,600 | $2,675 | **$6,475** |
| Coffee machine + display case | $1,200 | $1,295 | $1,905 | **$4,400** |

The cart is $1,200 [seen, dealer dialog]. Only 80 of a box's 500 units fit; what happens to the rest isn't decided (open question 2). No licence or permit fee exists yet.

### Sensitivities

| Change | Best lunch + evening route, weekday dry |
|---|---|
| As above | $837 |
| Reload from a box in hand at the pitch, no walk | $848 |
| Two shops already sell hot dogs in the neighbourhood (market demand 71.5) | $750 (proposal: $1,635 instead of $1,874) |
| Day-1 import price index at its 1.3 cap and a 110 wholesaler: hot dog $6.36 instead of $4.45 [code, `PlayerItemPurchaser`; opener research] | margin $5.29 instead of $7.20: about -27% |
| A shop in the neighbourhood sells hot dogs below $11.65 | The reference price drops to that shop's price. At $5.23 (one such shop in the late-game save's Garment District) the cart's ratio is 2.2 and nobody buys. This is the game's own customer rule |

## 4. Verdict and tuning

### The band

| Rung | Profit a day | Cost to start | Days of delivery driving to afford |
|---|---|---|---|
| Delivery day (D) | $1,120 | $0 | 0 |
| **Cart target** | best route 1.5 to 1.8 D ($1,700 to $2,000); typical neighbourhood 0.8 to 1.3 D; single all-day spot below the best route by at least 10% | 3 to 4.5 D ($3,400 to $5,000) | 3 to 4.5 |
| Small starter shop run by the player (S) | $2,600 (75% of S is $1,950) | $7,100 to $9,400 | 6 to 8.5 |
| Truck with the player and one hire (Milestone 4 and 5) | about S | | |

In words: a good cart day is worth about 1.7 delivery days and three quarters of a starter shop, a typical one about a delivery day, and the cart costs about half a shop. After the 10% tax on business income, $1,874 is $1,687, still 1.5 D.

### Where the cart sits now

Below the band on profit and above it on cost. Best route $837 (0.75 D, 32% of S), typical neighbourhood $580 to $820 (0.5 to 0.75 D), start-up $5,625 to $6,750 (5 to 6 D, 60 to 95% of a starter shop). A player who can afford a cart is a day or two of driving away from a shop that earns three times as much.

### Proposed changes and results

Each was run through the same tool. `tools/balance/proposed.json` holds the data change; the pricing rule is the `--pricing index` flag.

| # | Change | Where | Why |
|---|---|---|---|
| 1 | The cart's price for an item = default price × the neighbourhood's average price index (`CitizenHelper.averagePriceIndicesInNeighborhoods`), or a price setting that starts there | `CartDemand.PriceOf` (game side) | Vanilla shops charge this and every customer accepts it [code, `CitizenHelper.MaxAcceptableRelativePrice`]; the mod's price curve already keeps every buyer up to it. Margin per hot dog: $15.15 in Midtown (index 1.682), $10.21 in Lower Manhattan (1.258), $10.07 in Industry City (1.246), against $7.20 now |
| 2 | `vehicles.cart.stockUnits` 80 → 120; `vehicles.truck.stockUnits` 160 → 240 | `data/economy.json` | A good lunch sells 85 to 91 units including drinks. The design says "about half a day at a good pitch": 6 hours × 20 + drinks is about 140 |
| 3 | Loading at a wholesaler charges only the units that fit; the rest stays on the shelf | game side, loading rule | Start-up drops by $1,700 to $2,000, and there's no 420-unit box to carry |
| 4 | Vehicle permit $500 per cart, vendor licence $250 per 60 days (about $4 a day), plus the Food Safety course | new fees (Milestone 4) | Gates, not costs: under 10% of entry, 0.2% of a day |
| 5 | Keep the cart at $1,200 | `vehicles` dealer price | About one delivery day; with 3 and 4 the entry lands at four delivery days |
| 6 | Keep `passersPerHour` 30, all footfall and appetite curves, and the service minutes | `data/demand.json`, `data/economy.json` | At good pitches the window binds, not footfall. More footfall would mostly lift the truck (proposal run below), and faster service would leave the design's 12 to 20 an hour |

Results with 1 and 2 (hot dog grill + cooler unless named) [calc]:

| | Today | Proposed |
|---|---|---|
| Best lunch + evening route, weekday dry | $837 | **$1,874** |
| same, weekday rain / weekend dry / weekend rain | $661 / $978 / $721 | $1,406 / $1,848 / $1,081 |
| Lunch + evening + late, weekday dry | $790 | $1,941 |
| Breakfast + lunch + evening, hot dog + coffee, weekday dry | $705 | $1,931 |
| Breakfast + afternoon, coffee + bakery, weekday dry | $339 | $778 |
| Best route inside Midtown | $837 | $1,874 |
| Hell's Kitchen | $820 | $1,456 |
| Garment District | $727 | $1,124 |
| Murray Hill | $679 | $1,047 |
| Industry City | $588 | $818 |
| Lower Manhattan | $583 | $892 |
| Best single spot all day, Midtown, weekday dry | $798 | $1,710 |
| Best single spot, Hell's Kitchen / Garment District / Industry City | $760 / $723 / $534 | $1,291 / $1,099 / $844 |
| Start-up, hot dog grill (+ cooler) | $5,625 ($6,750) | $4,684 (about $5,480) with 120 units, permit and licence |

Midtown's office lunch with the proposal: 43 customers/h, 20.8 served, $325 an hour; Hell's Kitchen's late nightlife $252 an hour.

Variants I tried and rejected [calc]:

| Variant | Best route, weekday dry | Why not |
|---|---|---|
| Only stock 120 and faster service (hot dog 2.5 min, coffee and bakery 2 min), default price | $1,008 | Still a delivery day |
| Pricing + stock 120 + `passersPerHour` 40 | $2,119 weekday, $2,464 weekend | Weekend at 95% of S, and footfall is shared with the truck |
| Pricing + stock 120 + hot dog 2.5 min, coffee and bakery 2 min | $2,096 | Outside the design's 12 to 20 customers an hour |

### What the proposal doesn't fix

- **Staying put still pays almost as well as moving** ($1,710 against $1,874 in Midtown). Pushing a cart covers 240 m an in-game hour, so a move costs about two hours. If moving should pay clearly more, the levers are the walking speed (pushing at a run, 360 m an hour, leaves the best lunch + evening route unchanged because the move falls in the 14:30 to 17:00 gap) or a smaller lunch overshoot at mixed spots (`classifier.maxAreas` 1, or a lower `edgeWeight`). I'd decide this in game.
- **Nightlife needs zones.** The late peak is the cart's second-best daypart and almost no real spot has it (see the classifier table).
- **The coffee + bakery cart stays below a delivery day** ($778 best). At a 3-minute service its margins ($3 to $5.66 a serve with index pricing) can't reach the band. As the second slot next to a grill it works: hot dog + coffee makes $1,931.
- **The rest of a box.** Without change 3, a restock means buying a whole box. If the remainder can't be kept, every restock costs $2,225 for 120 hot dogs and the cart loses money. If the player may hold the box at the cart and reload on the spot, restock walks disappear: nothing on the proposal's lunch + evening route, about +$200 on the three-pitch routes.

### Room for the truck, and Milestones 3 to 7

A cart run with two people at the window and 240 units approximates a truck with the player and one hire. With pricing at the index it makes $2,812 on the best weekday route [calc]; less a hire at a job-board wage (cashier base $16 × (20^1.05/100 + 1) = $19.70 an hour [code, `EmployeeHelper`; BC data], 12 hours = $236) and a little fuel ($1.30 a litre [code, `GasStationOverlay`]), about **$2,550**: the starter shop's level, the design's target for the truck. The second person only helps at pitches whose demand exceeds 20 an hour, so outside Midtown and Hell's Kitchen the truck-like run made no more than the cart. The truck's own room should come from window capacity, stock and catering, not from footfall.

What later milestones add on top of the cart's band:

- **Milestone 3, queues.** Customers above the window's rate wait up to 20 minutes instead of being lost. At a capped pitch this moves sales later, it doesn't add many. Worth a few percent.
- **Milestone 4, truck and licence.** The fees above. Tax: `TaxPatch` adds cart sales to taxable income at the game's rate (10% by default), so a cart day loses 10% against untaxed delivery pay.
- **Milestone 5, hires.** A hire running the cart alone earns the cart's sales less about $20 an hour. A hired cart on a good lunch and evening is worth about $1,874 - 12 × $19.70 = $1,640 a day at Midtown pricing, so two hired carts would pass a starter shop. The permit cap (default 4) is the brake; consider a lower cap on carts than on trucks.
- **Milestone 6, regulars (+30% at a pitch after six weeks) and premium spots.** They raise demand, which only pays where the window isn't full: evenings, weekends and weaker neighbourhoods. On the proposal that is roughly +10 to 20% on a route [assumption]. Keep the cart's band with regulars in mind: the best route would reach about $2,100, 80% of S.
- **Milestone 7, catering.** Trucks only, so it widens the gap between cart and truck, which is what the ladder wants.

## 5. The Economy draft on `core-m3-m7`

The draft in the worktree `friendly-kepler-2476dc` (`Core/Economics/Economy.cs`, `EconomyRules.cs`, `data/rules.json`, `tests/Economics/RouteDay.cs`) was read, not changed.

**Checked and sound:**

- `HourlyWageCents`: base × (skill^1.05 / 100 + 1) × wage multiplier matches `EmployeeHelper` [code].
- Fuel at $1.30 a litre matches `GasStationOverlay.PricePerLiter` [code].
- The arrivals formula in `ReferenceShopIncome`, building capacity × (base promotion + 0.75 × traffic / 100) × hour factor, capped, has the right shape (`CustomerEntriesCalculatorRetail`; Big Copilot's model uses the same).

**Replaced:**

- `ReferenceShopIncome` leaves out the day factor, each product's sales ratio (hot dog 0.66), the neighbourhood's demand, satisfaction and the amount each customer buys. Big Copilot's model has all of them and is validated, so the yardstick above uses it. The draft's guessed shop ($1,432 a day) lands between Big Copilot's staffed starter ($126 to $2,004) and the owner-run one ($1,719 to $3,597).
- Guessed inputs in `rules.json`: building capacity 20 (an A unit is 15 [BC data]), equipment $2,500 (a hot dog starter needs $4,775 in register, counters, grill and fridge), hot dog wholesale $3.50 ($4.45), rent $90 a day (the 111 A units' median is $34, range $20 to $147 [BC model]).
- `WholesaleCents` multiplies by 1.05. That factor is in `BusinessHelper` for an estimate; buying in person uses the wholesaler's own price index, 90 or 110 (`PlayerItemPurchaser` [code]; opener research). This study uses $4.45 (index 100) and shows the 1.3 × 110 case as a sensitivity.
- `RouteDay` prices: burger $15.00 (game $12.50), coffee $4.50 ($3.90), soda $3.50 ($3.00), wholesale at 30% of price (hot dog $3.50, coffee $1.35; game $4.45, $0.90). It also uses pure archetype spots (Midtown office, Hell's Kitchen nightlife), which real spots rarely reach. Its cart day of $1,062 against this study's $837 comes mostly from those two differences.
- Walking pace: `RouteDay` notes 1.2 m/s for walking. The player's default is a 4 m/s jog [code].

## Assumptions

1. Default difficulty is `GameVariables`' code defaults. The Normal preset may differ.
2. Delivery: $300 a job, $20 tips, 3 to 4 jobs a day, from Peter's play on harder settings. The job pay doesn't depend on the difficulty sliders in the code I read.
3. Map scale 1.0 / 1.49 / 3.2 m per map unit; streets add 30%; doors are on the outline boxes.
4. The player pushes the cart at the default jog, 4 m/s.
5. Restocking: walk to the nearest wholesaler, 10 minutes to buy and load. All six wholesalers stock hot dogs, coffee, croissants and soda (unverified; Total Produce Trading may only sell produce).
6. Market demand 100 (no shop sells the cart's items in the neighbourhood), and no cheaper shop sets the reference price.
7. May, spring: footfall 1.0, no season factors. The Hamptons' summer weekends would be better.
8. The cart day runs 07:00 to 23:00 for single spots and 06:00 to 23:00 or 01:00 for routes, the player's whole waking day, like a delivery day.
9. Store comparison: Big Copilot's model on one late-game city, A units, 06:00 to 22:00, satisfaction 95, no marketing for starters.
10. Business types: fixed AI businesses plus one save's rivals. A new game has different rivals, so nightlife spots will differ.
11. The best route is the best the search found (top 20 per pitch), not a proven optimum.

## Open questions for Peter

1. **Pricing.** Should the cart price like a shop (default × the neighbourhood's index), or get a price setting? Either works for the band; the index rule needs no UI.
2. **The rest of a box.** Load only what fits and charge per unit (my proposal), let the player keep the rest in hand, or let it ride in the cart as a sealed reserve? Each gives a different restock loop and a $2,000 swing in start-up cost.
3. **Can the player hold a box and let the cart sell?** If yes, restocking needs no walk and the 120-unit limit only matters between pitches.
4. **Moving vs staying.** With these numbers a mixed office/residential spot all day earns 90% of the best route. Is that fine, or should moving pay more?
5. **The Food Safety course price** isn't in the code. Is it cheap enough that it doesn't add to the entry cost?
6. **Delivery check.** What does one delivery job take in game hours, and did the $300 and $20 tips hold on default settings? The band's floor rests on it.
7. **Coffee + bakery as a standalone cart.** Leave it as a weak budget start ($4,400 today, best route $778 with the proposal), or lift it?
8. **Hired carts.** Should the permit cap differ for carts and trucks, so a fleet of hired carts doesn't pass the shop before the truck exists?
