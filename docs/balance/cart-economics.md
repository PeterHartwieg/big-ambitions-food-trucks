# Cart economics: a calculated balance study

Written 8 October 2026, revised the same day with Peter's answers: the cart prices like a shop, it keeps 80 units and buys per unit at the wholesaler's shelves, refills during the day are expected, fees are undecided, and moving should pay. Nothing here was played. Cart figures come from running the mod's own Core demand model (`tools/balance/`), store figures from Big Copilot's validated store model, delivery figures from the game code and Peter's play.

## The answer

All figures: one player, default difficulty, a weekday without rain, the player's 16-hour day (09:00 to 01:00 for the cart).

| | Profit a day | Cost to start | Source |
|---|---|---|---|
| Delivery driving, 3 to 4 jobs (D) | $960 to $1,280, mid $1,120 | $0 | [code], [Peter] |
| Cart today, default price, best route in the city | $1,018 | $3,756 (grill) to $4,554 (grill + cooler) | [calc] |
| Cart today, price set at the ceiling, best route | $2,038; staying at one spot earns 83% of that | same | [calc] |
| **Cart with the proposed curves, price at the ceiling, best route** | **$1,962**; staying earns **64%** | same | [calc] |
| same, default price (a player who sets nothing) | $1,013; staying 66% | same | [calc] |
| Small starter shop run by the player (S), city median | about $2,600 ($1,720 to $3,600) | $7,100 to $9,400 | [BC model] |

**Verdict.** At the ceiling price, today's data already puts the best cart day at 1.8 D, but staying put in one mixed spot earns 83% of the best route: moving doesn't pay. At the default price the cart is about a delivery day. The proposed footfall curves keep the best route in the band ($1,962, 1.75 D, 75% of S) and drop staying to 64% of it. A careless player at the default price earns about one delivery day; a player who sets the ceiling earns 1.9 times that.

**Tune on the ceiling price.** The ceiling is the most every customer class accepts: reference × the neighbourhood's average price index. A player who sets prices by hand reaches it, and so does a full-skill pricing manager. The default price is the floor a careless player gets. The gap between them is the neighbourhood index: ×1.25 in Industry City, ×1.68 in Midtown, about ×1.9 on the best route's profit.

**The patch** (section 4 has the full list): `passersPerHour` 30 → 22, and in `demand.json` each area's footfall lowered outside its own peak, with nightlife's peak raised (evening 1.5 → 2.0, late 2.0 → 3.0). Office lunch stays at 3.0. Nothing changes in `economy.json`.

## Labels

- **[code]**: read in the 1.0 decompile of 23 September 2026 (`BigAmbitionsMCP/lib/decompiled-1.0-build-2026-09-23`). Unverified in game.
- **[BC data]**: Big Copilot's data files (`ba_store_rules.json`, `ba_item_prices.json`, `ba_buildings.json`, map outlines, the wiki data), extracted from the game and checked against saves.
- **[BC model]**: Big Copilot's Open a store model (`template/open-store-model.js`), validated against 94 of the player's real shops: median actual ÷ model 0.96, p25 0.80, p90 1.08 (`docs/open-a-store-scope.md`, "Expected profit").
- **[mod data]**: this repo's `data/*.json` or Core.
- **[seen]**: seen in game (Milestone 1 session, `docs/evidence/milestone-1-session-1.md`).
- **[Peter]**: Peter's play, recorded in the HART workspace's opener research of 23 September 2026, or his answers of 8 October.
- **[calc]**: this study's tool.
- **[assumption]**: a judgement of mine, listed at the end.

"Default difficulty" means the code defaults in `GameVariables`: customer promotion 0.5, market prices 1.0, wages 1.0, tax 10% [code]. The preset Normal is asset data and wasn't checked.

## 1. Delivery driving

How it works [code, `DeliveryJobStartController`, `DeliveryJobHelper`, `DeliveryDriverMission`]:

