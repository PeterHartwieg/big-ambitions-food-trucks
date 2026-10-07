# Food truck mod: design

Working title "Food Trucks". A public Workshop mod for Big Ambitions that adds street food as an early-game business: a cart first, then a truck, driven to legal spots around the city and worked at the window.

Written 7 October 2026. Background research: [nyc-food-trucks-2026-10-07.md](research/nyc-food-trucks-2026-10-07.md). Nothing here has been built or tested in the game. Code facts come from the 1.0 build of 23 September 2026; see [Technical answers](#technical-answers).

## Decisions so far

| Topic | Decision |
|---|---|
| Audience | Public Workshop mod. Neutral names, works without other mods. |
| Bakery mod | Optional. If installed, its goods can be sold from a display case. |
| Moving | The player drives the vehicle, parks and opens. |
| Staffing | The player works the window first. Hires can run a vehicle alone, including driving it between spots on a schedule, in version 1. |
| Long run | A small fleet is possible, capped by permits. Never a replacement for buildings. |
| Licence | Fee plus the game's existing Food Safety course. |
| Pitches | Park anywhere legal, plus curated premium spots and temporary event pitches. Area type is automatic from nearby buildings, with hand-drawn zones on top. |
| Illegal spot | The Open button is disabled and says why. No fines. |
| Equipment | Version 1: plain inventory, no placement. Later: place equipment inside the truck ("tetris"). |
| Restocking | Drive to a wholesaler and load the vehicle directly. |
| Economy | Game-calibrated. Real NYC shape, Big Ambitions money. |
| Demand | Own thematic model: area type, neighbourhood character, daypart and product category. Strong peaks, lunch about 3x in office areas. Not based on `trafficIndex`. |
| Ladder | Cart, then truck. |
| Version 1 extras | Weather and seasons, catering gigs, visible queues, health grades, regulars on a route, same-food competition. |
| Game market | Read the game's per-neighbourhood product demand one-way. Trucks don't count as providers. |
| Player view | A "Street food" filter category on the game's city map: offices, factories, nightlife, theatres and cinemas, premium spots. Not expected sales. |
| Tax | Truck sales are added to the game's taxable income through a Harmony patch, at the game's own rate. |
| Show surges | Fixed times near theatres and cinemas, bigger at weekends. |
| Off-screen | Sales away from the player and hired trucks are statistical; customers are visible within about 100 m. |
| Not chosen | Grey-market permits, fines, time-restricted streets, per-area demand curves, commissary. |

## Core loop

1. Take the street food course and buy the vendor licence.
2. Buy a cart, buy equipment, load stock at a wholesaler.
3. Push or tow the cart to a pitch, open, serve the queue yourself.
4. Sell out or close, restock, move to the next pitch for the next peak.
5. Save up for a truck: more menu slots, more stock, more staff, catering.
6. Hire staff and add trucks until the permit cap stops you.

The day is shaped by the demand curve. A good day is a breakfast or lunch pitch in an office area, a restock, and an evening pitch near nightlife or a market. Staying in one spot all day should earn noticeably less than moving.

## Licence and permits

NYC separates the vendor licence (a person) from the permit (a cart or truck), and caps the permits at 5,100 citywide. The mod keeps both layers.

- **Vendor licence.** Needs the game's existing Food Safety course. One fee, renewed every 60 in-game days. The player needs it to open. A vehicle's permit covers its crew, so hires don't need the course.
- **Vehicle permit.** One per cart or truck, bought at a city office. The fleet cap comes from the permit count: one permit at the start, more unlocked by reputation or days in business, up to a setting (default 4).

No waiting list and no grey market in version 1. Both are easy to add later as settings.

## Vehicles

| | Cart | Truck |
|---|---|---|
| How it moves | Pushed on foot, anywhere in the city (slow); bridges use the game's bridge skip with walking time | Driven |
| Equipment slots | 2 | 4 |
| Stock | Small (about half a day at a good pitch) | Large (about a full lunch rush and a bit) |
| Window staff | 1 | Up to 3 |
| Catering | No | Yes |
| Pitch rules | Sidewalk spots | Curb spots |

Slots and stock are inventory counts in version 1. The tetris layout replaces the slot count later; the item list stays the same.

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

## Demand

The mod has its own demand model. It doesn't use the game's `trafficIndex`, which only says how busy a block is and nothing about who walks past or what they want. Demand here answers two questions per hour: how many people pass this spot, and how many of them want what this vehicle sells.

```
customers/hour = footfall × appetite(menu) × market × conditions × reputation
```

All tables live in a data file (`demand.json`) shipped with the mod, so balance changes need no code and players can tune them.

### Areas

Every street spot gets an **area type** in two layers:

1. **Automatic.** The mod reads the building types and business types within about 50 m. This covers every legal spot in the city, and it changes when the city changes: a nightclub opening next door makes the spot more of a nightlife spot.
2. **Hand-drawn zones.** Polygons in the data file add or override the automatic result where the buildings don't tell the story: the theatre blocks in Hell's Kitchen, the Industry City market courtyard, the waterfront, tourist stretches in Midtown. Curated premium and event pitches set their area type directly.

| Area type | Comes from | Who walks past |
|---|---|---|
| Office | office buildings | commuters and office workers, weekdays |
| Retail | shops, cinemas | shoppers, afternoons and weekends |
| Residential | residential buildings | locals, mornings, evenings, weekends |
| Nightlife | nightclubs, bars, theatres | evening and late-night crowds |
| Industrial | warehouses, factories | shift workers, early and late |
| Waterfront and park | parks, piers, open space | walkers and tourists, warm weekends |

A spot can mix two types (for example office 70%, nightlife 30%) and blends their curves.

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

People passing per hour = area base × neighbourhood factor × daypart × day × season × weather. Dayparts:

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
| Waterfront and park | 0.1 | 0.4 | 0.8 | 1.2 | 0.8 | 0.1 |

Day of week is per area: offices and industrial drop to 0.2 at weekends; nightlife peaks Thu to Sat; residential, retail and parks rise at weekends.

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
- **Weather.** Rain is the only weather the game has: footfall 0.5 while it rains, coffee appetite 1.3, ice cream 0.3. Heat and cold come from the season instead: winter raises coffee appetite by 1.3, summer raises ice cream and cold drinks by 1.4.
- **Reputation.** Health grade, regulars bonus and same-food competition, as described below.

### Live crowds from game businesses

Where the game already simulates people, the mod uses them instead of a fixed curve:

- **Nightclubs.** A nightclub's opening hours and customer count drive late footfall on its block. A full club at 01:00 means a crowd outside at closing time. The mod adds a closing-time surge in the 30 minutes after a club closes.
- **Theatres and cinemas.** The game has no show times, so the mod uses fixed surge times for any spot near an open theatre or cinema: theatres let out at about 22:30 (plus a matinee at 17:00 at weekends), cinemas every 2.5 hours from 17:00. Surges are bigger at weekends than on weekdays. Theatre crowds favour hot dogs, pizza and cold drinks; cinema crowds favour ice cream and snacks.
- **Offices.** The number of employees in office businesses nearby scales morning coffee and lunch footfall. A block of busy offices beats a block of empty office buildings.
- **Factories and warehouses.** Shift workers in factory businesses drive the early and late peaks in industrial areas, timed to the game's shift schedules if the mod can read them.

**Partnerships with your own businesses.** If the player owns a nightclub, theatre or office business, they can sign a "street food partner" deal with one of their vehicles. The vehicle gets a reserved spot outside and a share of the venue's crowd at closing time. The venue gets a small satisfaction bonus because its guests can eat. Partnering with AI venues for a fee is a later option.

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
- **Stock.** Each product draws from the vehicle's stock. A sold-out item disappears from the menu for the day. Selling out everything closes the window.
- **Price.** Uses the game's normal price setting per product. The mod applies its own price-to-conversion curve, calibrated against the game's shop curve.

When the player stands in the window, the sale animation and register work the way they do in a shop, if the game allows it on a vehicle. Otherwise the player gets a simple serve action per customer.

## Catering gigs

Bookable jobs from offices, parties and events. Offers arrive as text messages from a "Street food bookings" contact, and the player accepts by replying or calling, using the game's contact and call dialog system (the SDK's BackAlleyDealer pattern). Booked gigs also appear as address filters on the map. Each gig has a date, time, place, guest count (40 to 200), a menu requirement and a fixed fee paid on completion. The truck must be at the place on time with enough stock. Missing a gig costs reputation. Real gourmet trucks earn 30% or more of revenue this way, so mature trucks should see a steady flow of offers.

