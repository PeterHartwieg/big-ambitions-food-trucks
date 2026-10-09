# New foods: a calculated balance proposal

Written 9 October 2026 for the plan's "New foods" section. Every number here is a calculated proposal, unverified in game. The method is the accepted cart study's ([cart-economics.md](cart-economics.md), sections 6 and 7): additive crowds, the game's queue, the ceiling price (reference × the neighbourhood's index), a 16-hour day from 09:00 (07:00 for the truck stand-in), refills by pushing to the nearest wholesaler, market demand 100 unless stated. The data is `tools/balance/new-foods-proposal.json`; run it with `--patch new-foods-proposal.json --reports foods` (about 20 minutes). Labels as in the study: [code] read in the 1.0 decompile, [BC data] Big Copilot's extracted game data, [calc] the calculator, [assumption] my judgement.

## The answer

- **Four of the eight foods are already in the game.** Kabob, popcorn, cotton candy and slushi are base-game items with fixed prices and boxes [BC data]. They stay vanilla items, bought at wholesalers like hot dogs, on vanilla machines. Only dumplings, tacos, chopped cheese and grilled corn are new mod items, sold through the commissary.
- **Three new demand categories, no Core change.** `streetmeal` (kabobs, dumplings), `snacks` (popcorn, cotton candy, grilled corn) and `slushies`. Tacos and chopped cheese join `fastfood`. Categories, area and season factors are all data the Core already reads.
- **Meal carts land beside the hot dog cart.** Weekday profit at the ceiling: dumplings $1,865, kabobs $1,864, burgers + kabobs on one grill $2,140, the griddle $2,210, against the hot dog cart's $1,975 (grill + cooler) to $2,204 (grill alone).
- **Snack carts are a summer business.** Popcorn, cotton candy, corn and slushies earn $730 to $1,250 on a spring weekday, $2,100 to $2,900 on a July Saturday and about $300 in January. Over a July week the concessions case + slushie machine makes $1,982 a day, level with the hot dog cart's $2,129; over a May week $1,381.
- **One combination reaches the coffee rung:** hot dog grill + industrial grill for kabobs, $2,396 a weekday for $5,173 to start, against the coffee cart's $2,330 for $7,134. That is the normal value of a second meal category, and I'd accept it (see "Open questions").

## What the game already has

| Item | Price / wholesale / box [BC data] | Sold in game from | Demand limited to |
|---|---|---|---|
| `ba:itemname_kabob` | $14.00 / $5.20 / 500 | industrial grill (fast food restaurants) | everywhere |
| `ba:itemname_popcorn` | $8.50 / $0.20 / 500 | concessions display case (cinemas, theatres) | Hell's Kitchen, Midtown, Lower Manhattan, the Hamptons |
| `ba:itemname_cottoncandy` | $6.75 / $0.10 / 500 | concessions display case (cinemas) | same four |
| `ba:itemname_slushi` | $7.00 / $0.90 / 500 | slushi machine (cinemas) | same four |

The concessions display case costs $895 and the slushi machine $2,200, both at Red Carpet Decor, 6 Broadway Street; the industrial grill $1,400 and the fryer $4,400 at Square Appliances [BC data].

### The neighbourhood limit and the market factor

The game builds a market entry only for the neighbourhoods an item's demand is limited to (`ProductMarketHelper`, `CanNeighborhoodHaveItemDemand`) [code]. Two effects on the cinema items:

- **Inside the four neighbourhoods, cinemas and theatres are providers.** Demand = 100 − providers × 100 / 7 (all four items cap their optimal providers at 7), floored at about 31 [code, `CalculateDemand`]. With the city's cinemas (Midtown 2, the other three 1) and theatres (Midtown 4, Hamptons 1), if they count as providers: popcorn 31 in Midtown, 72 in the Hamptons, 86 in Hell's Kitchen and Lower Manhattan; cotton candy and slushi 72 in Midtown, 86 elsewhere. The proposal's `game.market` feeds these to the calculator. Popcorn in Midtown sells at the market floor, 0.31 of its appetite; that is why the snack carts' best days are in Hell's Kitchen.
- **Outside them there is no entry.** `GetNeighborhoodDemand` then logs an error and returns 50 [code], and `World.cs` calls it on every plan. A popcorn cart in Garment District, Murray Hill or Industry City would sell at half its appetite and fill the log. The fix is game side: in `World.cs`, skip items for which `ProductMarketHelper.CanNeighborhoodHaveItemDemand` is false, so Core treats them as unsaturated (100). The proposal assumes this fix. Without it, the city's best snack days barely move (concessions case + slushie machine on the first pass's data: weekday May $1,096 either way, Saturday May $1,662 against $1,740), because the best pitches are in Hell's Kitchen and Midtown; a player who stays in the three other districts loses half the snack trade.