- A job pays `deliveryReward` per completed destination. The code default is $100; the asset value isn't in the code, but Peter's Episode 1 footage shows $300 for a three-stop job [seen, tips video script]. Destinations per job default to 3.
- Tips: a roll per completed destination from the location's tip table, better for a fast delivery (`DeliveryJobTipsConfig`). The table is asset data. Peter's average was about $20 a job [Peter].
- The game spawns a free delivery van with a full tank and a hand truck (`OnAcceptJob`: `fuel = maxFuel`, `zeroSellingPrice`). No vehicle, fuel or deposit to pay. Start-up cost $0.
- Each start location gives one job a day (`deliveryJobLocationsDoneToday`).
- The time limit is the straight-line route length × `minutesPerMeter`, clamped to `minutesMin` to `minutesMax`, all asset data.
- Job pay is wages, not business income, so it's untaxed [Peter's opener research].

| Delivery day | Pay | Tips | Total | Time |
|---|---|---|---|---|
| 3 jobs | $900 | about $60 | **$960** | most of a waking day [Peter: "Three to four delivery jobs fit into a day around sleep"] |
| 4 jobs | $1,200 | about $80 | **$1,280** | a full waking day |

I use D = $1,120 (3.5 jobs) below.

## 2. A small vanilla shop

Big Copilot's model, run unchanged by `tools/balance/store/store_yardstick.cjs` on the city of a late-game 1.0 save (build 3682). The script changes only the difficulty (to the defaults above), the cost of goods (the plain wholesale price: import index 1.0, no purchasing agent), and drops the player's own shops so satisfaction is the model's well-run default of 95. Rent and deposit are Big Copilot's rent estimate [BC model]. "Save's sellers" uses that save's count of shops per product and neighbourhood; "no other sellers" sets every count to 0, the best case for a newcomer. Shops charge the model's price: what every customer accepts, the ceiling.

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
- Cost to open: register $900, two counter tops at $400, the product's machine (hot dog grill $2,200, drinks fridge $875, industrial grill $1,400, cheap coffee machine $400, bakery showcase $895), one box of each product at wholesale (a shop's shelves take whole boxes: 500 hot dogs $2,225), the deposit and the first day's rent [BC data, opener research].
- The full fast food outfit (whole range, 24/7, best marketing) has a median of $21,900 a day on a $53,700 investment. That's where shops go later; the cart is never compared with it.

**The yardstick S:** about **$2,600 a day** (the median of the six owner-run medians, which run from $1,720 to $3,600), **$7,100 to $9,400** to open.

## 3. The cart

### How the tool computes a cart day

`tools/balance/` runs Core unchanged: `AreaClassifier`, `DemandModel`, `PriceCurve`, `ServiceRate`, `SalesRate`. `Cart.Plan` copies `CartDemand.Plan` from the game side, with each game read replaced by an input:

| Game side reads | The tool uses |
|---|---|
| Buildings within the classifier radius, by distance to their doors (`World.Read`) | Each building's outline box from Big Copilot's map, in metres; distance to the nearest point of the box. One pitch in front of every building, 883 in all |
| Business types | Fixed AI businesses (wholesalers, gas stations, banks...) and one late-game save's rival businesses; the player's own left out (`city.json`) |
| Market demand, only for `isADemandedProduct` items | 100 (no shop sells the item in the neighbourhood) unless stated |
| Promotion = `baseCustomerPromotionMultiplier` / 0.5 | 1.0 |
| Price | One of the pricing cases below |
| Reference = min(default price, cheapest shop in the neighbourhood) | The default price |
| Staff 1, stock 80 units, 2 equipment slots | `data/economy.json` |

Prices and wholesale prices: hot dog $11.65 [seen, BC data] / $4.45, coffee $3.90 / $0.90, croissant $7.20 / $2.91, soda $3.00 / $0.50 [BC data]. May (footfall 1.0), Wednesday or Saturday. Sales run in 5-minute steps.

