# Balance pass, 14 October 2026

Every number here comes from the balance calculator [calc] and none was checked in game. Since the last run (storage per piece, 10 October) these changed: truck staff 2 (was 3), storage per piece (40 a cart piece, 80 a truck piece), Extra Storage at $900, truck mass 1,600 kg (driving feel only, nothing here), commissary phone orders with a weekly limit of 10 boxes of each food per commissary plus the 10% fee, standing orders, crews restocking at their commissary, logistics plans (no fee, no limit) and the new `park` area. No tuning value in `data/` changed in this pass. The proposals at the end are for Peter to decide.

Run it with `dotnet bin/Debug/net10.0/balance.dll --reports pass,premium,pitches,storage,compare` in `tools/balance` (about 16 minutes; `pass` alone takes about 3). Method as before: additive crowds, the game's queue, the ceiling price, May, dry, market demand 100, a 16-hour day (carts 09:00 to 01:00, coffee carts 06:00 to 22:00, trucks 07:00 to 23:00), seven open days a week. The truck is the stand-in with four pieces: hot dog grill, coffee machine, display case (croissants) and cooler.

## What drifted in the calculator, and the fixes

`Cart.Plan` still matches `CartDemand.Plan` line by line: neither changed since the storage pass. The drift was in selling and around it.

