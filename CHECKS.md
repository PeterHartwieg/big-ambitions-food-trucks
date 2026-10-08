# Milestone 7 checks: catering and Bakery

Build `0.9.0`. Catering gigs for trucks, offered by a new "Street Food Bookings" contact, and the Bakery mod's goods when that mod is installed. On top of 0.8.0 (its list is below). Fresh **FT** save, other gameplay mods off; the Bakery mod on for row 5 only. Debug: **Ctrl+F12** books a gig at the nearest place from in 30 minutes.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 15 minutes)

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+F6 truck, Ctrl+Shift+F6, fit a grill, load 50 hot dogs. Ctrl+F12 near an office | "Debug: catering gig at …". Is "Street Food Bookings" in the contacts? |
| 2 | Drive to the building named, park in front within the half hour | "Catering: the truck serves the guests until …". Open the window too: it sells only the hot dogs beyond the gig's 40 |
| 3 | Wait the hour by the truck (or sleep) | A text "Thanks for the catering … paid" and a catering transaction; 40 hot dogs fewer in the cargo |
| 4 | Play or sleep a few in-game days with the truck having sold some food | Offer texts from Street Food Bookings. Call: take one. Does the dialog read well? |
| 5 | With the Bakery mod on: fit its bread counter, load bread | The truck opens and sells bread |

## 3. Log lines (me)

- `Version 0.9.0 loading.`; `Catering: bookings by phone …`; `Bakery mod: found …` or `not installed`.
- `Catering: day …: N offers from …`; `Catering offer … taken`; `Catering gig … started`; `… completed … paid`; failures with their reason.
- `Map filters: Street food line with 7 filters.`

# Milestone 6 checks: depth (second half)

Build `0.8.0`. Premium spots, the city's events and partner deals with your own venues, on top of 0.7.0 (whose list is below and comes first if time is short). Fresh **FT** save, other gameplay mods off. Debug keys as before, plus **Ctrl+Shift+F11**: a city event at the nearest event place from now for three hours, with one place booked.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 15 minutes, after the 0.7.0 list)

| # | Do | Look for |
|---|---|---|
| 1 | Open the city map, Street food line, "Premium spots and events" | About 17 buildings lit in gold, two or three per neighbourhood. Do any look like silly places for a food cart? Name them and I drop them |
| 2 | Push a cart to a lit building and open within about 40 m of its door | A second message names the spot ("Fifth Street office row: the cart has the spot …"). Open a second vehicle next to it: "your cart already has the spot" |
| 3 | Wait or sleep a few days | A text from the City of New York about a weekend market or street festival. Call the city: "Event places", book one. Does the dialog read well? |
| 4 | Ctrl+Shift+F11 near a lit event place, then open a cart within about 60 m | "The cart has its place at the …"; more customers than before. A second vehicle there is refused ("Your booked places at this event are taken by your other vehicles"); after an unbooked event (a real one you didn't book) the message is "The street is taken by a city event" |
| 5 | If the save owns a nightclub, theatre, cinema or office: park within 50 m, Hires | "Sign a street food partner deal with …". Sign it; open there in the evening |
| 6 | Sleep to the next morning after the partner sold an hour outside a nightclub, theatre or cinema | The venue's facility satisfaction a few points up in BizMan (5 points; the log line gives before and after) |

## 3. Log lines (me)

- `Version 0.8.0 loading.`; `Pitches loaded: 17 premium spots, 7 event places, 2 event kinds.`; `Pitches: 17 of 17 premium spots and 7 of 7 event places found in the city.` (any missing addresses are listed).
- `Patch BusinessHelper.UpdateSatisfaction(BuildingRegistration): Active.`
- `Premium spot …: held by …` / `taken by …`; open lines end with `premium spot …` and `partner …` where they apply.
- `City event …: market at … on day …`; `City of New York: place 1 of 2 booked at event …`; `Event …: place held by …`; `Open: … refused at the …`.
- `Partnership: … signed with …`, `worked day …`, `facility satisfaction … -> …`.
- `Cart … spot on day …` lines show live venues from your own clubs and theatres with their guest counts behind the weights.

# Milestone 6 checks: depth (first half)

Build `0.7.0`. Health grades, regulars, competition between your own vehicles, and live crowds from nearby venues. It includes Milestones 3 to 5, also unplayed; their lists are below and come first if time is short. Fresh **FT** save, other gameplay mods off.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; installed after the mod was parked for any recording.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 10 minutes, after the Milestone 5 list)