**Map scale.** Big Copilot's map draws Manhattan, Industry City and the Hamptons in separate frames. I set the scale from same-sized buildings: 1.0 m per map unit for Manhattan (a 225 m² unit's outline says at least 0.85, a 2,000 m² cinema's at most 1.36), 1.49 for Industry City and 3.2 for the Hamptons. Distances could be off by 15 to 35%.

**Walking.** One in-game minute per real second at normal speed (`GameManager`), and the player jogs at 4 m/s by default (`ThirdPersonCharacter`, `GlobalReferences`) [code]: pushing the cart covers 240 m per in-game hour. Streets add 30% to straight lines [assumption].

### Pricing: three cases

How vanilla shops price [code]:

- A shop sells at the item's default price unless the player sets a price in BizMan or a pricing manager covers the district.
- Every customer class accepts up to reference × max(class index, the neighbourhood's average index), class indices 1.2, 1.4, 1.7 (`CitizenHelper.MaxAcceptableRelativePrice`). The most all classes accept is the minimum over the classes (`ItemHelper.CalculateMaxAcceptablePriceByNeighborhood`), which is reference × the neighbourhood's index everywhere, since every index is at least 1.246. A player monopoly adds 0.3; the cart is no provider, so it never gets that bonus.
- A pricing manager (`PricingManagerHelper.ComputeSuggestion`, run daily at 08:00) sets floor + (ceiling − floor) × capture. Floor = the reference price, ceiling = the most all classes accept, capture = Lerp(`minSkillCapture`, 1, skill) less a random jitter that vanishes at full skill. `minSkillCapture` is asset data. `CalculateOptimalPriceByNeighborhood` (default × index) gives the same number as the ceiling here, but the manager doesn't call it.

So the study models (a) **default price**: a player who sets nothing; (b) **ceiling**: reference × index, a player who sets the most every class accepts, or a full-skill manager; (c) **a pricing manager** at capture 0.5 and 0.75. Below and above the ceiling the mod's own price curve decides how many buy [mod data].

Best lunch + evening route, hot dog grill + cooler, proposed curves [calc]:

| Price | Midtown hot dog | Midtown office lunch: share who buy, profit/h | Best route | ÷ ceiling |
|---|---|---|---|---|
| (a) default | $11.65 | 100%, $157 | $818 | 49% |
| (c) manager, capture 0.5 | $15.62 | 100%, $241 | $1,238 | 74% |
| (c) manager, capture 0.75 | $17.61 | 100%, $283 | $1,455 | 87% |
| ceiling × 0.9 | $17.64 | 100%, $284 | $1,458 | 87% |
| **(b) ceiling** | **$19.60** | **100%, $325** | **$1,672** | 100% |
| ceiling × 1.1 | $21.55 | 7%, $35 | $527 | 32% |
| upper class's maximum, reference × max(1.7, index) | $19.81 | 73%, $323 | $1,543 | 92% |

Above the ceiling sales collapse: 10% over loses 90% of buyers in Midtown. The game is harsher still: there, 10% over the ceiling is above what every class accepts, so nobody buys. A price setting needs to show the ceiling.

### Stock and refills

The cart keeps 80 units. The player pushes it into a wholesaler and buys only what fits, at the wholesale price per unit [Peter; the build charges only what goes in at the shelves]. All six wholesalers sell hot dogs, coffee, croissants and soda [BC data, wiki: "can be purchased from any wholesale location"]:

| Wholesaler | Address | Neighbourhood |
|---|---|---|
| StockCo | 1 Sixth Street | Midtown |
| Metro Wholesale | 18 First Street | Hell's Kitchen |
| NY Distro Inc | 37 First Street | Garment District |
| Total Produce Trading | 4 Sixth Avenue | Murray Hill |
| Hudson Wholesale | 13 Twelfth Street | Lower Manhattan |
| Titans of Industry Supply | 4 25th Street | Industry City |

None in the Hamptons. In person, a unit costs wholesale × import price index × market multiplier × the store's own index, 90 (NY Distro, Hudson, Titans) or 110 (Metro, StockCo) [code, `PlayerItemPurchaser`; opener research]. The study uses $4.45 for a hot dog (index 1.0, store 100).

A refill is modelled as: push the cart from the pitch to the nearest wholesaler, 10 minutes to buy the gap, push back or on to the next pitch. The median pitch in Manhattan is 160 to 200 m from its nearest wholesaler, so a refill round trip takes about two in-game hours; Industry City pitches about 390 m, about four and a half hours [calc]. A day refills when the cart sells out and at least half an hour of selling is left. A route goes straight to the next pitch when at least half the stock is left, otherwise through a wholesaler. The best days below refill once or twice.

### Hourly rates by spot type, today's curves

A pure spot of each area type, hot dog grill + cooler, weekday, dry, unlimited stock, **default price**. Each cell is the model's customers per hour (before price and the window) / profit per hour (sales at the window's limit, price minus wholesale) [calc]. At the ceiling, multiply the profit by about 1.4 (Industry City, Lower Manhattan, Garment District) to 2.1 (Midtown).

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

