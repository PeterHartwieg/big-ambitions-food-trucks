# Cart economics: a calculated balance study

Written 8 October 2026 and revised twice that day with Peter's answers. Second pass: the cart prices like a shop, keeps 80 units bought per unit at the wholesaler's shelves, refills during the day, fees are undecided, and moving should pay. Third pass: coffee is the step after hot dogs, at the industrial coffee machine; coffee gets its own crowds (commuters, subway entrances, parks); nightlife comes from buildings (nightclubs, theatres, cinemas); hand-drawn zones only for parks, waterfront and tourist stretches. Nothing here was played. Cart figures come from running the mod's own Core demand model (`tools/balance/`), store figures from Big Copilot's validated store model, delivery figures from the game code and Peter's play.

**Fourth pass, additive crowds (8 October): see [section 6](#6-fourth-pass-additive-crowds). Its numbers replace the ladder and district table below, which were computed on the share model. Peter has dropped the staying target: finding a good spot is rewarded, and staying there is fine.**

## The answer

One player, default difficulty, a weekday without rain, a 16-hour day (hot dogs 09:00 to 01:00, coffee 06:00 to 22:00, the truck stand-in 07:00 to 23:00). "Ceiling" is the most every customer class accepts, "default" the item's default price.

**The ladder** [calc; BC model for the shop]:

| Rung | Cost to start | Best route, ceiling | Best route, default price | Weekend, ceiling | Staying ÷ best route |
|---|---|---|---|---|---|
| Delivery driving, 3 to 4 jobs (D) | $0 | $960 to $1,280, mid $1,120 | same | | |
| Hot dog cart: grill + cooler (grill only $3,756) | $4,552 | **$1,946** (today $2,038) | $1,002 (today $1,018) | $1,753 | **64%** (today 83%) |
| Coffee cart: industrial coffee machine + display case | $7,135 | **$2,353** (today $928) | $1,210 (today $471) | $1,601 | **72%** (today 96%) |
| Small starter shop run by the player (S), city median | $7,600 to $11,400 | about $2,600 | | | |
| Truck stand-in: two at the window, 160 units, grill + coffee + display case + cooler | $10,400 before a truck price | $3,239, about $3,000 after a hire | $1,696 | $2,897 | 71% |

**Moving against staying per district**, proposed, weekday, ceiling price (staying ÷ best route; target 70% or less) [calc]:

| District | Hot dog cart today → proposed | Coffee cart today → proposed | Evening pitches today → proposed (spots with nightlife 0.3 or more) |
|---|---|---|---|
| Midtown | 83% → **64%** | 96% → 72% | 6 → 29 |
| Hell's Kitchen | 87% → **58%** | 100% → **69%** | 0 → 18 |
| Garment District | 95% → **69%** | 94% → **67%** | 0 → 15 |
| Murray Hill | 61% → 85% | 94% → **68%** | 1 → 7 |
| Lower Manhattan | 91% → 84% | 93% → 73% | 0 → 20 |
| Industry City | 86% → 98% | 100% → 85% | 0 → 12 |
| Hamptons | 80% → 78% | 94% → **68%** | 7 → 14 |
| **City** | 83% → **64%** | 96% → 72% | |

**Verdict.**

- **Hot dog cart.** With today's data it already earns a good amount at the ceiling ($2,038), but staying at one mixed spot earns 83% of that, and Hell's Kitchen has no evening pitch at all. With the patch the best route is $1,946 (1.7 D, 75% of S), staying drops to 64%, and moving pays in Midtown, Hell's Kitchen and Garment District.
- **Coffee cart.** Today it is a trap: $928 at best after a $6,900 start. With the patch it earns $2,353, 1.21 times the hot dog cart, and costs 1.6 times as much. A player who sets nothing (default price) gets $1,002 from hot dogs and $1,210 from coffee, so the ceiling roughly doubles the day.
- **The truck** stays clearly above both, at about $3,000 after its hire.

Two caveats:

- **Parks and subways decide the coffee lead.** Coffee's lead comes from subway-entrance and park pitches, which the city data can't place yet: they are archetype spots at each district's centre. Without them the coffee cart makes $1,436 (0.74 times the hot dog cart) and has no reason to move.
- **Weekends belong to hot dogs**, through the nightlife pitches.

**Tune on the ceiling price**, with the default price as the floor a careless player gets. The gap is the neighbourhood's index: ×1.25 in Industry City, ×1.68 in Midtown, about ×1.9 on the best route.

**The patch** (section 4 has the full list):

- **Footfall.** Sharper daypart curves, `passersPerHour` 22.
- **Nightlife from buildings.** Nightclub, theatre and casino votes raised, and cinemas vote nightlife.
- **Coffee crowds.** A morning commuter peak at offices, a new `transit` area for subway entrances, and busier park afternoons.
- **Coffee service.** Coffee serves in 1 minute and bakery in 2. Coffee's appetite rises through the day.
- **Fast food.** Lunch and late appetite trimmed to keep the hot dog cart in its band.
- **Equipment.** The coffee machine maps to the industrial machine only.
- **Two rules Core can't express yet.** A stock weight per item, so a coffee takes a quarter of a unit and a pastry half. A per-area appetite factor, so parks double coffee and ice cream.

## Labels

- **[code]**: read in the 1.0 decompile of 23 September 2026 (`BigAmbitionsMCP/lib/decompiled-1.0-build-2026-09-23`). Unverified in game.
- **[BC data]**: Big Copilot's data files (`ba_store_rules.json`, `ba_item_prices.json`, `ba_buildings.json`, map outlines, the wiki data), extracted from the game and checked against saves.
- **[BC model]**: Big Copilot's Open a store model (`template/open-store-model.js`), validated against 94 of the player's real shops: median actual ÷ model 0.96, p25 0.80, p90 1.08 (`docs/open-a-store-scope.md`, "Expected profit").
- **[mod data]**: this repo's `data/*.json` or Core.
- **[seen]**: seen in game (Milestone 1 session, `docs/evidence/milestone-1-session-1.md`).
- **[Peter]**: the mod author's own play notes (opener research of 23 September 2026) and his answers of 8 October.
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
| Coffee + croissants | save's sellers | $678 / $879 / $2,034 | $2,271 / $2,472 / $3,627 | $11,374 |
| Coffee + croissants | no other sellers | $1,520 / $2,004 / $2,376 | $3,113 / $3,597 / $3,969 | $11,374 |

- The model's staffing is its structural estimate: a cashier, a cleaner and a guard every open hour, $1,593 a day for 16 hours [BC model]. A beginner usually stands at the register, so the right-hand column is the fair match for a cart, which is also the player's own day.
- Cost to open: register $900, two counter tops at $400, the product's machine (hot dog grill $2,200, drinks fridge $875, industrial grill $1,400, industrial coffee machine $4,700, bakery showcase $895; the $400 cheap coffee machine is office equipment, ItemType 0x8000 in Big Copilot's data, not shop equipment), one box of each product at wholesale (a shop's shelves take whole boxes: 500 hot dogs $2,225), the deposit and the first day's rent [BC data, opener research].
- The full fast food outfit (whole range, 24/7, best marketing) has a median of $21,900 a day on a $53,700 investment. That's where shops go later; the cart is never compared with it.

**The yardstick S:** about **$2,600 a day** (the median of the six owner-run medians, which run from $1,720 to $3,600), **$7,600 to $11,400** to open (burgers to coffee).

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

Best lunch + evening route (11:00 to 14:30, 17:00 to 23:00), hot dog grill + cooler, the final proposal [calc]:

| Price | Midtown hot dog | Midtown office lunch: share who buy, profit/h | Best route | ÷ ceiling |
|---|---|---|---|---|
| (a) default | $11.65 | 100%, $157 | $885 | 52% |
| (c) manager, capture 0.5 | $15.62 | 100%, $241 | $1,279 | 75% |
| (c) manager, capture 0.75 | $17.61 | 100%, $283 | $1,491 | 87% |
| ceiling × 0.9 | $17.64 | 100%, $284 | $1,494 | 87% |
| **(b) ceiling** | **$19.60** | **100%, $325** | **$1,713** | 100% |
| ceiling × 1.1 | $21.55 | 7%, $29 | $498 | 29% |
| upper class's maximum, reference × max(1.7, index) | $19.81 | 73%, $297 | $1,419 | 83% |

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

Nightlife is rare, and Hell's Kitchen, the design's nightlife district, has no nightlife spot at all. Why:

- **Which venues exist.** The city's nightlife buildings are four theatres on Broadway (all in Midtown) and one in the Hamptons, five cinemas (two in Midtown, one each in Lower Manhattan, Hell's Kitchen and the Hamptons) and the casino boat (Murray Hill). Nightclubs are businesses, and in the save they were all rivals' (eight, one of them in Hell's Kitchen at 2 Fourth Street), so a new game may have none [BC data, save].
- **Cinemas vote retail.** `data/demand.json` maps cinemas (building and business) to retail, so Hell's Kitchen's only fixed venue, the cinema at 15 Third Avenue, counts as a shop.
- **Venues are outvoted.** A nightclub votes 1.5 and a theatre 1.0, against every other building within 50 m. The club at 2 Fourth Street has 11 neighbours (4 residential, 5 retail, 2 office), worth about 7 votes in all, and the Garment District club at 15 Fourth Avenue has 30.
- **maxAreas 2 drops the venue.** The classifier keeps only the two biggest areas, so the venue's nightlife share isn't diluted, it disappears: the club's own spot reads retail and residential.

