# Food truck mod: design

Working title "Food Trucks". A public Workshop mod for Big Ambitions that adds street food as an early-game business: a cart first, then a truck, driven to legal spots around the city and worked at the window.

Written 7 October 2026. Background research: [nyc-food-trucks-2026-10-07.md](research/nyc-food-trucks-2026-10-07.md). Nothing here has been built or tested in the game. Code facts come from the 1.0 build of 23 September 2026; see [Technical answers](#technical-answers).

## Decisions so far

| Topic | Decision |
|---|---|
| Audience | Public Workshop mod. Neutral names, works without other mods. |
| Bakery mod | Optional. If installed, its goods can be sold from a display case. |
| Moving | The player drives the vehicle, parks and opens. |
| Staffing | The player works the window first. Hires can run a vehicle alone, including driving it between spots on a schedule, in version 1. Hires are the game's own employees, unassigned to any building; the vehicle's BizMan page puts them on it and builds the route stop by stop. The crew sells what is loaded and the player restocks (Peter, 8 October 2026). |
| Long run | A small fleet is possible. Each vehicle needs its own permit; there is no cap on their number (Peter, 8 October 2026). Never a replacement for buildings. |
| Licence | Fee plus the game's existing Food Safety course, bought by phone from the City of New York. |
| Pitches | Park anywhere legal, plus curated premium spots and temporary event pitches. Area type is automatic from nearby buildings, with hand-drawn zones on top. |
| Illegal spot | The Open button is disabled and says why. No fines. |
| Equipment | Version 1: plain inventory, no placement. The game's own equipment items (hot dog grill, coffee machine and so on): carry one to the vehicle and click it to fit it; fitted equipment uses cargo space. Each piece brings its own storage for the foods it serves (see Restocking). Later: place equipment inside the truck ("tetris"). |
| Restocking | Buy boxes at a wholesaler, carry them out and click the cart or truck to load them, the way the game loads a car. A hand truck full of boxes can be unloaded into the cart the same way. Pushing the cart into a store and loading it at the shelves still works, as with the game's hand truck; kept for now. Storage per piece (Peter, 10 October 2026; replaces one limit per vehicle of 80 and 160 units): each fitted piece of equipment holds 40 units on a cart and 80 on a truck of the foods it serves, a coffee counting a quarter and a pastry half. A piece serving several foods shares its room; the Extra Storage piece takes a slot, serves nothing and doubles every piece; food with no fitted piece doesn't load. Loading a box that doesn't fit whole moves what fits, and the box with the rest stays in the player's hands. The commissary (below) sells whole boxes, and its Fill up (Commissary stock) fills each piece. |
| Economy | Game-calibrated. Real NYC shape, Big Ambitions money. |
| Demand | Own thematic model: area type, neighbourhood character, daypart and product category. Strong peaks, lunch about 3x in office areas. Not based on `trafficIndex`. |
| Ladder | Cart, then truck. |
| Version 1 extras | Weather and seasons, catering gigs, visible queues, health grades, regulars on a route, same-food competition. |
| Game market | Read the game's per-neighbourhood product demand one-way. Trucks don't count as providers. |
| Player view | A "Street food" filter category on the game's city map: offices, factories, nightlife, theatres and cinemas, premium spots. Not expected sales. |
| Tax | Truck sales are added to the game's taxable income through a Harmony patch, at the game's own rate. |
| Show surges | Fixed times near theatres and cinemas, bigger at weekends. |
| Off-screen | Sales away from the player and hired trucks are statistical; customers are visible within about 100 m. |
| Pricing | The cart behaves like a shop: the standard price by default, the price a pricing manager covering the district sets, or the player's own price per product, set in the Prices section of the vehicle's BizMan page (15 October 2026). Where feasible it appears in the game's business screens like a shop (to research: those screens are built around rented buildings). |
| Restocking day | Carts carry about half a day's stock; a refill at a wholesaler during the day is expected. The player buys only what fits, so no full box is needed. |
| Coffee | The step after hot dogs: the industrial coffee machine (the shop machine; the cheap one is an office item), fast service, its own crowds (morning commuters, parks at weekends and on warm afternoons). It should earn more than a hot dog cart at its own spots and cost more to start. |
| Crowds add up | A spot's area types are not exclusive: offices nearby bring an office crowd, a nightclub a nightlife crowd, and the two add up rather than share a total. Every pavement also has ordinary street traffic at every hour. Who buys at a given hour follows who is there then. (Peter, 8 October 2026, after a spot next to offices and a club read as near-empty at lunch.) |
| Nightlife and parks | Nightlife pitches come from buildings: nightclubs, theatres and cinemas nearby. Parks are their own area (Peter, 14 October 2026), found from the game's own park areas. Hand-drawn zones are for what the mod can't find: parks the game has no area for, the waterfront, tourist stretches. |
| Staff skill and queues | Service speed follows the worker's customer service skill times their satisfaction (the game's own measure; the player serves with theirs, 50 at the start): 50 serves at the base speeds the balance uses, low skill about 1.5× slower, top skill about 30% faster. Each vehicle's queue has a limit (cart about 6, truck about 10); arrivals at a full queue walk on. So skilled staff pay off at peaks and cheaper staff off-peak. As in the game, low skill makes customers complain and high skill pleases them, feeding reputation. All three in the data files. (Peter, 8 October 2026; in the game itself skill affects only satisfaction, not speed [code].) |
| Messages | In-world wording: say what happens in the game ("The cart can't open without any equipment installed"), never how to use the interface. Maintenance tools such as the uninstall step may stay plain. |
| Not chosen | Grey-market permits, fines, time-restricted streets, per-area demand curves. |