Kabob has no limit, and fast food restaurants sell it, so it behaves like hot dogs: the study's assumption of market 100 (no shop in the neighbourhood) applies to both.

## Equipment

| Piece (mod key) | Game item | Price | Unlocks | Display | Why |
|---|---|---|---|---|---|
| `grill` (replaces `burgerstation`) | industrial grill | $1,400 | burger, kabob | BurgerStation | The game cooks burgers and kabobs on this grill |
| `fryer` (replaces `burgerstation`) | industrial fryer | $4,400 | fries | BurgerStation until a fryer model exists | The game fries on its own machine |
| `concessions` | concessions display case | $895 | popcorn, cotton candy | PopcornMachine | One game item holds both, so one piece |
| `slushiemachine` | slushi machine | $2,200 | slushi | SlushieMachine | |
| `dumplingsteamer` | new mod item | $2,000 | dumplings | DumplingSteamer | Priced near the ice cream counter and hot dog grill |
| `griddle` | new mod item | $2,000 | tacos, chopped cheese | Griddle | Two foods in one category, so priced like a one-food piece |
| `corngrill` | new mod item | $1,200 | grilled corn | CornGrill | The cheapest: a seasonal snack |

**Splitting the burger station.** Today either the grill or the fryer unlocks burgers and fries. Adding kabobs there would let a fryer sell kabobs. Split it the way the game does: grill for burgers and kabobs, fryer for fries. Fitted pieces come from the game items in cargo (`CargoRules.FittedEquipment`), so saves need no migration; a vehicle that carries only one of the two machines loses the other's food. The `CottonCandyMachine` and `KabobGrill` models have no piece left to show; the grill could show `KabobGrill` instead of `BurgerStation` if Peter prefers.

## Demand categories

| Category | Items | Appetite early / morning / lunch / afternoon / evening / late / night | Area factors | Season, rain |
|---|---|---|---|---|
| `fastfood` (existing) | hot dog, burger, fries, **tacos, chopped cheese** | unchanged | none | unchanged |
| `streetmeal` (new) | **kabob, dumplings** | 0.02 / 0.03 / 0.18 / 0.05 / 0.11 / 0.11 / 0.12 | office ×1.2 | none |
| `snacks` (new) | **popcorn, cotton candy, grilled corn** | 0 / 0.02 / 0.06 / 0.17 / 0.15 / 0.06 / 0.01 | parks ×2, nightlife ×1.5, retail ×1.3, office ×0.5 | summer 1.3, autumn 1.1, winter 0.6; rain 0.5 |
| `slushies` (new) | **slushi** | 0 / 0.02 / 0.10 / 0.18 / 0.10 / 0.04 / 0.01 | parks ×2 | summer 1.6, winter 0.3; rain 0.4 |

Neighbourhood rules: `streetmeal` joins Midtown's lunch rule (with `snacks` and `slushies`, since that rule lists every category), Lower Manhattan's lunch rule (FiDi halal carts) and Hell's Kitchen's late rule.

Why these:

- **Tacos and chopped cheese are fast food.** Both are cheap, quick and strongest at lunch and late, which is fast food's curve, with its late peak where the nightlife crowd is. Keeping both griddle foods in one category keeps the griddle a one-category piece. A separate taco category would make a griddle beside a hot dog grill a second category for $2,000, the question the kabob raises below. The game's own grill does bring two categories (burgers and kabobs), but burgers are slow (5 minutes), so that piece earns $2,140, level with the hot dog grill.
- **Kabobs and dumplings are a meal, not fast food.** The research note's halal carts and Yumpling are lunch businesses with queues at office blocks, and halal carts run late. In `fastfood`, the kabob ($14, 4 minutes) beats the hot dog outright: a kabob-only grill made $2,345 a weekday against the hot dog grill's $2,204 [calc]. In its own category, a little below fast food at lunch and in the evening, it earns $1,864 and adds a second category where it shares a cart.
- **Snacks are for people with time.** Afternoons, evenings, parks and the cinema and theatre crowd; offices halved. Corn's late summer and cotton candy's summer can't differ from popcorn's year-round trade inside one category: the Core's season factors are per category. A per-item season factor would need a Core change (`products.<item>.seasons`); I don't think the difference is worth it.
- **Slushies aren't cold drinks.** As a `colddrinks` add-on the slushi would ride on every hot dog order at $7 instead of the soda's $3, and the cooler would have no use. Its own category, shaped like ice cream, makes it a summer treat with its own customers.

## Per item

Service time follows the vanilla rule that price per service minute sits at $3.50 to $3.90 (hot dog $11.65 / 3 min, coffee $3.90 / 1, ice cream $11.50 / 3, croissant $7.20 / 2). New items' wholesale is 36 to 38% of the price, as for hot dogs, burgers and ice cream. A box of a new item holds 40 stock units, half a cart, so one delivery fits a parked vehicle; vanilla boxes of 500 are six cart loads at $2,000 to $2,500.

| Item | Category | Service (min) | Stock weight | Price | Wholesale | Box | Source of the money |
|---|---|---|---|---|---|---|---|
| kabob | streetmeal | 4 | 1.0 | $14.00 | $5.20 | 500 | game |
| dumplings | streetmeal | 3.5 | 0.5 | $13.50 | $5.10 | 80 | proposed |
| tacos | fastfood | 3.5 | 1.0 | $13.00 | $4.90 | 40 | proposed |
| chopped cheese | fastfood | 3.5 | 1.0 | $12.00 | $4.55 | 40 | proposed |
| grilled corn | snacks | 2 | 0.5 | $7.00 | $2.50 | 80 | proposed |
| popcorn | snacks | 2 | 0.25 | $8.50 | $0.20 | 500 | game |
| cotton candy | snacks | 2 | 0.25 | $6.75 | $0.10 | 500 | game |
| slushi | slushies | 1.5 | 0.25 | $7.00 | $0.90 | 500 | game |

Stock weights follow the study's rule: what the machine makes from a little stock counts less. Popcorn from kernels, cotton candy from sugar and slushi from syrup count like coffee (0.25); dumplings and corn are small (0.5). Popcorn and slushi wholesale are the game's; their near-zero cost is a vanilla choice, and with the low prices it doesn't make the snack carts strong.

## Profit comparison

Best day city-wide, staying or moving, ceiling price, dry [calc]. "Week" is (5 weekdays + 2 Saturdays) / 7.