The 50 m radius and the 0.5 edge weight are not the problem: the venue is at distance 0 from its own pitch. Neither is a longer reach needed; stronger venue votes are enough (section 4).

### Today: staying against moving

The best single spot over the cart's 16-hour day against the best route that splits the same hours into two or three pitches (hot dogs change at 14:30, at 17:00, or at 14:30 and 21:00; coffee at 11:00, at 14:30, or at both). Staying put is one of the routes the search may pick. Today's data, ceiling price, weekday, dry [calc]. Hot dog grill + cooler, then coffee machine + display case (croissants):

| | Hot dog: stay / route / ratio | Coffee: stay / route / ratio |
|---|---|---|
| Midtown | $1,693 / $2,038 / 83% | $889 / $928 / 96% |
| Lower Manhattan | $832 / $914 / 91% | $451 / $485 / 93% |
| Murray Hill | $828 / $1,363 / 61% | $403 / $430 / 94% |
| Hell's Kitchen | $1,328 / $1,531 / 87% | $364 / $364 / 100% |
| Garment District | $1,227 / $1,288 / 95% | $344 / $366 / 94% |
| Industry City | $928 / $1,082 / 86% | $322 / $322 / 100% |
| Hamptons | $776 / $969 / 80% | $246 / $261 / 94% |
| **City** | **$1,693 / $2,038 / 83%** | **$889 / $928 / 96%** |