## Core loop

1. Take the street food course and buy the vendor licence.
2. Buy a cart, buy equipment, carry stock out of a wholesaler and load it into the cart.
3. Push or tow the cart to a pitch, open, serve the queue yourself.
4. Sell out or close, restock, move to the next pitch for the next peak.
5. Save up for a truck: more menu slots, more stock, more staff, catering.
6. Hire staff and add trucks, each with its own permit.

The day is shaped by the demand curve. A good day is a breakfast or lunch pitch in an office area, a restock, and an evening pitch near nightlife or a market. Finding a good spot is the reward: a pitch where offices, venues and transit meet can carry a whole day, and staying there is fine. Moving pays when a spot's peak has passed and another spot's is starting (Peter, 8 October 2026; replaces the earlier rule that staying must earn clearly less).

## Licence and permits

NYC separates the vendor licence (a person) from the permit (a cart or truck), and caps the permits at 5,100 citywide. The mod keeps both layers.

- **Vendor licence.** Needs the game's existing Food Safety course. One fee, renewed every 60 in-game days. The player needs it to open. A vehicle's permit covers its crew, so hires don't need the course.
- **Vehicle permit.** One per cart or truck. No cap on the number of permits.
- **Where.** Both are bought by phone: the game's City of New York contact (the one that sends parking tickets) gets a call dialog. The game has no city hall.

No waiting list and no grey market in version 1. Both are easy to add later as settings.

Decided while building the Core (8 October 2026; numbers in `data/rules.json`): the licence can be renewed in its last 10 days and a renewal adds a full 60 days. The city texts when renewal opens and when the licence has run out. First fees, placeholders until the balance pass: $250 a licence, $500 a permit, both tax-deductible. The permit cap the Core first had (unlocks by days and sales) was dropped before Milestone 4's game side, and so was the free permit for older saves: the mod is unreleased.

## Vehicles

| | Cart | Truck |
|---|---|---|
| How it moves | Pushed on foot, anywhere in the city (slow); bridges use the game's bridge skip with walking time | Driven |
| Equipment slots | 2 | 4 |
| Stock | 40 units per fitted piece, 80 with Extra Storage (a one-piece cart sells out in about 4 hours at a good pitch) | 80 units per fitted piece, 160 with Extra Storage |
| Window staff | 1 | Up to 2 |
| Catering | No | Yes |
| Pitch rules | Sidewalk spots | Curb spots |

Slots and stock are inventory counts in version 1. The tetris layout replaces the slot count later; the item list stays the same.

Decided 9 October 2026 (Peter): the cart shows its fitted equipment on the counter, one piece per slot, using the game's own item model where it suits a cart and a small model of our own where it doesn't. Planned for Milestone 8; see the implementation plan.

## Equipment and menu

Each piece of equipment unlocks products. The menu is whatever the fitted equipment allows, so slots force a trade-off between a wide menu and a fast one.

| Equipment | Products (base game) | Notes |
|---|---|---|
| Hot dog grill | hot dog | Fastest service, low price |
| Burger station | burger, french fries | Slower, higher ticket |
| Coffee machine | cup of coffee, cup of tea | Breakfast peak |
| Ice cream counter | ice cream | Strong in summer, weak in winter |
| Pizza oven | pizza | Slowest, highest margin |
| Cooler | soda can, water | Add-on sales, no prep time |
| Display case | croissant, donut, cupcake | Bought in, no baking; Bakery mod goods here if installed |

Item IDs come from the base game (`ba:itemname_hotdog` and so on). The Bakery mod's IDs are read from its bundle (see Technical answers, row 8).

Each product has a service time. A customer order takes the longest service time of its items, so a burger slows the whole queue.

## Pitches

**Anywhere legal.** The player can open on any street spot that passes the siting rules. The rules follow NYC, simplified:

- not within about 3 m of a crosswalk, subway entrance, driveway or hydrant
- not in front of a building entrance (carts)
- not in a no-stopping zone or on a highway (trucks)

If the check fails, the Open button is disabled and names the rule.

**Pitch demand** comes from the mod's own model (see [Demand](#demand)): the area type around the spot, the neighbourhood's character and the time of day.

**Premium spots.** Two or three curated spots in every neighbourhood, about 15 to 20 in all, with a name and a character: Midtown office lunch, Financial District, a park gate, a university, nightlife streets, the waterfront. They have a traffic bonus and their own demand curve (nightlife peaks at night, parks at weekends). They are first come, first served, as in NYC where trucks claim spots at 3 to 5 a.m.: whoever parks first gets the spot, AI trucks included, and a taken spot shows as taken.

**Event pitches.** Temporary spots announced a few days ahead: a weekend market, a game day, a street festival. High traffic for a few hours, an entry fee, a limited number of trucks.

Decided while building 0.8.0 (9 October 2026, Peter away; numbers in `data/pitches.json`): 17 premium spots, each outside one building and named after its street, picked from the balance calculator's best pitches. The first of the player's vehicles to open on one holds it until it closes, with 1.25 times the passers and the spot's own crowd. Events are weekend markets and street festivals at 7 places; the City of New York texts 3 or 4 days ahead and sells up to 2 places per event by phone ($80 at a market, $120 at a festival). During an event only vehicles with a booked place may open there. There is no game day: the city has no stadium. A market brings a park's crowd on top of the street's (Peter, 14 October 2026: more customers rather than a lower fee), so a hot dog cart covers its $80 place at every market. The park premium spots (one at a park gate in each neighbourhood with a park) wait for the parks' positions from a test bridge run.