| # | Do | Look for |
|---|---|---|
| 1 | Open a cart or truck | The message names the grade on the window ("grade pending" on a new vehicle) |
| 2 | Hold the cart (or sit in the truck) and press Clean | The game's time-skip overlay runs 20 minutes with "Cleaning the window, the counter and the equipment", then "clean for today" |
| 3 | Open two of your vehicles with the same food within a block of each other | Each sells less of that food than alone (the open log line shows "neighbours" above 1 and "customers left by neighbours") |
| 4 | Open next to a theatre or cinema in the evening, or a nightclub as it closes | More customers from 22:30 for about 45 minutes (theatre), at 17:00, 19:30, 22:00 and 00:30 (cinema), or for 30 minutes after the club closes. A closed nightclub brings nobody |
| 5 | If you have time: sleep a few days with a cart open each day at the same spot, never cleaning | A health inspector's message with a grade below A, eventually; the next open message shows it |

## 3. Log lines (me)

- `Version 0.7.0 loading.`; `Reputation through day …: N vehicle days counted` after 05:00 each day with an open vehicle (the night belongs to the evening before).
- Open lines end with `Reputation: grade … x regulars … x neighbours … = …; pitch …`.
- `Cart … spot on day …, HH:00: …; N live venues, now nightlife x… ` hourly while open near venues.
- `Cleaning: … cleaned on day …`; `Health inspection: …` when the inspector comes; `stale … sold` after three days with the same stock.

# Milestone 5 checks: hires

Build `0.6.0`. The game's own employees can run a cart or truck: the "Hires" button on the vehicle's panel puts an unassigned employee on it and builds a route of stops with opening hours. The crew drives (or pushes) to each stop, opens, closes at the stop's time and moves on, and the game pays them at midnight. The player still restocks. 0.6.0 includes Milestone 4, which hasn't been played yet either: its list is below and can share the session.

Use a fresh save whose name starts with **FT**, never the series save. Other gameplay mods off. Debug keys, holding Ctrl: **F6** free truck, **Shift+F6** free licence and permits, **F7** free cart, **Shift+F9** hire a customer service worker (skill 60).

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds.
- [ ] Installed on homepc with the game closed, after the mod was parked for any recording and restored for the session.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 20 minutes, one launch)