## Health grades

Each vehicle has hidden cleanliness points. Dirty equipment, old stock and skipped cleaning add points. An inspector visits at random intervals: about every 12 in-game weeks for an A, 6 for a B, 4 for a C.

| Grade | Points | Demand |
|---|---|---|
| A | 0 to 13 | 1.0 |
| B | 14 to 27 | 0.9 |
| C | 28+ | 0.75 |

The grade shows on the window. The demand effect is an assumption; there is no NYC data on how much grades change cart sales.

## Regulars on a route

Opening at the same pitch on the same weekday builds a regulars bonus for that pitch, up to +30% over about six weeks. Missing a scheduled day removes some of it. This rewards a fixed weekly route over random driving. The numbers are invented and need playtesting.

## Competition

Vehicles within one block of each other add about 10% to the total foot traffic of the spot, but vehicles selling the same product split that product's demand. A hot dog cart next to a burger truck is fine; two hot dog carts halve each other. Shops are already covered by the game's market demand (see Market), so they don't count here.

AI trucks are a later version. Until then, this rule only applies between the player's own vehicles.

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
| 4 | Course | Yes, existing | The game already has a Food Safety course (`DiplomaName.FoodSafetyCourse`). The mod checks `EducationHelper.HasCompletedDiploma`. A brand-new course is not practical: course names are a fixed enum and the education screens throw on unknown values. |
| 5 | Weather | Rain only | `RainHelper.isRaining` is readable. There is no snow or temperature. The game's "seasons" are Halloween and Christmas decorations from the real-world date, not climate. Time and day of week are readable, with `onNewHour` and `onNewDay` events. |
| 6 | Visible queues | Partly | No outdoor queue exists to reuse; customers only exist inside buildings. The mod spawns its own characters (the pattern the game uses for passengers getting out of cars), walks them on the navmesh and runs its own line. Street people only exist within about 100 m of the player and vanish when the player enters a building, so queues are visual near the player and sales are statistical elsewhere. |
| 7 | Serving | Custom | The cash register depends on being inside a building and can't be reused. The game has outdoor seller stands (`SellerStandController`) with a standing seller and direct payment; the truck window copies that pattern with its own interaction. A hired worker at the window is a placed character with the register animation, run by the mod. |
| 8 | Bakery mod | Yes | Products `bakeryxx:itemname_bread`, `_bun`, `_chocolatecookie`, `_latte`, `_americano`; equipment `bakeryxx:itemname_breadcounter`, `_bistromachine`. Detected with `ItemsGetter.IsModItem` at city load. |
| 9 | Live venue data | Partly | Opening hours of every business, player or AI, are readable (`scheduleDays`, `BusinessHelper.IsBusinessOpen`). Hourly customer counts exist for the player's own businesses; AI ones only have randomised forecasts. Cinemas and theatres have no show times, only hourly customers. Player offices and factories have shift schedules; AI ones only a head count. |
| 10 | Venue bonus | Yes | Add a fulfilled customer demand to the partner venue's cached list after each daily reload. It raises the venue's facility score through the game's own satisfaction maths and is safe for saves. |
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

**Version 1.** Cart and truck, licence with course, permits with a fleet cap, equipment as inventory, anywhere-legal plus premium and event pitches, the thematic demand model with weather and seasons, live crowds from nightclubs, theatres, offices and factories (opening hours for all venues, real counts for the player's own), the Street food map filters, partnerships with the player's own venues, visible queues, catering gigs, health grades, regulars, same-food competition between the player's vehicles, per-save state. The player works the window; hires can serve alone and drive a vehicle along a schedule.

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