## Demand

The mod has its own demand model. It doesn't use the game's `trafficIndex`, which only says how busy a block is and nothing about who walks past or what they want. Demand here answers two questions per hour: how many people pass this spot, and how many of them want what this vehicle sells.

```
customers/hour = footfall × appetite(menu) × market × conditions × reputation
```

All tables live in a data file (`demand.json`) shipped with the mod, so balance changes need no code and players can tune them.

### Areas

Every street spot gets an **area type** in two layers:

1. **Automatic.** The mod reads the building types and business types within about 50 m. This covers every legal spot in the city, and it changes when the city changes: a nightclub opening next door makes the spot more of a nightlife spot.
2. **Hand-drawn zones.** Polygons in the data file add or override the automatic result where the buildings don't tell the story: the theatre blocks in Hell's Kitchen, the Industry City market courtyard, the waterfront, tourist stretches in Midtown, a park the game has no park area for. Curated premium and event pitches set their area type directly.

Parks are found automatically too. The game marks each park with a trigger volume that gives the player its walking-in-the-park happiness boost (`LocationHappinessTrigger`), and the skate park with its own [code, 1.0]. A spot within the classifier's radius of one counts it like a building, at the distance to the park's edge (0 inside): a park votes 1.5 for `park`, the skate park 0.75, so a pitch at a park's gate reads a park presence of about 0.7.

| Area type | Comes from | Who walks past |
|---|---|---|
| Office | office buildings | commuters and office workers, weekdays |
| Retail | shops, cinemas | shoppers, afternoons and weekends |
| Residential | residential buildings | locals, mornings, evenings, weekends |
| Nightlife | nightclubs, bars, theatres | evening and late-night crowds |
| Industrial | warehouses, factories | shift workers, early and late |
| Park | the game's park areas, park zones | joggers and dog walkers in the morning, office workers at weekday lunch, families and walkers on weekend days; empty at night, in rain and in winter |
| Waterfront | waterfront zones, piers | walkers and tourists, warm weekends |

A spot can be several types at once, and their crowds add up: each type has a presence from 0 to 1 that grows with the number and closeness of its buildings (two office buildings nearby read about 0.67), and each brings its own crowd on top of the ordinary street traffic every pavement has. Who buys at a given hour follows who is there then.

### Neighbourhood character

Each game neighbourhood has a character that scales area footfall and shifts appetite. First draft, meant to be argued over:

| Neighbourhood | Character | Strong | Weak |
|---|---|---|---|
| Midtown | towers, commuters, tourists | coffee and bakery 07 to 10, lunch everything | late night, weekends outside tourist spots |
| Lower Manhattan | finance, offices | coffee breakfast, lunch | evenings and weekends almost dead |
| Murray Hill | young residents, bars | weekend brunch (coffee, bakery), late-night pizza and fast food Thu to Sat | weekday lunch |
| Hell's Kitchen | theatres, restaurants, nightlife | pre-theatre 17 to 20, late-night hot dogs and pizza | breakfast |
| Garment District | workshops, cheap offices, through traffic | fast food at lunch and late night | coffee and bakery premium items |
| Industry City | warehouses, factories, weekend markets | coffee and bakery 05 to 08 at shift start, fast food at lunch and 22 to 02 for night shifts, weekend market crowds | weekday afternoons |
| Hamptons | summer resort | ice cream, coffee, bakery on summer weekends | everything in winter |

### Footfall

People passing per hour = (street baseline + Σ area presence × area curve × area day factor) × neighbourhood factor (by daypart and day) × season × weather × promotion. A player-edited data file from before 0.3.1 has no street baseline, so spots without classified buildings get no passers until one is added. Dayparts:

| Daypart | Hours |
|---|---|
| Early | 05 to 08 |
| Morning | 08 to 11 |
| Lunch | 11 to 14:30 |
| Afternoon | 14:30 to 17 |
| Evening | 17 to 21 |
| Late | 21 to 02 |

Example footfall multipliers, weekday (1.0 is an average open hour city-wide):

| Area | Early | Morning | Lunch | Afternoon | Evening | Late |
|---|---|---|---|---|---|---|
| Office | 0.8 | 1.2 | 3.0 | 0.5 | 0.6 | 0.1 |
| Retail | 0.1 | 0.5 | 1.2 | 1.3 | 1.0 | 0.2 |
| Residential | 0.6 | 0.7 | 0.5 | 0.5 | 1.0 | 0.4 |
| Nightlife | 0 | 0.1 | 0.4 | 0.3 | 1.5 | 2.0 |
| Industrial | 1.5 | 0.4 | 1.5 | 0.3 | 0.5 | 1.0 |
| Waterfront | 0.1 | 0.4 | 0.8 | 1.2 | 0.8 | 0.1 |
| Park | 0.15 | 0.5 | 1.4 | 0.9 | 0.6 | 0.05 |