| # | Do | Look for |
|---|---|---|
| 1 | Ctrl+F6 for a truck, Ctrl+Shift+F6, load a grill and hot dogs. Ctrl+Shift+F9 twice | Two new employees in My Employees, both "Unassigned". Is there an "unassigned employee" task for each? |
| 2 | Sit in the truck, press Hires | The list shows both employees with skill, satisfaction and wage. Put one on the truck. Does their "unassigned" task go away? |
| 3 | Stop in a legal parking space at a lunch spot. Hires, "Add this spot to the route", open 11:00, close 14:00. Drive about 500 m to another legal space, add a second stop 15:00 to 18:00 | Do the hour lists read well? The messages say "Stop added" with the hours |
| 4 | Drive to a third place, step out and walk 30 m away. Wait (or sleep) until about 10:30 (at least an in-game hour after you last drove it: the crew leaves a vehicle the player has just used where it is) | Before 11:00 the truck disappears ("sets off … arriving about 11:00"), then appears in a parking space near stop 1 with its hatch to the pavement, and the crew opens it. Did it land in a sensible spot? |
| 5 | Walk to stop 1 while it's open | The worker stands inside behind the hatch, dressed as an employee; customers queue and pay. Does the worker look right (height in the window, facing out)? |
| 6 | Sleep through to the next morning | The truck sold through the afternoon at stop 2 (the summary message for each stop), then was off duty. Is there an unassigned wage for the employee in Econoview or the transactions, dated the day they worked? |
| 6b | Next day during stop 1's hours: drive the truck (it closes as you take it) to a wholesaler, step out and go in to buy boxes | The truck waits outside while you shop, then you load it. Walk away: within about an in-game hour of your last drive the crew takes it back to stop 1 and opens |
| 7 | If you have time: Ctrl+F7 for a cart, load it, put the second employee on it, add a stop where it stands with hours starting in a few minutes, then push it 30 m away and let go | The cart goes back to its stop (a few minutes' walk) and opens there with the worker standing behind it |
| 8 | If you have time: in My Employees, assign the truck's employee to one of your buildings (if the save has one) | The truck's crew loses them with a message, and the truck stops at the next stop |

For the trailer: item 5 is the new shot, the crew at the hatch with a queue. Anything odd, write it down or note the in-game time.

## 3. Log lines (me)

- Startup: `Version 0.6.0 loading.`, and `Active` for `EmployeeHelper.RunHourly`, `NoTaskAssignedComplaint.ConditionToComplainMet`, `NoTaskAssignedComplaint.ComplaintHandled` and `TasksUI.CreateTodoTask`, with every patch from Milestone 4.
- `Debug: hired … customer service 60`, then `Crew: … on truck …` with the wage.
- `Route: stop … added …`, `Route: truck … leaves for stop 1 …`, `Route: truck … arrived at …: …, N m from the stop` with N under 60, then `Truck … open by its crew at stop 1 …` with `1 serving`.
- `Window crew: … at the … window.`
- `The crew closes` summaries in the close lines (`notification_hire_closed`), one per stop.
- Wages: the save's transactions show `ba:transaction_unassignedwage` for the crew at about the shift's hours times their wage.
- No `Hire routes`, `Hires`, `Window crew` or `Crew` errors.

Not checked this time, on purpose: a late stop past midnight (Core tests cover it), three crew in one truck, crew satisfaction over weeks (the route's hours feed the game's job demands; watch for "no task" complaints later), the uninstall step with a crew (run Ctrl+Shift+F12 at the end if you like and I check the log).

# Still open: Milestone 4 checks (truck, licence and city systems)

Build `0.5.0`. The food truck is in, with the final model: buy it at General US Trucks, drive it, stop in a parking space and open from the driving panel. Selling now needs a vendor licence and a permit for each vehicle, both bought by phone from the City of New York (the contact that sends parking tickets). The licence needs the Food Safety course. The city map has a "Street food" filter line. You look at how it looks and handles; I read the rest from `Player.log`.

This build changes save data (licence, permits, vehicle types) and replaces the test truck, so use a fresh save. Don't reopen an older FT save that still has a Ctrl+F6 test truck: that vehicle type is gone, and a save with an unknown vehicle breaks. A save from 0.4.0 or earlier gets no help: its cart needs a licence and a permit like any other.

### 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; the bundle has `FoodTruck.prefab` and the truck's textures.
- [ ] Installed on homepc with the game closed, after your recording is done and the mod was parked.
- [ ] After the session: the log lines in section 3.

### 2. In game (about 15 to 20 minutes, one launch)

A new save whose name starts with **FT**, never the series save. Other gameplay mods off. Debug keys, holding Ctrl: **F6** free food truck, **Shift+F6** free licence and permits (skips the course), **F7** free food cart, **F9** demand here.

| # | Do | Look for |
|---|---|---|
| 1 | Without the course: phone the City of New York and try to buy a licence | Does the city show up in the contacts with a call button? The call offers a licence, and refuses it without the Food Safety course. Do the texts read well? |
| 2 | Ctrl+F6 for a truck, then drive it a few blocks, park, reverse, take a corner fast | How does it look on the road (wheels on the ground, size, lights, mirrors) and how does it handle? Too floaty, too slow, tips over? |
| 3 | Ctrl+Shift+F6, then load a grill and hot dogs into the truck (box in hand, click the truck). Stop in a parking space at a lunch spot and press Open here | You step out, the hatch on the right swings up, and people line up on the pavement beside the hatch, not in the road or behind the truck. Do they face the hatch? |
| 4 | Stand at the hatch for a few minutes, then drive off | Sales come in as they reach the hatch. Driving off closes the hatch and the line walks away |
| 5 | Try Open here while moving, in the road outside a parking space, and parked with the hatch towards the road (on the left side of a one-way street) | The truck refuses each time and says why |
| 6 | Open the city map, turn on the Street food filters one by one | Offices, factories and warehouses, nightlife, theatres and cinemas light up; "Your food carts" puts a marker on each cart, green while open (trucks have the game's usual car marker; a cart bought or spawned this session also has the game's marker, always shown, and the filter only turns it green). Does any of it look wrong (homes lit as offices, say)? |
| 7 | If you have time: buy a cart with real money, phone the city and buy its permit ($500), and a licence after the course | The money and the call's messages look right; a cart without a permit refuses to open |
| 8 | If you have time: push a cart into a subway station | Does the game let you take the cart on the subway? (The code says it refuses vehicles; you said it works.) |

For the trailer: item 3 is the new shot. Anything off, write it down or note the in-game time.

### 3. Log lines (me)

- Startup: `Version 0.5.0 loading.`, `Vehicle registered: foodtrucks:vehicletype_foodtruck ... price 15000`, `Demand data loaded ... truck 10; licence $250 for 60 days, permit $500`, `Active` for every patch, including `CityMapFilters.InitializeFilters`, `CityMapFilters.ApplyFilters` and `CityMapFilters.ApplyFilters(CityBuildingController)`, no errors.
- `City of New York: licences and permits by phone.` on each load; `licence refused, no Food Safety course` from item 1.
- `Open: truck ... parking Legal`, then `Truck ... open on day ...`, and for item 5 `parking Illegal`, `NotAvailable` or `no pavement beside its hatch`.
- `Queue view: line at cart ...: N of 14 places on the right side ...` for the truck, with N close to 14.
- `Map filters: Street food line with 5 filters.` and no `Map filters` errors.
- Parking: the day's `ba:transaction_publicparking` charge and any ticket for the truck show in the save's transactions (Econoview).
- No `City of New York` or `City reminder` errors.

Not checked this time, on purpose: delivery of a truck to a warehouse (refused, as for the cart), the licence reminder texts (they need 50 in-game days; Core tests cover the days), the uninstall step with a truck (same code path as the cart; run Ctrl+Shift+F12 at the end if you like and I check the log).