One person serves 20 hot dogs an hour (3 minutes each [mod data]), so a pitch tops out at about $155 an hour at the default price and $325 at Midtown's ceiling. Office lunches want 25 to 43 customers an hour and all hit it. Hell's Kitchen's late nightlife is the only other daypart that fills the window.

The coffee + bakery cart's best is early and morning at offices and industrial spots, 15 to 37 customers an hour, but its ceiling is about $66 an hour at the default price: the same 3-minute service at a $3.00 to $4.29 margin.

### What the classifier makes of the real city

One pitch in front of each building; every building whose outline is within 50 m votes. A spot is "mostly X" at a share of 0.5 or more [calc].

| Neighbourhood | Spots | Mostly office | Mostly residential | Mostly retail | Mostly industrial | Mostly nightlife | Highest office share |
|---|---|---|---|---|---|---|---|
| Midtown | 94 | 33 | 22 | 36 | 0 | 3 | 0.82 |
| Lower Manhattan | 117 | 16 | 61 | 21 | 23 | 0 | 0.76 |
| Murray Hill | 103 | 7 | 36 | 57 | 4 | 1 | 0.65 |
| Hell's Kitchen | 146 | 5 | 107 | 34 | 0 | 0 | 0.69 |
| Garment District | 165 | 6 | 71 | 84 | 6 | 0 | 0.57 |
| Industry City | 200 | 0 | 73 | 83 | 44 | 0 | 0.26 |
| Hamptons | 58 | 0 | 28 | 29 | 0 | 1 | 0.00 |

Nightlife is rare: only theatres, the casino boat and rival nightclubs vote for it. Hell's Kitchen, the design's nightlife district, has no nightlife spot at all. This matters for moving (below): an evening pitch is what makes a route pay.

### Today: staying against moving

The best single spot all day against the best route that splits the same 16 hours into two or three pitches (changing at 14:30, at 17:00, or at 14:30 and 21:00), hot dog grill + cooler, weekday, dry. Staying put is one of the routes the search may pick, so a ratio over 100% is only the search's rounding [calc].

| | Default price: stay / route | Ceiling price: stay / route / stay ÷ route |
|---|---|---|
| Midtown | $814 / $979 | $1,693 / $2,038 / 83% |
| Lower Manhattan | $591 / $649 | $832 / $914 / 91% |
| Murray Hill | $536 / $883 | $828 / $1,363 / 61% |
| Hell's Kitchen | $794 / $915 | $1,328 / $1,531 / 87% |
| Garment District | $845 / $888 | $1,227 / $1,288 / 95% |
| Industry City | $667 / $778 | $928 / $1,082 / 86% |
| Hamptons | $386 / $482 | $776 / $969 / 80% |
| **City** | **$845 / $1,018** | **$1,693 / $2,038 / 83%** |