Day of week is per area: offices and industrial drop to 0.2 at weekends; nightlife peaks Thu to Sat; residential, retail and the waterfront rise at weekends. An area can have its own weekend curve, season and rain factors (14 October 2026). Parks use all three: a weekend day runs 0.9 in the morning, 2.4 at lunch, 2.8 in the afternoon and 1.2 in the evening; summer 1.3 and winter 0.35 on top of the month's footfall; rain 0.4 on top of the 0.5 every passer has. Park crowds want ice cream, slushies and snacks about twice as often, cold drinks 1.8 times, hot dogs and coffee 1.2 times, sit-down food less. Weekend here means Saturday and Sunday, after the night's shift (01:00 on Saturday is still Friday night). The game's own `TimeHelper.IsWeekend` also counts Friday, but it only uses that for nightclub fees, so the mod keeps Friday a weekday with its own day factor. The game has no public holidays, so weekends stand for them.

### Appetite

Appetite is the share of passers who want a product category in that daypart. Categories:

- coffee and tea
- bakery (croissant, donut, cupcake, Bakery mod goods)
- fast food (hot dog, burger, fries)
- pizza
- ice cream
- cold drinks

Example base appetite, before neighbourhood shifts:

| Category | Early | Morning | Lunch | Afternoon | Evening | Late |
|---|---|---|---|---|---|---|
| Coffee and tea | 0.40 | 0.30 | 0.08 | 0.15 | 0.05 | 0.02 |
| Bakery | 0.25 | 0.20 | 0.05 | 0.12 | 0.03 | 0.02 |
| Fast food | 0.03 | 0.05 | 0.30 | 0.08 | 0.20 | 0.35 |
| Pizza | 0 | 0.02 | 0.20 | 0.05 | 0.20 | 0.30 |
| Ice cream | 0 | 0.02 | 0.08 | 0.20 | 0.12 | 0.03 |
| Cold drinks | 0.05 | 0.05 | 0.15 | 0.15 | 0.10 | 0.08 |

The neighbourhood table shifts these: Midtown multiplies morning coffee and bakery by 1.4, Industry City multiplies early coffee by 1.5 and late fast food by 1.4, Garment District multiplies lunch and late fast food by 1.3 and bakery by 0.7, and so on.

A menu's appetite is not the plain sum of its categories. The strongest category counts fully and each further category adds half its share, so a wider menu helps but a focused one is close behind. Cold drinks mostly add to the ticket instead of bringing new customers.

### Market

The game tracks demand per product per neighbourhood: 100 minus the number of shops selling it times 100 divided by the optimal number, plus hype and shortage events [code, July decompile]. The mod multiplies by this value / 100, with a floor of 0.3. Then a burger truck in a neighbourhood full of burger bars sells less, and the game's hype events on hot dogs reach hot dog carts too. The trucks don't count as providers in the game's market in version 1, so they don't depress shop demand; adding that is a later choice.

### Conditions and reputation

- **Season.** April to October 1.0, March and November 0.8, December to February 0.6. Ice cream 0.2 in winter, 1.3 in summer. Hamptons footfall 0.3 in winter. The game has no climate seasons, so the mod keeps its own calendar from the in-game day count; the season length is a setting. If the Alcware Seasons mod is installed, the mod follows its season instead.
- **Weather.** Rain is the only weather the game has: footfall 0.5 while it rains, coffee appetite 1.3, ice cream 0.3. Heat and cold come from the season instead: winter raises coffee appetite by 1.3, summer raises cold drinks by 1.4 (ice cream by the 1.3 above).
- **Reputation.** Health grade, regulars bonus and same-food competition, as described below.

### Live crowds from game businesses

Where the game already simulates people, the mod uses them instead of a fixed curve:

- **Nightclubs.** A nightclub's opening hours and customer count drive late footfall on its block. A full club at 01:00 means a crowd outside at closing time. The mod adds a closing-time surge in the 30 minutes after a club closes.
- **Theatres and cinemas.** The game has no show times, so the mod uses fixed surge times for any spot near an open theatre or cinema: theatres let out at about 22:30 (plus a matinee at 17:00 at weekends), cinemas every 2.5 hours from 17:00. Surges are bigger at weekends than on weekdays. Theatre crowds favour hot dogs, pizza and cold drinks; cinema crowds favour ice cream and snacks.
- **Offices.** The number of employees in office businesses nearby scales morning coffee and lunch footfall. A block of busy offices beats a block of empty office buildings.
- **Factories and warehouses.** Shift workers in factory businesses drive the early and late peaks in industrial areas, timed to the game's shift schedules if the mod can read them.

**Partnerships with your own businesses.** If the player owns a nightclub, theatre or office business, they can sign a "street food partner" deal with one of their vehicles. The vehicle gets a reserved spot outside and a share of the venue's crowd at closing time. The venue gets a small satisfaction bonus because its guests can eat. Partnering with AI venues for a fee is a later option. Built in 0.8.0 without the reserved spot: the deal is signed on the vehicle's BizMan page within 50 m of the venue, the venue's crowd counts in full and its surges are 1.5 times as big, and the bonus is 5 points of facility satisfaction after a day the partner sold there (the game's demand cache, row 10, can't carry it).

