# Storage per piece: what it does to a day

Written 10 October 2026 for the plan's "Storage per piece" section. Every number is from the balance calculator [calc] and unverified in game. Same method as the cart study ([cart-economics.md](cart-economics.md)) and the new-foods study ([new-foods.md](new-foods.md)): additive crowds, the game's queue, the ceiling price, a dry weekday in May, market demand 100, refills by pushing to the nearest wholesaler (a stand-in for the trip to a commissary). Run it with `dotnet bin/Debug/net10.0/balance.dll --reports storage --patch new-foods-proposal.json` in `tools/balance` (about 30 seconds).

## What changed in the calculator

- Each menu item is held by the one piece that serves it. A piece holds 40 units on a cart and 80 on a truck, doubled with Extra Storage. A piece serving two items is split by what the spot would sell, at their stock weights. That is the player's best split; the commissary's Restock splits evenly instead, which is a little worse for a lopsided pair.
- A stay now restocks when the first item runs out, not when everything has. With one shared limit the items ran out together. Per piece, a hot dog cart with a cooler used to keep selling sodas for the rest of the day after the hot dogs had gone, and that undercounted it.
- Review round 1 (10 October 2026) fixed three gaps. When an item runs out too late for a restock trip, the other pieces now keep selling what they hold until closing (before, the whole stay stopped). A restock keeps the stock on board and fills only each piece's free room, towards the spot's best split, as Core's Restock keeps what is loaded (before, it replaced the stock with a fresh load). A route goes straight on to the next pitch only while every piece holds at least half its room (before, half of all the pieces together, so it could leave with one piece empty). `CartDemand.Plan` parity is unchanged: `Cart.Plan` wasn't touched.

## Headlines

| Vehicle | Holds | Best spot | Units a day | First out | All out | Restocks | Profit | Profit restocking in place |
|---|---|---|---|---|---|---|---|---|
| Cart: hot dog grill | 40 hot dogs | 4 Fifth Street (Midtown) | 104 | 12:45 (3.8 h) | 12:45 | 2 | $1,580 | $1,955 |
| Cart: hot dog grill + Extra Storage | 80 hot dogs | 4 Third Avenue (Midtown) | 133 | 15:40 (6.7 h) | 15:40 | 1 | $2,011 | $2,075 |
| Cart: hot dog grill + cooler | 40 + 40 | 4 Fifth Street (Midtown) | 134 | 12:45 (3.8 h) | never | 2 | $1,716 | $2,114 |
| Cart: coffee machine + display case | 40 + 40 | 2 Fifth Street (Midtown) | 320 | 10:10 (4.2 h) | 15:30 | 1 | $2,020 | $2,252 |
| Cart: coffee machine + Extra Storage | 80 (320 coffees) | 2 Fifth Street (Midtown) | 312 | never | never | 0 | $1,768 | $1,768 |
| Cart: grill (burgers, kabobs) | 40 shared | 4 Fifth Street (Midtown) | 104 | 13:20 (4.3 h) | 13:30 | 2 | $1,756 | $2,092 |
| Cart: grill + Extra Storage | 80 shared | 4 Third Avenue (Midtown) | 118 | 18:15 (9.3 h) | 18:40 | 1 | $1,989 | $2,208 |
| Truck: hot dog, coffee, display case, cooler | 80 each | 4 Fifth Street (Midtown) | 488 | 13:40 (6.7 h) | never | 1 | $4,046 | $4,519 |
| Truck: hot dog, coffee, display case + Extra Storage | 160 each | 4 Third Avenue (Midtown) | 450 | never | never | 0 | $4,189 | $4,189 |

The cart's day runs 09:00 to 01:00 (coffee carts 06:00 to 22:00), the truck's 07:00 to 23:00. "First out" is from opening, full.

Against the shared limit, the same Midtown best-spot days (`--reports single`, before and after):

| Cart | Before (80 shared) | After (40 a piece) |
|---|---|---|
| Hot dog grill + cooler | $1,975, 153 units, 1 restock | $1,716, 134 units, 2 restocks |
| Coffee machine + display case | $2,143, 338 units, 1 restock | $2,020, 320 units, 1 restock |

The best-spot days above came out the same after the review round's fixes, to the dollar: at these spots the last load lasts to closing, so neither the end-of-day selling nor the top-up changes anything.