Weekend, ceiling: city $1,700 / $2,087 (81%).

Why staying works today: a spot half office and half residential fills the window at lunch with half the office crowd (office lunch demand is twice the window's rate), then gets the residential evening at 1.0. Moving costs two hours of pushing and gains little.

### Cost to start

The cart ($1,200 [seen, dealer dialog]), its equipment [BC data], and 80 units at wholesale, split by what a Midtown lunch sells [calc]:

| Menu | Equipment | First 80 units | Total | In delivery days |
|---|---|---|---|---|
| Hot dog grill | $2,200 | $356 | **$3,756** | 3.4 |
| Hot dog grill + cooler | $3,075 | $279 | **$4,554** | 4.1 |
| Hot dog grill + coffee machine | $2,600 | $323 | **$4,123** | 3.7 |
| Coffee machine + display case | $1,295 | $110 | **$2,605** | 2.3 |

Optional fees, not counted in the band: a vehicle permit of $500 and a vendor licence of $250 per 60 days (about $4 a day) would add $750 to the first day.

## 4. Verdict and tuning

### The band

Tuned on the ceiling price, weekday, dry.

| Rung | Profit a day | Cost to start |
|---|---|---|
| Delivery day (D) | $1,120 | $0 |
| **Cart: best route** | **1.5 to 1.8 D ($1,700 to $2,000), at most 75% of S ($1,950)** | **3 to 4.5 D ($3,400 to $5,000)** |
| **Cart: best single spot all day** | **at most 70% of the best route** | |
| Cart at the default price | about 1 D | |
| Small starter shop run by the player (S) | $2,600 | $7,100 to $9,400 |
| Truck with the player and one hire (Milestones 4 and 5) | above the cart's best, up to S | |

### Where the cart sits now

- **Profit.** At the ceiling price the best route makes $2,038 (1.8 D, 78% of S), just over the band. At the default price it makes $1,018 (0.9 D).
- **Moving doesn't pay.** Staying makes 83% of the best route.
- **Start-up is already in the band.** With per-unit buying it costs $3,756 to $4,554 (3.4 to 4.1 D, about half a starter shop).

### Proposed changes

**Rules (game side, none in Core data):**

1. **The cart prices like a shop.** Default price unless the player sets one; a price per product; a pricing manager covering the district applies. `PricingManagerHelper.IsManageableBusiness` only takes rented buildings, so the mod has to apply a district plan's suggestion to its carts itself (`PricingManagerPlan.cachedSuggestions`, by item and district). The price setting should show the ceiling (reference × the district's index), because 10% over it loses almost every buyer.
2. **Stock 80 per cart (160 per truck), bought per unit at the shelves,** as built. Refill trips are part of the day.
3. **Fees stay optional.** If they come, a $500 permit and a $250 licence per 60 days leave the band unchanged (under $5 a day).

**Data, as a patch over `data/demand.json`** (`tools/balance/proposed.json`). Each area keeps its peaks; footfall outside them drops, so a spot that mixes two areas no longer earns well all day. Nightlife's peak rises, so the evening pitch is worth the walk. `passersPerHour` comes down to keep the best route inside the band.