| Cart | Cost to start | Weekday May | Saturday May | Week May | Weekday July | Saturday July | Week July | Weekday January | Default price, weekday May |
|---|---|---|---|---|---|---|---|---|---|
| hot dog grill + cooler (accepted) | $4,554 | 1,975 | 2,162 | 2,028 | 2,092 | 2,221 | 2,129 | 1,673 | 1,012 |
| hot dog grill alone | $3,756 | 2,204 | 2,018 | 2,151 | 2,204 | 1,997 | 2,145 | 1,613 | 1,127 |
| coffee machine + display case (accepted) | $7,134 | 2,330 | 1,577 | 2,115 | 2,330 | 1,678 | 2,144 | 1,731 | 1,199 |
| ice cream counter (vanilla) | $3,346 | 1,239 | 1,435 | 1,295 | 1,554 | 1,639 | 1,578 | 162 | 596 |
| dumpling steamer | $4,016 | 1,865 | 1,949 | 1,889 | 1,865 | 2,287 | 1,986 | 1,409 | 968 |
| dumpling steamer + cooler | $4,575 | 2,042 | 1,732 | 1,953 | 2,076 | 1,740 | 1,980 | 1,496 | 1,034 |
| grill, kabobs only | $3,016 | 1,864 | 1,757 | 1,833 | 1,864 | 1,660 | 1,806 | 1,412 | 941 |
| grill (burgers, kabobs) | $2,990 | 2,140 | 2,150 | 2,143 | 2,140 | 2,150 | 2,143 | 1,755 | 1,130 |
| grill (burgers, kabobs) + cooler | $3,787 | 2,226 | 2,230 | 2,227 | 2,276 | 2,264 | 2,273 | 1,862 | 1,120 |
| hot dog grill + grill (kabobs) | $5,173 | 2,396 | 2,486 | 2,422 | 2,396 | 2,484 | 2,421 | 1,978 | 1,217 |
| griddle (tacos, chopped cheese) | $3,578 | 2,210 | 2,144 | 2,191 | 2,210 | 2,119 | 2,184 | 1,675 | 1,140 |
| griddle + cooler | $4,371 | 2,090 | 2,278 | 2,144 | 2,065 | 2,316 | 2,137 | 1,784 | 1,090 |
| griddle + slushie machine | $5,775 | 2,366 | 2,362 | 2,365 | 2,471 | 2,542 | 2,491 | 1,717 | 1,243 |
| corn grill | $2,800 | 867 | 1,124 | 940 | 1,126 | 1,432 | 1,213 | 312 | 429 |
| concessions case (popcorn, cotton candy) | $2,159 | 788 | 1,446 | 976 | 1,025 | 2,335 | 1,399 | 284 | 550 |
| slushie machine | $3,688 | 730 | 960 | 796 | 1,169 | 2,141 | 1,447 | 131 | 427 |
| concessions case + slushie machine | $4,556 | 1,218 | 1,787 | 1,381 | 1,603 | 2,928 | 1,982 | 352 | 774 |
| corn grill + slushie machine | $4,927 | 1,246 | 1,503 | 1,319 | 1,773 | 2,193 | 1,893 | 373 | 672 |
| truck stand-in, accepted (grill, coffee, case, cooler) | $9,220 before the truck | 4,116 | 3,112 | 3,829 | 4,244 | 3,184 | 3,941 | 2,931 | 2,103 |
| truck stand-in: kabob grill, griddle, slushie, cooler | $7,093 before the truck | 3,654 | 3,403 | 3,582 | 3,805 | 3,447 | 3,703 | 2,547 | 1,916 |

Hourly rates at pure spots (served per hour / profit per hour, unlimited stock) [calc]:

| Cart | Midtown office lunch, weekday | Hell's Kitchen nightlife late, Saturday | Midtown park afternoon, weekday May | same, Saturday July | same, weekday January |
|---|---|---|---|---|---|
| hot dog grill + cooler | 21 / $320 | 19 / $230 | 5 / $66 | 6 / $78 | 3 / $40 |
| dumpling steamer | 17 / $293 | 10 / $148 | 2 / $42 | 3 / $47 | 1 / $25 |
| grill, kabobs only | 15 / $271 | 10 / $154 | 2 / $44 | 3 / $49 | 1 / $26 |
| griddle | 17 / $278 | 16 / $223 | 4 / $62 | 4 / $70 | 2 / $37 |
| corn grill | 4 / $33 | 7 / $49 | 14 / $134 | 22 / $205 | 5 / $48 |
| concessions case | 2 / $26 | 6 / $60 | 7 / $105 | 11 / $161 | 3 / $38 |
| slushie machine | 7 / $75 | 3 / $23 | 11 / $120 | 21 / $226 | 2 / $22 |
| ice cream counter (vanilla) | 8 / $115 | 2 / $27 | 17 / $249 | 20 / $296 | 2 / $31 |

What the tables say:

- **No new food dominates a single slot.** The best one-piece carts are the griddle ($2,210) and the grill with burgers and kabobs ($2,140), both level with the hot dog grill ($2,204). Dumplings and kabobs alone are 15% under it and win office lunches only where the hot dog's window is already full.
- **None is useless, but snacks are seasonal.** On a spring weekday a snack-only cart earns less than a delivery day ($1,120). That is their trade-off for the cheapest start ($2,159 for the concessions case) and the best summer weekends. In January they die, as the vanilla ice cream counter does.
- **Snacks pair well.** The concessions case + slushie machine is two categories at parks and in summer, and the best snack cart in every season. A slushie machine on a griddle cart ($2,366) is the best food-cart combination after hot dog + kabobs.
- **Popcorn beats cotton candy on the same case.** Popcorn is the dearer item and both sell at the ceiling, so the case earns most with popcorn only ($876 against $788 mixed). Cotton candy pays in Midtown, where the theatres saturate popcorn (market 31 against 72).
- **The new truck menu stays under the accepted truck stand-in** ($3,654 against $4,116), so the truck's room is unchanged.

Iterations: a first pass with streetmeal at 0.22 lunch and 0.14 evening put dumplings and kabobs at $2,106 and $2,112 and hot dog + kabobs at $2,486. Snacks with parks ×2.5 and the Hamptons summer-weekend rule made a concessions + slushie cart $3,507 on a July Saturday, above the truck. The proposal trims streetmeal by a fifth, lifts snacks' afternoon and evening, sets parks to ×2 and keeps snacks and slushies out of the Hamptons rule.

## Changes outside the data

- **Core: none.** New categories, area appetite, season and rain factors, neighbourhood rules, stock weights and the equipment split are all data. A per-item season (corn in late summer only) would need one; not proposed.
- **Game side: the market read** in `World.cs`, as above: skip items whose demand the game limits away from the neighbourhood.
- **Locale keys** `foodtrucks:category_streetmeal`, `category_snacks` and `category_slushies` (`Catering.Category` shows them).
- **New mod items**: dumplings, tacos, chopped cheese, grilled corn, and three machines (dumpling steamer, griddle, corn grill), registered as the Bakery mod does. Each food item was to have `isADemandedProduct` with no neighbourhood limit; the fix round of 9 October 2026 turned it off, since a market entry for a mod item breaks the save once the mod is removed. The foods are therefore unsaturated everywhere, which is what this study assumed.
- **Calculator** (`tools/balance`, done): a `foods` report, a patch `game` section for item and equipment prices and per-neighbourhood market demand, `null` to remove a key, and the four vanilla items in `game-prices.json`.

## Open questions for Peter

1. **Hot dog + kabobs reaches the coffee cart's day for less money** ($2,396 for $5,173 against $2,330 for $7,134). It is any two meal categories on a cart; dumplings would do the same. Accept it as a second road to the coffee rung, or lower `menu.furtherCategoryWeight` (that moves every combination, including the truck)?
2. **The burger station split.** Fine to split it into grill and fryer, with the fryer reusing the BurgerStation model for now?
3. **The `World.cs` market fix.** Without it, popcorn, cotton candy and slushi sell at half appetite in Garment District, Murray Hill and Industry City, and each plan logs a game error.
4. **Box sizes of the new items** (40 to 80 units) assume the commissary delivers into a parked cart. If deliveries can split boxes, the vanilla 500 works too.
5. **Commissary restocking.** The calculator refills the new items by a walk to the wholesaler, as for hot dogs. If the commissary's delivery is slower or quicker, dumplings, tacos, chopped cheese and corn move with it.

## Assumptions

1. Cinemas and theatres count as providers of popcorn, cotton candy and slushi in the game's market; rival cinemas would lower the market further.
2. Wholesalers stock kabob, popcorn, cotton candy and slushi on their shelves like hot dogs (the coordinator's brief; not checked in the data).
3. Prices and wholesale of the four new items, service times, stock weights and machine prices are mine, set by the vanilla price-per-minute and margin rules above.
4. Everything else as in the accepted study: May footfall, dry, archetype parks and subway entrances at each district's middle, map scale and walking speed.

Observed in passing: the hot dog grill alone ($2,204 a weekday) beats grill + cooler ($1,975) on the city's best day. Probably because the soda shares the cart's 80 units, so the hot dogs run out sooner; not checked. That belongs to the accepted study, not to the new foods.