Default price: hot dog $1,018, coffee $471. Weekend, ceiling: hot dog $2,087 (staying 81%), coffee $682.

Why staying works today: a spot half office and half residential fills the window at lunch with half the office crowd (office lunch demand is twice the window's rate), then gets the residential evening at 1.0. Moving costs two hours of pushing and gains little. Coffee today is a morning business at offices with nowhere better to go in the afternoon, and at 3 minutes a cup it can't serve the morning crowd.

### Cost to start

The cart ($1,200 [seen, dealer dialog]), its equipment [BC data] and the first 80 units at wholesale [calc]. The coffee machine is the industrial one at $4,700: the $400 cheap coffee machine is office equipment (ItemType 0x8000 in Big Copilot's data), and Peter notes its price may have changed in a patch.

| Menu | Equipment | First 80 units | Total | In delivery days |
|---|---|---|---|---|
| Hot dog grill | $2,200 | $356 | **$3,756** | 3.4 |
| Hot dog grill + cooler | $3,075 | $277 | **$4,552** | 4.1 |
| Hot dog grill + coffee machine | $6,900 | $352 | **$8,452** | 7.5 |
| Coffee machine + display case | $5,595 | $340 | **$7,135** | 6.4 |

The coffee cart's first load is the proposal's (a coffee counts a quarter of a unit, so 80 units hold more cups); today it is $110. Optional fees, not counted in the band: a vehicle permit of $500 and a vendor licence of $250 per 60 days (about $4 a day) would add $750 to the first day.

## 4. Verdict and tuning

### The band

Tuned on the ceiling price, weekday, dry.

| Rung | Profit a day | Cost to start | Moving |
|---|---|---|---|
| Delivery day (D) | $1,120 | $0 | |
| **Hot dog cart** | **1.5 to 1.8 D ($1,700 to $2,000), at most 75% of S**; about 1 D at the default price | 3 to 4.5 D | staying at most 70% of the best route |
| **Coffee cart** | **1.2 to 1.4 times the hot dog cart's best route**, below S | more than the hot dog cart | staying at most 70% of the best route |
| Small starter shop run by the player (S) | $2,600 | $7,600 to $11,400 | |
| Truck with the player and one hire (Milestones 4 and 5) | above the coffee cart | | |

The coffee cart sits close to a shop on purpose. It costs as much as a burger shop to start, it needs a morning and a park to pay, and it gives up the weekend nights to hot dogs.

### Where the carts sit now

- **Hot dog cart.** At the ceiling price its best route is $2,038 (1.8 D, 78% of S), just over the band, and staying earns 83% of it. Hell's Kitchen, Garment District, Lower Manhattan and Industry City have no evening pitch.
- **Coffee cart.** It earns $928 at best (0.8 D) for $6,905, and staying earns 96% of that. It is the worst choice on the ladder.

### Proposed changes

**Rules (game side or Core; not data):**

1. **The cart prices like a shop.** Default price unless the player sets one; a price per product; a pricing manager covering the district applies. `PricingManagerHelper.IsManageableBusiness` only takes rented buildings, so the mod applies a district plan's suggestion to its carts itself (`PricingManagerPlan.cachedSuggestions`, by item and district). The price setting should show the ceiling, because 10% over it loses almost every buyer.
2. **80 units per cart (160 per truck), bought per unit at the shelves,** as built. Refill trips are part of the day.
3. **Stock weight per item** (Core: `StockLimit`/`CargoRules`, plus a field per product in `economy.json`). A coffee takes 0.25 of a unit, a croissant, donut or cupcake 0.5, everything else 1. A cup from the machine is beans and water, a pastry is small; without this, 80 cups last two hours of a morning rush and the refill walk eats the rest.
4. **Per-area appetite factors** (Core: `DemandModel`, plus an optional `appetite` table per area in `demand.json`). Parks: coffee ×2, ice cream ×2. The design's "Not chosen: per-area demand curves" stays true; this scales a category per area, it doesn't add curves. The tool emulates it for pure park spots only.
5. **Subway entrances vote** (game side: `World` already finds subway entrances for `Legality`; report each within the radius as a nearby place of building type `subwayentrance`). They feed the new `transit` area.
6. **Parks, waterfront and tourist stretches** stay hand-drawn zones. Nightlife no longer needs zones.
7. **Fees stay optional.** A $500 permit and a $250 licence per 60 days would leave the band unchanged.

**Data, as a patch** (`tools/balance/proposed.json`).

`data/economy.json`:

| Key | Today | Proposed | Why |
|---|---|---|---|
| `equipment.coffeemachine.gameItems` | cheap and industrial coffee machine | **industrial only** | The cheap one is office equipment (ItemType 0x8000) |
| `products.ba:itemname_cupofcoffee.serviceMinutes`, `cupoftea` | 3 | **1.0** | Press a button, lid, hand over. At 1.5 minutes the morning window binds and the coffee cart loses about a fifth of its best day |
| `products.ba:itemname_croissant`, `donut`, `cupcake` `.serviceMinutes` | 3 | **2.0** | Bought in, no prep: take from the case, bag, hand over. Slower than a cup, faster than a hot dog that is assembled (3) |
| `vehicles` | cart 80, truck 160 | unchanged | |

`data/demand.json`, footfall by daypart (early 05-08, morning 08-11, lunch 11-14:30, afternoon 14:30-17, evening 17-21, late 21-02; `night` unchanged):

| Key | Today | Proposed |
|---|---|---|
| `passersPerHour` | 30 | **22** |
| `areas.office.footfall` | 0.8 / 1.2 / 3.0 / 0.5 / 0.6 / 0.1 | **1.5 / 2.0** / 3.0 / **0.2 / 0.2 / 0.05** |
| `areas.residential.footfall` | 0.6 / 0.7 / 0.5 / 0.5 / 1.0 / 0.4 | 0.6 / 0.7 / **0.3 / 0.3 / 0.5 / 0.2** |
| `areas.retail.footfall` | 0.1 / 0.5 / 1.2 / 1.3 / 1.0 / 0.2 | **0.05 / 0.3 / 0.8** / 1.3 / **0.5 / 0.1** |
| `areas.industrial.footfall` | 1.5 / 0.4 / 1.5 / 0.3 / 0.5 / 1.0 | 1.5 / **0.2** / 1.5 / **0.15 / 0.3** / 1.0 |
| `areas.nightlife.footfall` | 0 / 0.1 / 0.4 / 0.3 / 1.5 / 2.0 | 0 / **0.05 / 0.15 / 0.1 / 2.0 / 2.5** |
| `areas.waterfront.footfall` (parks) | 0.1 / 0.4 / 0.8 / 1.2 / 0.8 / 0.1 | 0.1 / 0.4 / 0.8 / **2.0** / 0.8 / 0.1 |
| `areas.waterfront.days` mon-thu / fri / sat-sun | 0.7 / 0.9 / 1.6 | **0.9 / 1.0** / 1.6 |
| `areas.transit` (new) footfall | | **2.0 / 2.5 / 0.3 / 0.4 / 0.8 / 0.2**, night 0.05 |
| `areas.transit.days` mon-fri / sat / sun | | **1.0 / 0.5 / 0.4** |

`data/demand.json`, appetite (the same six dayparts):

| Key | Today | Proposed |
|---|---|---|
| `categories.coffee.appetite` | 0.40 / 0.30 / 0.08 / 0.15 / 0.05 / 0.02 | **0.55 / 0.45 / 0.12 / 0.35** / 0.05 / 0.02 |
| `categories.bakery.appetite` afternoon | 0.12 | **0.25** |
| `categories.fastfood.appetite` lunch / late | 0.30 / 0.35 | **0.25 / 0.25** |

`data/demand.json`, classifier (radius 50 m, edge weight 0.5 and `maxAreas` 2 unchanged):

| Key | Today | Proposed |
|---|---|---|
| `buildingTypes.ba:buildingtype_theater` | nightlife 1.0 | **nightlife 6.0** |
| `buildingTypes.ba:buildingtype_cinema` | retail 1.0 | **nightlife 3.0, retail 1.0** |
| `buildingTypes.subwayentrance` (new) | | **transit 1.0** |
| `businessTypes.ba:businesstype_nightclub` | nightlife 1.5 | **nightlife 10.0** |
| `businessTypes.ba:businesstype_theater` / `casino` | nightlife 1.0 | **nightlife 6.0** |
| `businessTypes.ba:businesstype_cinema` | retail 1.0 | **nightlife 3.0, retail 1.0** |
| `businessTypes.ba:businesstype_liquorstore` | nightlife 0.5, retail 0.5 | **nightlife 1.0**, retail 0.5 |

A venue now outvotes a block of ordinary buildings, so pitches next to it read as nightlife; their daytime footfall is low, which is what sends the cart elsewhere at lunch. A per-type reach (a longer radius for venues, a Core change) wasn't needed. `maxAreas` 3 dilutes the venue instead of keeping it, and lost nightlife spots in the test.

The shape stays: offices peak at lunch (3.0) with a morning commuter peak (2.0); nightlife late; industrial at shift changes; retail in the afternoon; residential mornings and evenings; parks in the afternoon and at weekends; subway entrances in the morning rush. The design's footfall table says 1.0 is an average open hour city-wide; after the patch the average is lower, and the table should take these numbers if the patch is accepted.

### Before and after

Ceiling price, weekday, dry unless noted [calc]:

| | Today | Proposed |
|---|---|---|
| Hot dog best route | $2,038 | **$1,946**: an office lunch at 7 Eighth Street (Midtown), the theatre corner at 6 Fifth Street from 16:50, then 17 Third Avenue by the Hell's Kitchen cinema from 21:40; one refill |
| Hot dog, staying | $1,693 (83%) | **$1,238 (64%)** |
| Hot dog, weekend: route / staying | $2,087 / 81% | $1,753 / 86% |
| Hot dog, default price | $1,018 | $1,002 (staying 59%) |
| Coffee best route | $928 | **$2,353**: the Midtown subway entrance from 06:00, a Midtown park from 12:05; one refill |
| Coffee, staying | $889 (96%) | **$1,704 (72%)** |
| Coffee, weekend | $682 | $1,601 (staying 82%) |
| Coffee, default price | $471 | $1,210 |
| Coffee without the subway and park archetypes | | $1,436, staying 100% |
| Hot dog without them | | $1,946 weekday, $1,593 weekend |
| Truck stand-in, best route | $2,971 | $3,239, about $3,000 after a hire |

Hourly rates at pure spots after the patch, coffee cart, ceiling, weekday (customers per hour / profit per hour): Midtown subway entrance early 46 / $293, morning 55 / $320; Midtown office early 35 / $219, morning 44 / $278; Midtown park afternoon 42 / $263 (weekend 53 / $323); Hell's Kitchen park afternoon 33 / $165 (weekend 58 / $264). Hot dog cart: Midtown office lunch 27 / $325; Hell's Kitchen nightlife late (weekend) 33 / $252; Midtown nightlife evening (weekend) 13 / $194 [calc].

Per district, see the table at the top. Moving pays clearly in Midtown, Hell's Kitchen and Garment District for hot dogs, and in five districts for coffee.

### What the patch doesn't fix

- **Coffee's lead rests on pitches the data can't place yet.** Parks need the hand-drawn zones; subway entrances need the game side to report them. The archetype park and subway sit at each district's centre. Without them coffee earns 0.74 times the hot dog cart.
- **Murray Hill and Industry City.** For hot dogs, Murray Hill's best route lost ground (staying 85%, against 61% today): its office lunches are weaker after the patch and its evening pitch is the casino boat. Industry City's best plan is an industrial spot all day: its shift peaks (early, lunch, late) are the area's own design. I'd leave both.
- **Weekends.** Staying earns 82 to 86% of the best route at weekends: offices are quiet and the nightlife pitch is good all evening. I think that's right for a Saturday.
- **Rival nightclubs.** Garment District's and Industry City's evening pitches in this city come from rivals' nightclubs. A new game may not have them until rivals open clubs.

### Room for the truck, and Milestones 3 to 7

The truck stand-in (two at the window, 160 units, grill + coffee machine + display case + cooler) makes $3,239 on the best weekday route; less a hire at a job-board wage (cashier base $16 × (20^1.05/100 + 1) = $19.70 an hour [code, `EmployeeHelper`; BC data], 12 hours = $236) and a little fuel ($1.30 a litre [code, `GasStationOverlay`]), about **$3,000**: 1.3 times the coffee cart and 1.15 times S. It starts at $10,400 before the truck itself is priced.

- **Milestone 3, queues.** Customers above the window's rate wait up to 20 minutes. At a full pitch this moves sales later more than it adds them: a few percent.
- **Milestone 4, truck and licence.** `TaxPatch` adds cart sales to taxable income at the game's rate (10% by default). The hot dog cart's best day nets $1,751, still 1.6 D against untaxed delivery pay.
- **Milestone 5, hires.** A hired hot dog cart on the best route earns about $1,946 − 12 × $19.70 = $1,710, a hired coffee cart about $2,120. The permit cap (default 4) is the brake; consider a lower cap for carts than for trucks.
- **Milestone 6, regulars, premium spots, live venue crowds.** They add demand where the window isn't full: evenings, weekends and weaker districts. Regulars reward a fixed route, which helps moving. The coffee cart's best day could reach about $2,600 [assumption], level with S; check it at the Milestone 6 balance session.
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

## 6. Fourth pass: additive crowds

In game on 8 October, a spot with two offices and a nightclub read as 88% nightlife and 3 passers an hour at lunch: areas were shares of one crowd, so the club pushed the offices out. Peter decided that areas must not be exclusive. The working tree now has additive crowds (Core and `data/demand.json`, by Sol):

- **Presence per area.** Each area's presence is 1 − exp(−votes / `classifier.saturation`), with no cut to two areas.
- **Footfall.** passersPerHour × the existing factors × (`streetBaseline` + Σ presence × area curve × day factor).
- **Appetite** is weighted by each crowd's share of the hour's footfall.

**Peter's decision, also 8 October:** finding a good spot should be rewarded, so staying there is fine. The "staying at most 70% of the best route" target is dropped; staying ÷ route is reported below for information only. Presence stays lively: Sol's saturation 1.25, a street baseline that keeps any pavement alive at midday, and venues that add an evening crowd without drowning the offices. Only levels were retuned to hold the band.

The calculator builds against the new Core unchanged. Its classifier table now reports presence instead of share, and an `ingame` report covers the in-game case and typical spots.

### Values in `data/demand.json`

Sol's lively structure is kept: `classifier.saturation` 1.25, `streetBaseline` early / morning / lunch / afternoon / evening / late / night 0.3 / 0.5 / 1.0 / 0.5 / 0.3 / 0.08 / 0.03, and the third pass's area curves. Changed:

| Key | Before | Now | Why |
|---|---|---|---|
| `passersPerHour` | 22 | **16** | Sets the level: the best hot dog route lands at 1.76 delivery days |
| nightclub (business), theatre (building and business), casino | nightlife 10 / 6 / 6 | **4.0** | A club or theatre still reads as a full evening crowd at saturation 1.25 (presence 0.9 at 30 m) without needing more |
| cinema (building and business) | nightlife 3.0, retail 1.0 | **nightlife 2.0**, retail 1.0 | Same, for the smaller venue |
| `categories.fastfood.appetite` evening / late | 0.20 / 0.25 | **0.15 / 0.20** | With every evening spot alive, fast food's evening share sets the hot dog cart's top |
| `categories.coffee.appetite` early / morning / afternoon | 0.55 / 0.45 / 0.35 | **0.60 / 0.50 / 0.40** | Keeps coffee 1.2 times the hot dog cart at the lower footfall |

Service times, stock weights and park appetite are as in `data/economy.json` and the third pass.

### Results

Weekday, dry, ceiling price, the 16-hour days and route splits of section 4 [calc]:

| Rung | Cost to start | Best route, ceiling | Default price | Weekend, ceiling | Staying ÷ route (information) |
|---|---|---|---|---|---|
| Delivery day (D) | $0 | $1,120 | | | |
| Hot dog cart (grill + cooler; grill only $3,756) | $4,552 | **$1,975** (1.76 D, 76% of S) | $1,014 | $2,201 | 100% |
| Coffee cart (industrial machine + display case) | $7,135 | **$2,393** (1.21× hot dog) | $1,232 | $1,601 | 92% |
| Small starter shop run by the player (S) | $7,600 to $11,400 | $2,600 | | | |
| Truck stand-in (two at the window, 160 units, four items) | $10,400 before a truck price | **$4,123**, about $3,900 after a hire | $2,108 | $3,113 | 75% |

The yardsticks are unchanged: delivery and the shop don't use the mod's model.

- **The truck sits far above.** It reaches 1.7 times the coffee cart and 1.5 times S, so its own price, the hire's wage and fuel have plenty of room.
- **The best day is one good spot.** In Midtown it is 4 Third Avenue all day, with one refill: nightlife, retail and office presence all above 0.9, residential 0.74.

Staying ÷ best route per district, weekday, hot dog / coffee, for information: Midtown 100% / 92%; Hell's Kitchen 100% / 100%; Garment District 100% / 100%; Lower Manhattan 99% / 92%; Murray Hill 90% / 99%; Hamptons 96% / 73%; Industry City 62% / 82%.

### The in-game case

Two offices at 20 and 40 m and a nightclub at 30 m. Presence: office 0.67, nightlife 0.89. Hot dog grill + cooler, weekday, ceiling price; passers per hour / customers per hour [calc]:

| District, hour | Share model (0.3.0) | Additive, now | Pure office block (eight offices, presence 0.99) |
|---|---|---|---|
| Midtown lunch 12:00 | 17 / 5.2 | **65 / 20.0** (the window is full) | 83 / 25.5 |
| Midtown evening 19:00 | 34 / 7.3 | **35 / 5.7** | 10 / 1.7 |
| Midtown late 22:00 | 25 / 6.5 | 21 / 4.4 | |
| Murray Hill lunch 12:00 | 8 / 2.2 | 31 / 8.4 | |
| Murray Hill evening 19:00 | 24 / 5.0 | 24 / 3.9 | |

At lunch the spot is busy: over the 60 passers aimed for in Midtown, and the cart sells as fast as one person can serve. In the evening it is three and a half times as busy as a pure office block.

### A typical spot

Hot dog grill + cooler at every real pitch, weekday, ceiling price; customers served per hour, p25 / median / p90 [calc]:

| District | Lunch 12:00 | Evening 19:00 | Lunch passers/h, median |
|---|---|---|---|
| Midtown | 21 / 21 / 21 | 3.6 / 4.0 / 8.5 | 90 |
| Lower Manhattan | 17 / 21 / 21 | 1.4 / 1.6 / 2.7 | 80 |
| Garment District | 18 / 21 / 21 | 3.0 / 3.2 / 6.2 | 61 |
| Hell's Kitchen | 9.9 / 14.5 / 18.6 | 4.3 / 4.7 / 9.4 | 54 |
| Murray Hill | 10.3 / 12.4 / 14.5 | 3.1 / 3.3 / 5.1 | 46 |
| Industry City | 7.3 / 7.4 / 11.7 | 1.8 / 2.4 / 3.2 | 25 |
| Hamptons | 2.9 / 3.4 / 4.4 | 0.8 / 1.0 / 3.0 | 13 |
| **City** | **8.6 / 14.6 / 21** | **2.1 / 3.1 / 6.2** | 51 |

At lunch a quarter of the city's pitches sell under 9 hot dogs an hour, the median 15, and the window is full in Midtown, Lower Manhattan and Garment District almost everywhere. In the evening the median is 3 an hour, away from the venues. Nowhere is near zero; the quietest are the Hamptons at 3 an hour at lunch.

### What this means

- **Finding the spot is the game.** Where the city's crowds overlap, a cart stays put. Midtown's best spot is good from lunch to late, and the route search finds nothing better. A route still pays where the crowds are apart: Industry City's industrial lunch and its club in the evening (staying 62%).
- **Lunch is generous everywhere downtown.** The window binds at most Midtown, Lower Manhattan and Garment District pitches. Picking a spot matters more for the evening, where venues make the difference: a club or theatre corner sells 2 to 3 times the median.
- **A tiring-pitch rule isn't needed** under this decision.

**Tests.** Sol's data-pinned Core tests were written for his first values. This pass changes `passersPerHour`, the venue weights and the fast food and coffee appetites, so the tests that pinned them were rewritten to assert behaviour instead: the in-game case is busy at a Midtown weekday lunch (at least 60 passers an hour) and busier than an office block in the evening.

## 7. The queue (Milestone 3, 8 October 2026)

From 0.4.0 the game sells through Core's `SalesSimulator` instead of the fluid rate. Customers who buy arrive at random (Poisson) at the same hourly rate, wait in a queue of at most 6 at a cart (10 at a truck, `data/rules.json`), give up after about 20 minutes (12 in rain), and the window serves them at the item's service time. The player's customer service skill sets the window's speed; at 50, the starting value, it is the base speed the study uses.

The calculator mirrors it with `--service queue`, now the default (`--service fluid` is the old rule). Each 5-minute step's sales are scaled by the share of customers the queue really serves at that load, measured with `SalesSimulator` itself for each order time, staff count and weather. Far below the window's capacity, or far above it, the queue serves what the fluid rule did. Close to capacity, random bunching costs up to about 8%: some customers find the queue full while the window was idle a minute earlier.

| Cart, weekday dry, ceiling price | Fluid | Queue | Change |
|---|---|---|---|
| Hot dog grill + cooler, best route | $1,975 | $1,975 | 0 |
| Hot dog, best route, weekend | $2,201 | $2,160 | −1.9% |
| Coffee machine + display case, best route | $2,393 | $2,326 | −2.8% |
| Truck stand-in, best route | $4,123 | $4,114 | −0.2% |

The ladder holds: coffee is 1.18× the hot dog cart instead of 1.21×, and the truck is about twice the cart. Coffee loses most; its best pitches run close to the window's capacity, where bunching costs the most. Nothing was retuned.

## Assumptions

1. Default difficulty is `GameVariables`' code defaults. The Normal preset may differ.
2. Delivery: $300 a job, $20 tips, 3 to 4 jobs a day, from Peter's play on harder settings.
3. Map scale 1.0 / 1.49 / 3.2 m per map unit; streets add 30%; doors are on the outline boxes.
4. The player pushes the cart at the default jog, 4 m/s; a refill takes 10 minutes in the store.
5. Hot dogs cost $4.45 at every wholesaler (index 1.0, store 100); the real price is 10% either side.
6. Market demand 100 (no shop sells the cart's items in the neighbourhood), and no cheaper shop sets the reference price. A shop selling below the default price lowers the reference, and with it the ceiling.
7. May: footfall 1.0, no season factors.
8. The day is 16 hours for staying and routes alike: hot dogs 09:00 to 01:00, coffee 06:00 to 22:00, the truck stand-in 07:00 to 23:00. Routes change pitch at fixed times (hot dogs 14:30, 17:00, or 14:30 and 21:00; coffee 11:00, 14:30, or both); the best route is the best the search found (top 20 per pitch plus the five best all-day spots), not a proven optimum.
9. Store yardstick: Big Copilot's model on one late-game city, A units, 06:00 to 22:00, satisfaction 95, no marketing.
10. Business types: fixed AI businesses plus one save's rivals. A new game has different rivals, so its nightlife spots differ.
11. Pricing manager capture 0.5 and 0.75 stand in for a typical analyst; `minSkillCapture` is asset data.
12. Parks and subway entrances are archetype spots, one of each per district at the middle of its buildings. Real parks come from zones and real subway entrances from the game; their number and places will move the coffee figures most.
13. The stock weights and the park appetite factors are emulated by the tool (`rules` in `proposed.json`); Core doesn't have them yet.
14. All six wholesalers sell hot dogs, coffee, croissants and soda (Big Copilot's wiki data).

## Open questions for Peter

1. **The truck's room.** With lively crowds the truck stand-in makes about $3,900 a day after its hire, 1.5 times the small shop. Should its price, or its window, take some of that back?

1. **Subway entrances and parks first.** The coffee cart's lead needs both. Should the game side report subway entrances to the classifier, and the park zones be drawn, before your balance check?
2. **Stock weight and per-area appetite.** Both need Core changes (a weight per product in `economy.json`, an optional appetite table per area in `demand.json`). Agreed, or should coffee get its volume another way (a bigger unit limit for drinks)?
3. **Weekends.** Staying earns 82 to 86% of the best route on a Saturday. Fine, or should weekends separate as much as weekdays?
4. **Pricing manager on carts.** The game's manager only reprices rented buildings. Should the mod apply a district plan to carts automatically, or only when the player assigns the cart to it?
5. **Delivery check.** How many in-game hours does one delivery job take, and does it pay $300 plus about $20 in tips on default settings? The band's floor rests on it.
6. **Fees.** If you add a permit and a licence, are $500 and $250 per 60 days about right? And what does the Food Safety course cost?
7. **Hired carts.** Should the permit cap differ for carts and trucks, so a fleet of hired carts doesn't pass the shop before the truck exists?