| Key | Today | Proposed |
|---|---|---|
| `passersPerHour` | 30 | **22** |
| `areas.office.footfall` early / morning / lunch | 0.8 / 1.2 / 3.0 | unchanged |
| `areas.office.footfall` afternoon / evening / late | 0.5 / 0.6 / 0.1 | **0.2 / 0.2 / 0.05** |
| `areas.residential.footfall` early / morning | 0.6 / 0.7 | unchanged |
| `areas.residential.footfall` lunch / afternoon / evening / late | 0.5 / 0.5 / 1.0 / 0.4 | **0.3 / 0.3 / 0.5 / 0.2** |
| `areas.retail.footfall` afternoon | 1.3 | unchanged (its peak) |
| `areas.retail.footfall` early / morning / lunch / evening / late | 0.1 / 0.5 / 1.2 / 1.0 / 0.2 | **0.05 / 0.3 / 0.8 / 0.5 / 0.1** |
| `areas.industrial.footfall` early / lunch / late | 1.5 / 1.5 / 1.0 | unchanged (shift peaks) |
| `areas.industrial.footfall` morning / afternoon / evening | 0.4 / 0.3 / 0.5 | **0.2 / 0.15 / 0.3** |
| `areas.nightlife.footfall` morning / lunch / afternoon | 0.1 / 0.4 / 0.3 | **0.05 / 0.15 / 0.1** |
| `areas.nightlife.footfall` evening / late | 1.5 / 2.0 | **2.0 / 3.0** |
| `areas.waterfront`, day factors, appetites, neighbourhoods, `night` dayparts | | unchanged |
| `data/economy.json` | | unchanged (stock 80 / 160, service minutes) |

The shape stays: offices peak at lunch at 3.0 and in the morning, nightlife late, industrial at shift changes, retail in the afternoon, residential mornings and evenings. The design's footfall table says 1.0 is an average open hour city-wide; after the patch the average is lower, and the table should be updated with the numbers if the patch is taken.

Rates at a pure spot, before → after, ceiling price, weekday dry (customers per hour / profit per hour) [calc]:

| Spot | Lunch | Afternoon | Evening | Late |
|---|---|---|---|---|
| Midtown office | 43 / $325 → 31 / $325 | 2 / $27 → 1 / $8 | 5 / $75 → 1 / $18 | 1 / $13 → 0 / $5 |
| Midtown residential | 6 / $101 → 3 / $44 | 2 / $24 → 1 / $11 | 7 / $113 → 3 / $41 | 3 / $47 → 1 / $17 |
| Midtown retail | 15 / $241 → 8 / $118 | 5 / $63 → 3 / $46 | 7 / $113 → 3 / $41 | 2 / $23 → 1 / $9 |
| Midtown nightlife | 4 / $63 → 1 / $17 | 1 / $11 → 0 / $3 | 9 / $132 → 9 / $129 | 12 / $181 → 13 / $200 |
| Hell's Kitchen nightlife | 3 / $33 → 1 / $9 | 1 / $7 → 0 / $2 | 10 / $117 → 9 / $114 | 23 / $252 → 26 / $252 |
| Industry City industrial | 13 / $130 → 9 / $96 | 0 / $4 → 0 / $1 | 3 / $26 → 1 / $11 | 12 / $124 → 9 / $91 |

Midtown's office lunch still fills the window (31 customers an hour against 20 served), so a pure office pitch keeps its lunch. A half-office spot no longer does.

### Before and after

Ceiling price, hot dog grill + cooler, the 16-hour day [calc]:

| | Today | Proposed |
|---|---|---|
| **Best route in the city, weekday dry** | $2,038 | **$1,962** (Midtown: lunch at 2 Fifth Street, office/retail, then the theatre corner at 6 Fifth Street from 14:30, one refill) |
| Best single spot, weekday dry | $1,693 (83%) | **$1,250 (64%)** |
| Best route / staying, weekend dry | $2,087 / $1,700 (81%) | $1,626 / $1,169 (72%) |
| Lunch + evening route, weekday dry / weekday rain / weekend rain | $1,741 / $1,373 / $924 | $1,672 / $1,076 / $775 |
| Default price: best route / staying, weekday dry | $1,018 / $845 | $1,013 / $663 (66%) |
| Two shops already sell hot dogs (market 71.5) | | $1,717, staying 65% |
| Truck-like run: two at the window, 160 units | | $2,484, about $2,250 after a hire |