See [Technical answers](#technical-answers) for what the mod can read. Anything it can't read falls back to the area curves above.

### Showing it to the player

A **"Street food" filter category on the game's own city map**, toggled like the existing filters. The map's filters highlight buildings by building type, business type, neighbourhood, address or a custom condition, and can carry a focus point [code: `CityMapFilters`, `CityMapFilterData.isAvailable`]. Filters:

- Offices (office buildings and office businesses)
- Factories and warehouses
- Nightlife (nightclubs and bars), highlighted while open
- Theatres and cinemas
- Premium spots and today's event pitches, as address filters with a focus point

The player works out which food fits which crowd, which keeps discovery in the game. Adding the category needs one Harmony patch on the map's filter setup, which calls the map's own private filter builders.

### Calibration

The base footfall is scaled so that a truck at a good spot and daypart matches the money target in [Economy calibration](#economy-calibration). The mod multiplies it by the game's customer promotion variable relative to its default, so the difficulty slider still affects trucks.

The footfall and appetite numbers are mine, shaped by the NYC lunch peak (about 3x) and the research note. They are a starting point for playtesting.

## Service, stock and queues

- **Service rate.** About 12 customers per staff-hour at a typical mix, scaled by item service time. A one-person cart serves roughly 12 to 20 an hour; a three-person truck about 40.
- **Queue.** Customers above the service rate join a visible queue at the window. They leave after about 20 minutes of waiting, sooner in rain. A long queue is the signal to hire.
- **Stock.** Each product draws from the stock in the piece that serves it. A sold-out item disappears from the menu for the day. Selling out everything closes the window. Taking a piece out leaves its foods in the vehicle: they stay until sold (they come back on the menu with the piece) and only block new loads.
- **Price.** Uses the game's normal price setting per product. The mod applies its own price-to-conversion curve, calibrated against the game's shop curve.

Decided while building the Core (8 October 2026): patience is 20 minutes ±25% per customer, times 0.6 in rain, and the queue holds at most 12. A customer whose food has sold out walks on rather than buying something else. One simulation, minute by minute, drives both the visible queue and the sales away from the player.

When the player stands in the window, the sale animation and register work the way they do in a shop, if the game allows it on a vehicle. Otherwise the player gets a simple serve action per customer.

## Catering gigs

Bookable jobs from offices, parties and events. Offers arrive as text messages from a "Street food bookings" contact, and the player accepts by replying or calling, using the game's contact and call dialog system (the SDK's BackAlleyDealer pattern). Booked gigs also appear as address filters on the map. Each gig has a date, time, place, guest count (40 to 200), a menu requirement and a fixed fee paid on completion. The truck must be at the place on time with enough stock. Missing a gig costs reputation. Real gourmet trucks earn 30% or more of revenue this way, so mature trucks should see a steady flow of offers.

Decided while building the Core (8 October 2026): only trucks cater. Offers grow with items sold (full rate at 500) to about one every two days, at most two a day, scaled by a booking reputation that a failed gig cuts by 0.25 and a completed one raises by 0.05. Gigs are office lunches on weekdays, parties in the evening and weekend events, each with its own areas, times and guest counts, never more guests than a truck holds. A gig is 2 to 5 days ahead and must be accepted within a day. The truck may arrive up to an hour early and must be there by 10 minutes after the start, with the required food on its menu and one unit per guest, which is then held back from street sales. The fee is the guests × the food's reference price × 1.25. "Missing a gig costs reputation" means the booking reputation, so fewer offers; it doesn't touch street sales. Weekend events can also be at a park gate, a building at the edge of one of the game's parks (Peter, 14 October 2026).

## Health grades

Each vehicle has hidden cleanliness points. Dirty equipment, old stock and skipped cleaning add points. An inspector visits at random intervals: about every 12 in-game weeks for an A, 6 for a B, 4 for a C.

| Grade | Points | Demand |
|---|---|---|
| A | 0 to 13 | 1.0 |
| B | 14 to 27 | 0.9 |
| C | 28+ | 0.75 |

The grade shows on the window. The demand effect is an assumption; there is no NYC data on how much grades change cart sales.

Decided while building the Core (8 October 2026): the points are the sum of daily marks over the vehicle's last 14 open days. An open day without cleaning adds 3 and selling stale stock adds 4, so cleaning every day keeps an A and never cleaning reaches C in two weeks. Closing doesn't wash points away, and doesn't clean, for a crew either. Cleaning happens only at the commissary where the vehicle rents its space: "Clean" in the vehicle's on-foot menu in that bay, a 20-minute time skip (Peter, 15 October 2026). A vehicle without a space can't be cleaned. The grade changes only when the inspector comes. A new vehicle shows "grade pending" (demand 1.0) until the first visit. After a 7-day gap, the inspector may come on any day with the same small chance, and finds a closed vehicle on its next open day. The intervals average 12, 6 and 4 calendar weeks, however often the vehicle opens, and there is no safe stretch after an inspection. The game has no inspections or reputation of its own [code, unverified in game].

## Regulars on a route

Opening at the same pitch on the same weekday builds a regulars bonus for that pitch, up to +30% over about six weeks. Missing a scheduled day removes some of it. This rewards a fixed weekly route over random driving. The numbers are invented and need playtesting.

Decided while building the Core (8 October 2026): regulars belong to the whole fleet, by spot and weekday. A visit of at least an hour adds 0.05, so six weekly visits reach +30%. Each missed week takes 0.075 off that spot and weekday, counted even across days the game skipped.

## Competition

Vehicles within one block of each other add about 10% to the total foot traffic of the spot, but vehicles selling the same product split that product's demand. A hot dog cart next to a burger truck is fine; two hot dog carts halve each other. Shops are already covered by the game's market demand (see Market), so they don't count here.

AI trucks are a later version. Until then, this rule only applies between the player's own vehicles.

Decided while building the Core (8 October 2026): "one block" is 80 m; each open neighbour with something to sell adds 10% footfall, at most 30%. The split is by item, not category: hot dogs and burgers are both fast food, but the cart and the truck sell different things. A category's share is the average over the vehicle's items in it of 1 ÷ (1 + the neighbours selling that item). The split applies after the menu's ranking of its categories, so losing hot dog customers never makes the pizza oven count double.

## Commissary and new foods

Decided by Peter, 9 October 2026, for the eight new foods. These are new game items, and the game delivers only to the player's buildings.

- **Commissaries.** A few fixed commissary garages around the city (in data, like the premium spots), each with a loading bay. The player rents a space for each vehicle by phone (Peter, 15 October 2026; it was in person, at the bay): the commissary's call has "Spaces", which lists the player's food vehicles in the game's list picker, each with its one action there (rent a space here, move its space here from another commissary, or give up its space here). The commissary texts the change into the conversation. A space costs $300 a month per cart and $1,250 per truck (NYC figures, `docs/research/nyc-food-trucks-2026-10-07.md`), charged daily as a tax-deductible expense. The first visit unlocks the commissary's phone contact, as the game's own contacts unlock.
- **Hourly public parking (Peter, 13 October 2026, review round 1).** Rent covers only the hourly public parking increment for a food vehicle standing in the bay of its active rented space, using the same bay test as the 04:00 check and only already resolved bays. Street parking earlier that day stays due, even if the vehicle returns to its rented bay at 23:50. Midnight charges the remaining balance before the new day's first hourly increment. Elsewhere, at a commissary where that vehicle has no rented space, or with no resolved bay, the game's charge stays. Other vehicles and parking tickets are unchanged. One summary per vehicle at midnight logs the waived hours observed since load. *Core tested; pending in-game check.*
- **Ordering.** By text to that contact, for the vehicles with a space there: the new foods, the game's own foods and the new machines, at wholesale price plus a delivery fee: a flat $250 per delivery, once for everything a commissary delivers that morning, however many orders, carts or trucks it is for (Peter, 14 October 2026; it was 10% of the goods). Orders arrive at 8:00 the next morning into the commissary's stock (kept in the save) and are paid when they arrive, goods and fee together, as the game's own deliveries are; an order the player can't pay for then is cancelled and the commissary texts why (Peter, 11 October 2026). Parked at the bay, the vehicle's "Commissary stock" button moves stock both ways (below).
- **Overnight rule (soft).** A food vehicle that isn't within its commissary's bay at 04:00 is fined $250, more than a parking ticket, as a text from the City of New York. Not fined: a vehicle the player is in (sleeping in the truck), one whose crew is still serving, open at a late stop of its route (a route that sold out and closed early doesn't count), and one taken off the map in transit. Without a commissary space every vehicle out at 04:00 is fined, so carts without one pay too: the fine is the push to rent. Also not fined (Peter, 9 October 2026): a vehicle at a building the player rents or owns, a warehouse, a garage or any business. The game records a vehicle inside a building with that building's address, which counts; it has no parking lot belonging to a building, so outside, the vehicle must stand within the bay radius (15 m) of one of the building's entrances.
- **Restocking (Peter, 9 October 2026).** Any of the player's food vehicles in a commissary's bay can load that commissary's stock, whether or not it has its space there.
- **Commissary stock (Peter, 15 October 2026; replaces Restock).** The vehicle's panel in a bay has "Commissary stock", a list of what can move: "Fill up" loads food as Restock did (each fitted piece filled from its foods, a shared piece split evenly), "Take hot dogs: 1,200 here" loads one item and "Return hot dogs: 62 on board" gives one back. Food asks how many, offering as many as fit or all on board. Equipment moves only when picked by name, one piece at a time: taking a piece fits it, as far as the slots allow, and giving a piece back also returns the food that no longer fits without it. What goes back joins the commissary's stock at what the vehicle paid for it, keeps the day it first came on board (taking it again doesn't make stale food fresh) and, for goods first bought from a commissary, that origin for the uninstall's tax refund. A fitted piece that holds stock goes back with what it holds.
- **Cleaning (Peter, 15 October 2026).** A vehicle is cleaned only at the commissary where it rents its space. Standing in that bay at the 04:00 check counts as cleaned for the service day that just ended, so a crewed vehicle stays clean by ending its route in its bay. Without a space a vehicle can't be cleaned, and its grade slips.
- **Ordering screen (Peter, 10 October 2026).** Ordering stays in the commissary's conversation on the phone, the way the furniture stores take orders: the call's "Order stock" shows the game's phone order form with the commissary's foods and machines at box prices, up to 10 boxes an order, the delivery fee and the total; the commissary reads the order back, and "Confirm order" places it. Ordering is by phone only (Peter, 15 October 2026): the bay's button no longer opens the call. The call starts with "Order stock" and "Spaces and standing orders"; without a space there it starts at Spaces and Standing orders.
- **New foods.** Dumplings, tacos, chopped cheese and grilled corn are new game items, with their machines (dumpling steamer, griddle, corn grill) as game items, sold only through the commissary. They are not furniture, so they can't be placed in a building, and the foods have no game market demand (fix round, 9 October 2026). Popcorn, cotton candy, slushies and kabobs turned out to be the game's own items (cinema and theatre concessions, the industrial grill) and are bought at wholesalers like the hot dog. Street food only in version 1: the game's own shops don't stock the new items.
- **Hires.** A crewed vehicle restocks at a commissary bay where it rents a space, before it leaves for a stop; short stock is loaded as far as it goes and the commissary texts what was missing (14 October 2026).
- **Supply.** Phone orders can repeat daily or weekly (the flat fee each delivery, shared with whatever else arrives that morning). Each food's boxes from phone orders are limited per game week, one limit for the whole fleet at all commissaries together (Peter, 14 October 2026), so a growing fleet stocks a warehouse. A logistics manager's plan can deliver from the player's warehouse to a commissary where they rent a space, with no fee. No wholesale contracts (Peter, 14 October 2026).

## Warnings

A food vehicle warns the player the way the game's shops do, with to-dos in the phone (Peter, 15 October 2026). Core's `VehicleWarnings` decides the list from the save and the tables. A menu food at or under a quarter of what its fitted pieces hold gives "low stock", and at none it gives "empty stock", as the game's shelves do. A vehicle with a route warns when it has fewer crew than it takes and when the route fails today's rules. Every vehicle warns without a permit, without a commissary space anywhere, and after a C from the health inspector. The vendor licence gives one fleet to-do from its renewal window on, and another once it has run out. Each warning is its own to-do type, numbered from 9201. Once a game minute the game side makes the to-dos that are missing and completes the ones that cleared. A click opens the vehicle's BizMan page, or the City's conversation for the licence, and the destination button points at where the vehicle stands. The to-dos never reach a save file. The mod takes them out while the game writes a save and puts them back straight after, so a save opened without the mod has none. Lost money is no warning: the overview shows a negative daily income in red, as the game does. *Core tested; pending in-game check.*

## Economy calibration

The target is game money, not NYC money:

- A cart at a good lunch pitch makes about half of what a small fast food shop makes, with no rent and the player's full day.
- A truck on a good route with one hire makes about the same as a small fast food shop, with lower fixed costs and more risk (weather, sell-outs).
- A bad pitch loses money once staff and fuel are counted.
- A fleet of four trucks stays below a mid-game business.

Vehicle prices, equipment prices and permit fees are set as multiples of the cheapest small-shop setup at the current difficulty, so they scale with the game's price settings.

## Technical approach

Every business system in the game hangs off a rented building's `BuildingRegistration`: customers, BizMan, employees, revenue, rivals, tax. A vehicle has none of it, so the mod runs its own simulation and connects to the game at the edges.

- **Vehicles.** Modded cart and truck from the SDK's `Example-Vehicle` pattern.
- **Simulation.** Mod-owned, per vehicle, per in-game minute while open. Inputs: the mod's demand tables, the game's building types around the spot, the game's product market demand and its promotion variable.
- **Customers.** Spawned pedestrians walk to the window and queue. Sales are decided by the simulation; the pedestrians are its visible side.
- **Money.** Revenue paid into the player's account so it appears in the transaction history and counts for tax. If the API only allows a generic income line, the mod labels it clearly.
- **Saves.** State in the save's `modData` (vehicles, equipment, stock, licences, grades, regulars, gigs), or an own file per save if that fails its test. What happens to mod vehicles when the mod is removed is untested; see the implementation plan.
- **Patching.** Harmony where the API has no hook.
- **Courses.** The street food course through the course system, if a mod can register one.

## Technical answers

Read from the current game build (`BigAmbitions.dll` of 23 September 2026, decompiled 7 October) and the Bakery mod's DLL. Nothing has been run in the game yet. **[code]** means read in the decompiled source; **[untested]** marks what still needs one in-game check.

The official mod API is thin: business and vehicle registration, options, asset bundles, load events and a logger. Everything else means calling public game classes directly, plus Harmony where the game has no hook. Harmony is not shipped with the game. A mod bundles it in its `Dependencies\` folder, which the loader reads first.

| # | Question | Answer | How |
|---|---|---|---|
| 1 | Demand inputs | Yes | Per-neighbourhood product demand is readable through `ProductMarketHelper.GetNeighborhoodDemand` (formula unchanged, plus hype, shortage and backorder events). Every building's address, type, business type, owner, rented state and neighbourhood are readable from `BuildingRegistrations`; positions from `CityManager.FindCityBuildingController` once the city is loaded. Neighbourhoods: Midtown, Lower Manhattan, Murray Hill, Hell's Kitchen, Garment District, Industry City, the Hamptons. |
| 2 | Vehicle stock | Yes | A modded `VehicleType` has `maxCargoCapacity`; the vehicle's cargo is a list the mod can read and change (`TryToAddToCargo`, `ReduceFromCargo`, `GetAmountByItemName`). The game's stock HUD already shows it. The SDK's example asset has fields (`isATruck`, `isHandVehicle`) the 1.0 DLL no longer has. |
| 3 | Revenue and tax | Partly | `GameManager.ChangeMoneySafe` pays money in and records a transaction; the mod can give it its own type and label through locale keys. Income tax is not taken from transactions but from each rented building's sales history, so truck sales are untaxed unless the mod adds them. Expenses (wages, licence fees) can use the game's own tax-deductible categories. |
| 4 | Course | Yes, with a patch | The enum has `DiplomaName.FoodSafetyCourse`, but the 1.0 business school has no door for it (seen in game, 9 October 2026), so the mod registers its own course data and offers it on the Market Demands door (see the implementation plan's playtest fixes). The mod checks `EducationHelper.HasCompletedDiploma`. A brand-new course is not practical: course names are a fixed enum and the education screens throw on unknown values. |
| 5 | Weather | Rain only | `RainHelper.isRaining` is readable. There is no snow or temperature. The game's "seasons" are Halloween and Christmas decorations from the real-world date, not climate. Time and day of week are readable, with `onNewHour` and `onNewDay` events. |
| 6 | Visible queues | Partly | No outdoor queue exists to reuse; customers only exist inside buildings. The mod spawns its own characters (the pattern the game uses for passengers getting out of cars), walks them on the navmesh and runs its own line. Street people only exist within about 100 m of the player and vanish when the player enters a building, so queues are visual near the player and sales are statistical elsewhere. |
| 7 | Serving | Custom | The cash register depends on being inside a building and can't be reused. The game has outdoor seller stands (`SellerStandController`) with a standing seller and direct payment; the truck window copies that pattern with its own interaction. A hired worker at the window is a placed character with the register animation, run by the mod. |
| 8 | Bakery mod | Yes | Products `bakeryxx:itemname_bread`, `_bun`, `_chocolatecookie`, `_latte`, `_americano`; equipment `bakeryxx:itemname_breadcounter`, `_bistromachine`. Detected with `ItemsGetter.IsModItem` at city load. |
| 9 | Live venue data | Partly | Opening hours of every business, player or AI, are readable (`scheduleDays`, `BusinessHelper.IsBusinessOpen`). Hourly customer counts exist for the player's own businesses; AI ones only have randomised forecasts. Cinemas and theatres have no show times, only hourly customers. Player offices and factories have shift schedules; AI ones only a head count. |
| 10 | Venue bonus | Yes, another way | The cached list (`cachedFulfilledCustomerDemands`) is rebuilt from the venue's own items on every daily update and item change, and its customers never demand food, so an added entry does nothing. 0.8.0 patches the daily `BusinessHelper.UpdateSatisfaction` instead and raises the facility part after a day the partner worked outside. |
| 11 | Hired drivers | Simulated | The game has no NPC that drives a player vehicle on its own. The private driver drives you, and the taxi fades the screen and teleports. The mod moves a hired truck by marking it in transit for the game's travel-time formula, then placing it in a free parking spot at the destination. A visibly driving truck would need the private-driver code and Harmony; that's a later option. |
| 12 | Push cart | Yes | The game's hand truck is a vehicle the player pushes at walking speed, with cargo. The cart copies that pattern. [untested] whether a modded hand vehicle needs extra tags. |
| 13 | Legal spots | Partly | For cars, "legal" means all wheels inside a parking lane that isn't disabled parking; the game already computes it. There's no data for hydrants, driveways or no-parking zones, and crosswalks only have a tag. Carts on the pavement need the mod's own rule: distance from crosswalks and building entrances. |
| 14 | Save data and UI | Partly | Each save has a `modData` dictionary that nothing in the game uses; the mod keeps its state there as JSON [untested that it saves and loads]. Mods can't add a phone app without patching: the app list is a fixed enum. Contacts, text messages, call dialogs and notifications are usable. |

### What this changes in the design

- **Licence.** Requires the game's Food Safety course instead of a new "Street food" course.
- **Seasons.** The mod runs its own climate calendar from the in-game day. Rain is the only weather. Cold and heat effects go away unless another mod adds temperature.
- **Theatres and cinemas.** No show times exist. The mod uses fixed surge times near open venues, bigger at weekends.
- **AI venues.** Use opening hours, size and business type; real customer counts only for the player's own venues. Partnerships with the player's own venues work fully.
- **Tax.** A Harmony patch adds truck sales to the game's taxable income.
- **Crowd map.** Not a phone app. A filter category on the game's city map, added with a Harmony patch.
- **Hired driving.** Simulated travel and placement, not visible driving, in version 1.
- **Pitch legality.** Trucks: the game's own parking lanes. Carts: pavement with the mod's distance rules. The "anywhere legal" rule therefore means "any parking lane" for trucks.
- **Queues.** Visible within about 100 m of the player; statistical otherwise.

## Versions

**Version 1.** Cart and truck, licence with course, a permit per vehicle, equipment as inventory, anywhere-legal plus premium and event pitches, the thematic demand model with weather and seasons, live crowds from nightclubs, theatres, offices and factories (opening hours for all venues, real counts for the player's own), the Street food map filters, partnerships with the player's own venues, visible queues, catering gigs, health grades, regulars, same-food competition between the player's vehicles, per-save state. The player works the window; hires can serve alone and drive a vehicle along a schedule.

**Later.** Partnerships with AI venues. Equipment placement inside the truck. AI food trucks (possibly through BusinessBridge). Waiting list and grey-market permits as settings. Time-restricted streets.

## Answered design questions

- Version 1 keeps the full scope, built in milestones; see [implementation-plan.md](implementation-plan.md).
- A vehicle's permit covers its crew.
- Shop competition comes from the game's market demand only.
- Carts cross bridges with the game's bridge skip and walking time.

- The cart is pushed on foot and can go anywhere in the city. It's slow, so cross-city moves cost most of a daypart.
- Hired workers can run a vehicle alone in version 1, including driving it between spots on a schedule.
- Premium spots and hand-drawn zones: two or three in every neighbourhood, Hamptons included.
- Premium spots are first come, first served.