Best routes over the same 16-hour day (`--reports compare`, the study's ladder; a route restocks on the way between pitches, so it softens the loss):

| Vehicle | Before (shared limit) | Per piece, first run | Per piece, after review round 1 |
|---|---|---|---|
| Hot dog grill + cooler | $1,975 | $1,934 | $2,058 |
| Coffee machine + display case | $2,393 | $2,180 | $2,202 |
| Truck stand-in (four pieces) | $4,123 | $4,201 | $4,130 |

The routes moved because a route now refills when any one piece is below half. The carts gain: a hot dog cart that used to go on with half its sodas and almost no hot dogs now refills first, and the hot dog grill + cooler route comes out $83 above the shared limit. The truck loses $71, most likely because one of its four pieces now drops below half well before the four together do, so it stops for stock more often. Both are within a few percent, and no row changes the conclusions below.

## What it means

- **A one-piece cart sells out in about 4 hours at a good spot.** 40 hot dogs last from 09:00 to 12:45 at Midtown's best pitch, so the lunch rush is cut short and the day takes two restock trips. That is the flag in the brief, and it is real: without Extra Storage a cart is a two-to-three-restock business. Restocking in place would be worth $375 a day, which is the cost of the walks.
- **A cart's two pieces no longer pool.** The hot dog cart with a cooler loses 13% ($1,975 to $1,716): the cooler's 40 units can't hold hot dogs, and drinks sell slower than food. The coffee cart loses 6%: its croissants (40 units = 80 croissants) run out by 10:10, though its 160 coffees last.
- **Extra Storage beats a second food piece on a hot dog cart.** Hot dog grill + Extra Storage makes $2,011, more than hot dog grill + cooler ($1,716, so $295 a day more for the cabinet than for the cooler) and more than the old shared-limit cart ($1,975). The cabinet adds $431 a day over the grill alone and pays back its $900 in about 2 days. On a cart, then, the best second slot is usually storage, not another menu item. I think that's acceptable (a real cart's second cabinet is storage), but it makes the cooler a weak buy.
- **On the coffee cart it is the other way round.** Coffee + Extra Storage ($1,768) earns less than coffee + display case ($2,020): 160 coffees already last the day, so the croissants are worth more than the room.
- **The truck barely needs it.** With four pieces at 80 each the truck's day is $4,046 with one restock; trading the cooler for Extra Storage gains $143 a day and pays back in about 6 days. A truck with 160 hot dogs never sells out at the best spot.
- **Shared pieces split well.** The grill carrying burgers and kabobs runs both out within ten minutes of each other (13:20 and 13:30).

## Extra Storage's price

$900, set in `unity/BuildEditor/ItemAssets.cs` and read by the commissary from the item. That is the price of the cheapest food pieces (display case $895, cooler $875), as Peter asked for "roughly a cheap machine". It pays back in 2 days on a one-piece hot dog cart, 4 days on the shared grill cart and 6 on the truck. That fast payback says it is cheap for what it does on a cart. A price up to about $2,000 would still pay back within a week everywhere it helps. I kept $900 because the cabinet costs a slot, and on a cart the slot is half the menu. If Peter wants the choice between a second food and storage to be harder, raising it to $1,500 is the lever.

The review round asked again whether $900 is too cheap next to a second food piece. Against the cooler, the cabinet's $295 a day more pays back the difference in price ($900 against the cooler's $875) on the first day, and the whole $900 in about 3 days. Against the display case on a coffee cart the cabinet loses $252 a day. So the choice is real on a coffee cart and lopsided on a hot dog cart, and the price doesn't decide it either way: at $1,500 the hot dog cart's cabinet still pays back in about 5 days. The new numbers don't change the picture, so the price stays at $900.

## Days per restock

Restocking now happens several times a day, not every few days. A cart with one piece needs two trips on a good weekday, and one with Extra Storage. A commissary order of whole boxes (500 hot dogs) holds about 12 cart loads (40) or 6 with Extra Storage, so one box lasts a cart about 4 days at Midtown's best spot (104 to 133 sold a day) and a truck's hot dog piece (80) about 6 truck loads.

## Not modelled

- The trip to the commissary is the wholesaler walk. A commissary is often further than the nearest wholesaler, so the restock cost above is a floor.
- Crews don't restock (they sell what is loaded, as before), so a crewed cart without Extra Storage closes after about 4 hours at a busy stop.