Per neighbourhood, proposed, weekday dry, ceiling (stay / best route / ratio): Midtown $1,250 / $1,962 / 64%; Murray Hill $803 / $1,210 / 66%; Garment District $963 / $1,041 / 92%; Hell's Kitchen $854 / $979 / 87%; Lower Manhattan $760 / $829 / 92%; Hamptons $688 / $848 / 81%; Industry City $711 / $673 (staying is the best plan there).

Against the band: the best route is 1.75 D and 75% of S; staying is 64% of the best route; the default price gives 0.9 D; start-up is 3.4 to 4.1 D. The truck-like run makes about $2,250 after its hire, 15% over the cart's best and 87% of S.

### What the patch doesn't fix

- **Moving pays only where there's an evening pitch.** In Midtown and Murray Hill (theatres, the casino boat) the route wins clearly. Hell's Kitchen, Garment District, Lower Manhattan and Industry City have no nightlife spot, so their best route is still within 10% of staying. The design's hand-drawn zones and premium spots are the fix; they should come before Peter's balance check.
- **Refills are part of why moving pays.** If a refill took no walk, staying would rise to 77% of the best route. Keep refills at the wholesaler.
- **Weekends are flatter.** Offices go quiet and retail spreads over the day, so staying earns 72% of the best route on a Saturday. I think that's right for a weekend.
- **The coffee + bakery cart stays weak.** Best route $621, best spot $470: its margins at a 3-minute serve can't reach the band. As a grill's second slot it works: hot dog + coffee, breakfast + lunch + evening, $1,926.
- **Classifier blending.** Keeping one area per spot (`classifier.maxAreas` 1) also separates moving from staying (55% in Midtown), but the design wants blended spots, so I left it out.

### Room for the truck, and Milestones 3 to 7

The truck-like run (two people at the window, 160 units, proposed curves) makes $2,484 on the best weekday route; less a hire at a job-board wage (cashier base $16 × (20^1.05/100 + 1) = $19.70 an hour [code, `EmployeeHelper`; BC data], 12 hours = $236) and a little fuel ($1.30 a litre [code, `GasStationOverlay`]), about **$2,250**: above the cart, below S. The second person helps only where demand beats 20 an hour, so the truck's further room should come from its four slots, catering and premium spots.

- **Milestone 3, queues.** Customers above the window's rate wait up to 20 minutes instead of leaving. At a full pitch this moves sales later more than it adds them: a few percent.
- **Milestone 4, truck and licence.** `TaxPatch` adds cart sales to taxable income at the game's rate (10% by default): the best route nets $1,766, still 1.6 D against untaxed delivery pay.
- **Milestone 5, hires.** A hired cart on the best route earns about $1,962 − 12 × $19.70 = $1,726. Two hired carts would pass a starter shop, so the permit cap (default 4) is the brake; consider a lower cap for carts than for trucks.
- **Milestone 6, regulars (+30% at a pitch after six weeks), premium spots, live venue crowds.** They add demand, which only pays where the window isn't full: evenings, weekends and weaker districts. Regulars reward a fixed route, which helps moving. I'd expect the best route to reach about $2,200 [assumption], 85% of S; check it at the Milestone 6 balance session.
- **Milestone 7, catering.** Trucks only, so it widens the gap to the truck.

## 5. The Economy draft on `core-m3-m7`

The draft in the worktree `friendly-kepler-2476dc` (`Core/Economics/Economy.cs`, `EconomyRules.cs`, `data/rules.json`, `tests/Economics/RouteDay.cs`) was read, not changed.

**Checked and sound:**