1. **Who serves.** The tool sold at the vehicle's whole staff. In the game `CartDemand.Plan`'s capacity counts the vehicle's staff, but `CartSelling` sells with whoever is at the window (`HireCrew.Team`): capacity per server × the team's speed × servers. A player alone at a truck is one server, not two. The tool now takes servers and speed (`Cart(servers, speed)`), defaulting to the vehicle's staff at speed 1, so every earlier cart number is unchanged to the dollar.
2. **Trucks were pushed.** Truck restock trips ran at the cart's jogging speed (4 m a game minute). Trucks now drive at 8 m a game minute (`--drive-speed`, about 29 km/h in town; a real second is a game minute) with 15 minutes at the wholesaler. The best truck day at Midtown rises from $4,046 to $4,196 (`--reports storage`), the truck's best route from $4,130 to $4,221.
3. **Hard-coded truck staff.** The truck stand-in used a fixed staff of 2 while `economy.json` said 3. It now reads the data, which says 2 since today, so the numbers don't move.
4. **The park archetype** stood in for a park with `waterfront` at presence 1. It is now `park` at 0.70, what a spot inside one of the game's park areas gets, and a waterfront archetype sits beside it. The coffee cart's best route drops from $2,202 to $2,161 because parks now favour coffee 1.2 times, not 2.
5. **New in the tool, not drift:** crew routes as the game runs them, the commissaries' positions, crew wages as the game pays them, the rent and the delivery fee. A crew's route is a schedule built and checked by Core (`HireSchedules.Validate`, `PlanDay`, review round 2): the pitch, then a 30-minute stop in the bay that opens before midnight (the pitch closes early enough for that), so the vehicle spends the night in the bay and restocks before it leaves the next morning (Core `CrewRestock` loads only in the bay of the commissary where it rents). The other form Core allows, a morning bay stop before the pitch, leaves the vehicle at the pitch overnight and pays the $250 fine if the plan has it standing there at 04:00 (on its way or open, it doesn't, as in the game's night check); the tool tries both and keeps the better, and names the forms Core refused. Travel is the taxi timing (`rules.json` `fleet.travel`); a hired cart is pushed at walking pace. A crew never drives off to restock on its own; a midday restock needs a scheduled bay stop, and a crew that sells out stays sold out until one comes. Review round 2 also found that the game couldn't do even that: `HireRunner.Step` refused every departure with nothing on board, so a sold-out crew never reached its bay. Core now lets it leave for a stop in its own commissary's bay (plan, "Commissary supply and crew restock"). Wages follow `CrewPay`: a route pays each hire every hour its planned shift (leaving the bay to the last bay stop's close, `HireRunner.PlannedShiftMinutes`) covers at least half of, sold out or not; a window the player opened pays the hire every hour they serve in its first half (round 1 of the review: the first version charged only the minutes the window sold).

## Headlines: a day and a week

Pitches are every real pitch ranked by its weekday net (gross less wages, fee and rent): poor is the 25th percentile, typical the median, good the 90th percentile, best the top. Crewed setups rank only pitches in a region with a commissary (not the Hamptons). Premium is the median and the best of the 17 premium spots, held all day. A day is gross: sales less goods at wholesale. A week is net: seven days less the commissary space ($9.86 a day for a cart, $41.10 for a truck), crew wages and the delivery fee. A hire has customer service 60 at satisfaction 85: speed 1.0 and $27.78 an hour (the game's $16 × (60^1.05 / 100 + 1)). Weekends are Saturday and Sunday; the game's own `IsWeekend` also counts Friday, but uses it only for nightclub fees.

**Played by the player** (restocks at the nearest wholesaler when a piece runs out):

| Vehicle | Typical: day / week net | Good | Best | Premium, median | Premium, best |
|---|---|---|---|---|---|
| Cart: hot dog grill + cooler | $697 / $4,889 | $1,055 / $7,937 | $1,716 / $11,656 | $1,037 / $5,748 | $1,888 / $12,830 |
| Cart: hot dog grill + Extra Storage | $841 / $5,898 | $1,320 / $9,786 | $2,011 / $13,308 | $1,274 / $8,445 | $2,274 / $15,055 |
| Cart: coffee machine + display case | $678 / $4,862 | $1,308 / $8,139 | $2,020 / $12,216 | $1,054 / $6,862 | $2,446 / $15,477 |
| Truck, player alone | $1,529 / $11,476 | $2,689 / $16,079 | $3,554 / $22,797 | $2,188 / $11,956 | $3,916 / $25,862 |
| Truck, player + 1 hire (paid 107 to 112 hours a week) | $1,563 / $8,464 | $2,743 / $13,410 | $4,196 / $22,756 | $2,503 / $10,724 | $5,858 / $31,630 |

**Crewed** (Core-checked route: pitch 07:00 to 23:00, then a bay stop; loads in the bay in the morning; all stock by phone, so the fee applies; a full truck day pays 17 hours a hire, 119 a week):

| Vehicle | Poor: week net | Typical: day / week net | Good | Best | Premium, best |
|---|---|---|---|---|---|
| Truck, crew of 1 | $3,090 | $1,550 / $6,469 | $2,844 / $13,951 | $3,213 / $16,715 | $3,429 / $18,692 |
| Truck, crew of 2 | -$210 | $1,560 / $3,302 | $2,920 / $10,554 | $3,567 / $14,417 | $4,507 / $20,622 |
| Truck, crew of 2, pitch 07:00 to 15:00 only (9 hours a hire a day) | $1,081 | $1,155 / $4,398 | $2,538 / $10,644 | $3,173 / $14,473 | $4,133 / $20,508 |
| Truck, crew of 2, with a scheduled midday bay stop | -$210 | $1,569 / $4,509 | $3,025 / $11,232 | $3,930 / $17,446 | $5,119 / $23,692 |
| Truck with Extra Storage (3 food pieces), crew of 1 | $2,527 | $1,441 / $5,795 | $2,708 / $12,713 | $3,423 / $18,219 | $3,775 / $21,021 |
| Truck with Extra Storage (3 food pieces), crew of 2 | -$771 | $1,465 / $5,566 | $3,027 / $11,533 | $4,189 / $18,118 | $5,567 / $25,387 |
| Cart: hot dog grill + Extra Storage, 1 hire | $949 | $785 / $1,986 | $1,212 / $2,791 | $1,212 / $4,637 | $1,212 / $4,415 |

The midday bay stop row is a route Peter's crews can run today: the pitch closes at 11:00, 13:30 or 16:00 (whichever nets most there), the truck drives to its bay for a 30-minute stop, restocks before it leaves, and opens again on the next half hour. Sales at the bay stops are left out. At the same pitch it adds little ($20 to $560 a week) except at premium spots ($1,160 and $3,070). The truck routes all came out as "pitch, bay" with the bay 3 to 14 minutes away. The hired cart is pushed to and from its bay at walking pace, 70 to 418 minutes each way, all paid, and its pitch closes between 16:30 and 22:00 so the bay stop can open before midnight; the morning-bay form (a night at the pitch) never wins: its long shift is paid from the early-morning push, and where the trip is longest Core skips its stops. Its 04:00 fine is charged only when the plan has the vehicle standing at the pitch at 04:00, not on its way or open (round 3; the first version fined it every day).

The ladder by best route (`--reports compare`, weekday, ceiling): hot dog cart $2,058, coffee cart $2,161, truck with two at the window $4,221. Staying at the best spot earns 83%, 93% and 99% of the best route.

Against the design's targets (S, a small shop run by its owner, about $2,600 a day):

- A cart at a good pitch makes $1,055 to $1,320 a day, 40 to 50% of S. On target.
- A truck with one hire at a good pitch nets about $1,920 a day, 74% of S; at the best pitch $3,250. A little under "about the same as a small shop" at a good pitch, above it at the best.
- A truck with a crew of two loses money at a poor pitch (-$210 a week, -$771 with Extra Storage). With a crew of one it still makes $3,090. Fuel isn't counted, so "a bad pitch loses money" holds only for a full crew.
- Four crewed trucks at good pitches net about $42,000 to $56,000 a week, $6,000 to $8,000 a day, well below a mid-game business (the full fast food outfit, about $21,900 a day).

The player's seven 16-hour days are a ceiling: a player who sleeps works fewer hours, so the played weeks are upper bounds. Crews work them all.

## Does storage cap sales before the day ends?

It depends on the vehicle and, for crews, on the pitch.

- **Played carts without Extra Storage: yes.** A hot dog cart with a cooler runs out of hot dogs 3.8 to 5.1 hours after opening at typical to best pitches and sells 66 to 80% of what the day's customers would buy, after one or two restock trips.
- **Played carts with Extra Storage: mostly not.** First out after 6.7 to 10 hours or never; 85 to 100% of demand.
- **The player's truck: no.** Alone at the window the truck never runs out at a typical or good pitch (first out after 11 hours at the best). The window caps it, not storage.
- **Crewed trucks: at the best and premium pitches, yes.** Loading once, the four-piece truck's 80 hot dogs run out after 5.7 to 7.5 hours at good to premium pitches with a crew of two, and it sells 78 to 95% of demand there; at a typical pitch it lasts 11.6 hours (96%).
- **Crewed carts: hard, and the trip kills them.** A hired cart with Extra Storage sells its 80 hot dogs and stops ($1,212 a day at most in Midtown, 61 to 81% of demand), while its pushes to and from the bay are paid and its pitch closes early: $949 to $4,637 a week. The crewed cart is a poor buy, which suits the ladder (hire for trucks).

**Extra Storage at the same pitch** (`--reports pass`, "The same pitch"; the truck gives up its cooler for it; week net without → with, and days to pay back $900):

| Vehicle | Poor | Typical | Good | Best | Premium, median | Premium, best |
|---|---|---|---|---|---|---|
| Played hot dog cart (cabinet instead of the cooler) | +$271, 23 days | +$1,225, 5 days | +$394, 16 days | +$1,202, 5 days | -$243 | +$2,225, 3 days |
| Truck, player alone | -$554 | -$1,006 | -$1,370 | -$1,001 | -$967 | -$969 |
| Truck, crew of 1 | -$563 | -$697 | +$265, 24 days | +$1,274, 5 days | +$1,568, 4 days | -$35 |
| Truck, crew of 2 | -$561 | -$424 | -$341 | +$1,737, 4 days | +$2,278, 3 days | +$4,764, 1.3 days |

So the cabinet is a real choice. On a played cart it beats the cooler almost everywhere, but pays back in 3 to 23 days, not the 2 days the storage study found at the single best spot. On a truck the player works alone it always loses: one server is the cap, not storage. With a crew it pays only where the crew sells out early, at the best and premium pitches.

## The weekly phone limit: when does a player need a warehouse?

10 boxes of a food are 5,000 units, per commissary and game week. One vehicle's week of each food:

| Vehicle and pitch | Hot dogs a week | Coffee a week | Vehicles one commissary's limit supplies |
|---|---|---|---|
| Hot dog cart (+ cooler or Extra Storage), typical to best | 435 to 883 | | 5.7 to 11.5 |
| Coffee cart, typical to best | | 752 to 1,584 | 3.2 to 6.6 (coffee) |
| Truck, player or crew, typical to best | 462 to 932 | 530 to 1,519 | 5.4 to 10.8 on hot dogs, 3.3 to 9.4 on coffee |
| Truck, premium best | 549 to 954 | 1,455 to 2,367 | 5.2 to 9.1 on hot dogs, 2.1 to 3.4 on coffee |

Croissants and sodas never come close (8 to 64 vehicles). So one commissary's limit runs out at about 3 busy coffee trucks or 6 to 10 hot dog vehicles. With five commissaries and a separate limit at each, a fleet spread over them gets 50 boxes a week of every food. A fleet of the design's size (four trucks) hits the limit only with coffee at the best pitches, all on one commissary. In short: today the limit almost never forces a warehouse.

## Does the 10% fee matter?

Hardly. The player's weekday at the good pitch, all stock hauled from the wholesaler against all stock ordered by phone and restocked at the nearest commissary:

| Vehicle | Wholesaler | Commissary, after the fee | Fee | No trips at all |
|---|---|---|---|---|
| Hot dog cart + cooler | $1,055 | $1,019 | $37 | $1,449 |
| Hot dog cart + Extra Storage | $1,320 | $1,283 | $49 | $1,549 |
| Coffee cart | $1,308 | $1,214 | $25 | $1,530 |
| Truck, player alone | $2,689 | $2,626 | $63 | $2,689 |
| Truck, player + 1 hire | $2,743 | $2,645 | $65 | $2,993 |

The fee is 2 to 4% of a day. What decides a played day is the trip: the restock trips cost $220 to $390 of sales (the "no trips" column), ten times the fee. The coffee cart loses $94 at the commissary mostly because its nearest commissary is further than the nearest wholesaler. For crews the fee is $150 to $800 a week, 2 to 3% of gross, against $3,300 to $6,600 of wages for a truck. A logistics plan saves that fee, but it needs a warehouse, a vehicle, a driver and a logistics manager, so it pays only for a large fleet.

## Crew wages against takings

The game pays a route's hire every hour of the planned shift, from leaving the bay to the evening bay stop's close: 17 hours a day for a 07:00 to 23:00 pitch near its bay, whether the truck sold out at 14:00 or not. That is $472 a day for one hire, $944 for two.

| Truck crew | Poor | Typical | Good | Best | Premium, best |
|---|---|---|---|---|---|
| 1 hire, wages ÷ gross | 48% | 32% | 18% | 16% | 14% |
| 2 hires, wages ÷ gross | 95% | 63% | 37% | 30% | 23% |
| 2 hires, pitch 07:00 to 15:00, wages ÷ gross | 70% | 41% | 24% | 19% | 14% |
| Player + 1 hire (paid while serving), wages ÷ gross | 49% | 26% | 18% | 12% | 9% |

The second hire pays for itself only at premium spots: a crew of one nets more than a crew of two at every ordinary pitch ($16,715 against $14,417 a week at the best) and less only at the best premium spot ($18,692 against $20,622). The reason is the window: one server sells all of a typical or good pitch's customers, so a second adds sales only where a queue forms. That matches the design ("skilled staff pay off at peaks"), but players will learn to hire one. A short crew day (07:00 to 15:00) nets about as much as the full one with two hires at good pitches and more at poor and typical ones, because the evening hours don't pay two wages.

## Parks

The best park pitch (one per district, park presence 0.70, no buildings around) against the good real pitch's weekday:

| Vehicle | Good pitch, weekday May | Park, weekday May | Saturday May | Saturday July | Saturday July, rain | Saturday January |
|---|---|---|---|---|---|---|
| Hot dog cart + cooler | $1,055 | $949 | $1,019 | $1,195 | $606 | $372 |
| Coffee cart | $1,308 | $795 | $866 | $1,586 | $684 | $342 |
| Truck, player alone | $2,689 | $1,812 | $1,978 | $3,033 | $1,259 | $683 |
| Ice cream cart | $662 | $749 | $881 | $1,268 | $283 | $59 |

Summer weekends make a park one of the better pitches, rain halves it, winter empties it. The ice cream cart finally has a home: a July Saturday in a park earns twice its best ordinary weekday. A real park pitch with buildings across the street adds their crowds on top.

## Premium spots and events

Held all day, a premium spot adds $6 to $394 to a hot dog cart's day and $144 to $1,931 to the truck's (`--reports pitches`). The three Midtown spots are the best pitches in the city; Lower Manhattan's are weekday spots (a cart's Saturday there is $250 to $320). Events: festivals pay everywhere for a truck ($148 to $1,650 over the fee) and mostly for a cart; a weekend market's $80 place loses a hot dog cart money at three of the seven places (-$23 to -$56) while the truck gains $256 to $1,040.

## Proposals

1. **Count the weekly phone limit across the fleet, not per commissary** (code: `CommissarySupply` sums the player's deliveries over all commissaries). Reason: with five separate limits a fleet gets 50 boxes of each food a week and never needs a warehouse. Effect: 10 boxes a week supply 5 to 10 hot dog vehicles and about 3 busy coffee trucks in all, so a player builds a supply chain around the fourth or fifth truck. Data-only alternative: `foodBoxesPerWeek.default` 4 per commissary (about 2 to 4 hot dog vehicles each), which still lets a player spread over commissaries.
2. **Make phone delivery cost more than 10%, if the fee should matter**: either 20% or a flat $50 a delivery on top. Reason: at 10% the fee is 2 to 4% of a day, so it never weighs against hauling or a logistics plan. Effect: at 20% a crewed truck pays $460 to $1,620 a week; a flat $50 a day adds $350 a week whatever it sells, which hurts small orders most. If the limit (proposal 1) is the lever, keep 10%.
3. **Price the truck at $25,000** (`unity/BuildEditor/TruckAssets.cs`, still the $15,000 placeholder). Reason: played alone at a good pitch the truck earns $1,400 to $1,600 a day more than the best cart there, so $15,000 plus $8,670 of equipment pays back in about 2.5 weeks. Effect: about 3 weeks, still quick, and the truck costs about 2.5 small-shop setups, which the design's "multiples of the cheapest small-shop setup" allows.
4. **Leave truck staff at 2, but expect players to hire one.** A second hire pays only at premium spots, and a crew of two loses money at a poor pitch. No change proposed; if Peter wants the second slot to matter more, the queue limit for trucks (10) or a faster truck window would be the levers, not wages.
5. **Lower the weekend market's place to $40** (`data/pitches.json`). Reason: at $80 a hot dog cart loses money at three of seven markets. Effect: the cart's losses shrink to under $20 at two places and it gains at the rest; the truck still gains $296 to $1,080.
6. **Give parks their pitches and gigs once the in-game check confirms the game's park areas**: add `park` to catering's event gig areas (`rules.json` `catering.kinds[event].areas`, now waterfront and retail) and one premium spot at a park gate in each district with a park (the design's "a park gate"). Reason: parks now have their own crowd but nothing curated uses it. Effect: weekend park gigs for trucks, and an ice cream or slushie cart has a premium spot that suits it.
7. **No change to storage (40 a cart piece, 80 a truck piece) or to Extra Storage's $900.** Reason: played carts restock as the design wants (one or two trips) and the player's truck isn't storage-bound. Compared at the same pitch, the cabinet pays back $900 in 3 to 23 days on a played cart, never on a truck the player works alone, and only at the best and premium pitches on a crewed truck (1.3 to 5 days there). At $1,500 a played cart at a good pitch would need 27 days, so the earlier proposal to raise it is dropped.
8. **Crewed carts: no change, but say it.** A hired cart pays wages for its pushes to and from the bay (an hour to seven each way), has to close its pitch early to reach the bay before midnight, and sells at most 80 hot dogs a day: $949 to $4,637 a week. If crewed carts should be a real option, the levers are the push speed for crews (`fleet.travel.pushMetresPerSecond`, 1.2) or letting a crew restock at a wholesaler, not storage.
9. **A sold-out crew may drive to its bay stop, and a bay stop counts only in the bay (Core and game side, done in review rounds 2 and 3).** Without it, a crew that sold out stood at its pitch until the player came, and a truck parked 20 m off its bay took that for the bay and never restocked. Now a route with a bay stop restocks as the tables assume; a route without one still stays sold out, as before.

## Not modelled

- Fuel for the truck, the licence ($250 per 60 days) and permits ($500 once), all small next to wages.
- Regulars (+30% over six weeks at a fixed weekly pitch), health grades and competition between the player's own vehicles.
- Live venue crowds (offices' staff counts, theatre and cinema surges), catering gigs.
- The player's sleep: played weeks assume seven 16-hour days.
- Positions are approximate (the map's scale, doors on outline boxes), and a crew's bay is the nearest commissary in the region, as if it rented its space there.
- Sales at bay stops, and the game's half-hour route slots beyond the bay stop's own timing (the pitch opens on the hour).