- `HourlyWageCents`: base × (skill^1.05 / 100 + 1) × wage multiplier matches `EmployeeHelper` [code].
- Fuel at $1.30 a litre matches `GasStationOverlay.PricePerLiter` [code].
- The arrivals formula in `ReferenceShopIncome` (building capacity × (base promotion + 0.75 × traffic / 100) × hour factor, capped) has the right shape (`CustomerEntriesCalculatorRetail`; Big Copilot's model uses the same).

**Replaced:**

- `ReferenceShopIncome` leaves out the day factor, each product's sales ratio (hot dog 0.66), the neighbourhood's demand, satisfaction and the amount each customer buys. Big Copilot's model has all of them and is validated, so the yardstick uses it. The draft's guessed shop ($1,432 a day) lands between Big Copilot's staffed starter ($126 to $2,004) and the owner-run one ($1,719 to $3,597).
- Guessed inputs in `rules.json`: building capacity 20 (an A unit is 15 [BC data]), equipment $2,500 (a hot dog starter needs $4,775 in register, counters, grill and fridge), hot dog wholesale $3.50 ($4.45), rent $90 a day (the 111 A units' median is $34, range $20 to $147 [BC model]).
- `WholesaleCents` multiplies by 1.05. That factor is in `BusinessHelper` for an estimate; buying in person uses the wholesaler's own index, 90 or 110 (`PlayerItemPurchaser` [code]).
- `RouteDay` prices: burger $15.00 (game $12.50), coffee $4.50 ($3.90), soda $3.50 ($3.00), wholesale at 30% of price (hot dog $3.50, coffee $1.35; game $4.45, $0.90). It uses pure archetype spots, which real spots rarely reach, and the default price.
- Walking pace: `RouteDay` notes 1.2 m/s for walking. The player's default is a 4 m/s jog [code].

## Assumptions

1. Default difficulty is `GameVariables`' code defaults. The Normal preset may differ.
2. Delivery: $300 a job, $20 tips, 3 to 4 jobs a day, from Peter's play on harder settings.
3. Map scale 1.0 / 1.49 / 3.2 m per map unit; streets add 30%; doors are on the outline boxes.
4. The player pushes the cart at the default jog, 4 m/s; a refill takes 10 minutes in the store.
5. Hot dogs cost $4.45 at every wholesaler (index 1.0, store 100); the real price is 10% either side.
6. Market demand 100 (no shop sells the cart's items in the neighbourhood), and no cheaper shop sets the reference price. A shop selling below the default price lowers the reference, and with it the ceiling.
7. May: footfall 1.0, no season factors.
8. The day is 16 hours, 09:00 to 01:00, for both staying and routes. Routes change pitch at 14:30, at 17:00, or at 14:30 and 21:00; the best route is the best the search found (top 20 per pitch plus the five best all-day spots), not a proven optimum.
9. Store yardstick: Big Copilot's model on one late-game city, A units, 06:00 to 22:00, satisfaction 95, no marketing.
10. Business types: fixed AI businesses plus one save's rivals. A new game has different rivals, so its nightlife spots differ.
11. Pricing manager capture 0.5 and 0.75 stand in for a typical analyst; `minSkillCapture` is asset data.

## Open questions for Peter

1. **Evening pitches outside Midtown.** Moving pays only where the city has a nightlife spot. Should the hand-drawn zones (Hell's Kitchen theatre blocks and others) come before your balance check, so routes pay in more than two districts?
2. **Weekends.** Staying earns 72% of the best route on a Saturday, against 64% on weekdays. Fine, or should weekends separate as much?
3. **Pricing manager on carts.** The game's manager only reprices rented buildings. Should the mod apply a district plan to carts automatically, or only when the player assigns the cart to it?
4. **Delivery check.** How many in-game hours does one delivery job take, and does it pay $300 plus about $20 in tips on default settings? The band's floor rests on it.
5. **Fees.** If you do add a permit and a licence, are $500 and $250 per 60 days about right? And what does the Food Safety course cost?
6. **Coffee + bakery as a standalone cart.** Leave it as a cheap but weak start ($2,605 to start, best route $621), or lift it?
7. **Hired carts.** Should the permit cap differ for carts and trucks, so a fleet of hired carts doesn't pass the shop before the truck exists?
