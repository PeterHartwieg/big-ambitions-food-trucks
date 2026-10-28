# Food Trucks for Big Ambitions

Start with a food cart and grow into a fleet of food trucks. Choose your equipment and menu, find customers around the city, work the serving window yourself or send a crew along a weekly route.

**Version 0.10.9, 28 October 2026.** Food cart and truck crew show their vehicle and the job "Street food crew" in the game's employee list, its assigned filter and the employee details, instead of "Unassigned" and "No task". Manage schedule for a crew member opens their vehicle's page. Training and sick-day labels and every other employee are unchanged. Display only: crew stay unassigned in the game's own rules. Automated checks and pending in-game checks: [crew employee screen](docs/release/0.10.9/crew-employee-screen.md).

**Included from 0.10.8:** Event and catering addresses use the game's own building address and street names. City events include GPS directions by phone, including fully booked events. Existing map filters highlight the event/catering building. Older city messages are repaired only for events that have not ended yet. Unavailable catering locations cancel without a reputation penalty; unavailable city locations refund unused bookings. Automated checks and native acceptance status: [event address fix](docs/release/0.10.8/event-address.md).

Bugs and balance changes are still possible. We recommend a new save or a backup of your existing save before enabling the mod. Read the removal instructions before unsubscribing.

## Start small and grow

Take Food Safety, buy your vendor licence and get your first cart from General US Trucks. Fit two pieces of equipment plus an optional dedicated drinks fridge, load matching food and work a legal pavement pitch. Learn when and where your menu sells, then save for a truck with four equipment slots plus its own drinks-fridge place, more storage per piece and room for two people working at once.

To retire a cart, close it, park it in a commissary bay and let go. Click it on foot and choose **Sell cart**. The confirmation quotes the cart and its onboard equipment and stock at the game's normal resale prices. Selling ends that cart's business and rental; crew become unassigned, with earned wages retained. Shared commissary stock and orders stay with you. **Checked on the Mac test rig:** stocked/empty sales, cancellation, crew/rental release and save/reload. A focused Windows sale/cancel/reload smoke also passed on 0.10.5 ([evidence](docs/release/0.10.5/followup-2026-10-23.md)).

Recruit staff through the recruitment agency after Basic HR, set their shifts and plan weekday routes in BizMan. Grow into several vehicles supplied through commissaries, standing orders and warehouse logistics.

Assign as many employees as you need to each vehicle using **Add employee** in its crew panel, then rotate them across weekdays or consecutive shifts in Schedule. The limit applies to people working at once: one on a cart, two on a truck.

## Choose your menu

**19 food and drink options are available without another mod:**

- **Street classics:** hot dogs, burgers, kabobs, fries and pizza.
- **Coffee and bakery:** coffee, tea, croissants, donuts and cupcakes.
- **Treats and refreshments:** ice cream, slushies, popcorn, cotton candy and soda.
- **New street food:** dumplings, tacos, chopped cheese and grilled corn, with their own equipment and commissary supply.

Equipment determines your menu and food capacity. Keep a focused two-piece cart or mix a broader truck menu. Extra Storage trades an equipment slot for more stock capacity.

With the optional **Bakery** mod, compatible equipment also supports bread, buns, chocolate cookies, latte and americano. Bakery is not required.

## Work the window or manage a fleet

- Work the cart or truck hatch with visible customer queues, native fast-forward and a timed work skip.
- Give each truck its own logo with the normal shop shapes, imported PNGs, fonts and colours through **BizMan → Street food → truck → Branding**.
- Set prices per vehicle and review income in BizMan. Player and crew sales are paid at midnight, with daily accounts and income-tax integration.
- Plan routes per weekday and use the game's Schedule tab for crew shifts. Crews travel between stops off screen, serve and restock at their rented commissary bay.
- Rent commissary spaces, order supplies, arrange repeating orders and transfer chosen food or equipment both ways.
- Follow demand shaped by location, nearby businesses, time, weather and season. Explore the Street food map filters, premium pitches and temporary events.
- Manage cleaning and health grades, build regular customers, take catering bookings and arrange partnerships with eligible venues you own.
- Receive stock and vehicle warnings through the game's phone to-do list.

## Getting started

1. Complete **Food Safety** at the business school, through the Market Demands door.
2. Call the **City of New York** for the vendor licence. Buy a cart or truck at **General US Trucks**, then call for that vehicle's permit.
3. Fit equipment and load matching food. For hot dogs, buy a **Hot Dog Grill**; the Industrial Grill serves burgers and kabobs.
4. Park legally, let go of the cart or step out of the truck, click the vehicle and choose **Open for business**. **Work the window** resumes cart work after stopping.
5. Visit a commissary bay to discover its phone contact, then call **Spaces and standing orders → Spaces** to rent a space. Return there overnight for cleaning and to avoid a commissary fine.

Keep some cash for supplies: the day's sales arrive at midnight. Phone stock deliveries arrive the next morning; a $250 fee is shared by deliveries at the same commissary that morning. Each food has a weekly phone-order limit across your fleet.

## Requirements and installation

Big Ambitions 1.0. Windows and macOS bundles included. English text. No other gameplay mod required.

Subscribe, wait for Steam to finish downloading, enable Food Trucks in the Mods menu and restart the game. If you used a local development copy, remove that duplicate with the game closed and use the Workshop copy before loading your modded save.

## Help shape the balance

Please use the Workshop item's **Discussions** for bug reports and balance feedback. Include the mod/game version, platform and other mods. For balance, include difficulty/economy settings, equipment, prices, location, staffing and several days of income and costs.

We want to hear whether the first cart is affordable, the truck is a worthwhile upgrade, crews cover their costs, restocking takes too much time, and different menus and locations offer useful choices. Tell us what was confusing as well as what broke.

## Before unsubscribing

With the mod still enabled, load each affected save, let go of the cart and step out of the truck. Press **Ctrl+Shift+F11 twice within 10 seconds** and wait for the successful removal/refund message. Save after cleanup, then close the game and unsubscribe. If cleanup reports an incomplete refund or removal, keep the mod installed and retry.

The console alternative is `foodtrucks_uninstall` twice. Simply unsubscribing cannot clean up vehicles and outstanding mod state. Old transaction/message labels may show raw keys after removal.

## Credits

Created by Peter Hartwieg. Uses the official Hovgaard Games modding SDK and Harmony by Andreas Pardeike. MIT licence and required notices are included.

## Development

**Included since 0.10.6:** truck logos reuse the normal shop editor, including imported PNGs, text, fonts and colours. Each truck saves its own design and displays it on both fixed side panels. Native Mac checks cover editing, save/restart/reload, removal and body views with the serving hatch open and closed; see the [original verification evidence](docs/evidence/2026-10-22-truck-logos.md) and [final 0.10.6 validation](docs/release/0.10.6/final-verification.txt) for later checks and remaining limits.

**Included since 0.10.6:** carts fit two general pieces plus one drinks fridge; trucks fit four plus one. The dedicated cart fridge sits below the counter on the worker side in 0.10.7; the truck fridge retains its customer-facing position at the rear of the serving counter. Further fridges still use general slots. Compared with 0.10.5, soda capacity doubles to 80/160 cans per fridge before Extra Storage, and standalone soda demand increases. Prices and the Extra Storage multiplier stay unchanged. The original [Mac fitting, stock, sales and reload checks](docs/evidence/2026-10-22-dedicated-cooler.md) are supplemented by the [final 0.10.6 validation](docs/release/0.10.6/final-verification.txt) and [0.10.7 cart-facing checks](docs/release/0.10.7/center-worker.md).

**Released in 0.10.3:** water has been removed from the street-food menu and commissary catalogue. It is a factory ingredient; the cooler serves soda only. Existing saved stock is retained. Core checks cover the removal.

The 0.10.6 baseline validation and remaining manual checks are in [the release evidence](docs/release/0.10.6/final-verification.txt). Peter accepted functionality and save safety following playtests on 18 October 2026. Release preparation focuses on packaging, presentation and community testing; the Workshop upload status is recorded in `docs/release/0.10.0/README.md` in the repository.

Build with the .NET SDK, Unity 2022.3.62f2 and the official Big Ambitions SDK: `dotnet test`, then `tools/build-mod.sh` (add `--generate` when regenerating assets). Package the release candidate with `python3 tools/package-release.py --output build/FoodTrucks-0.10.9-rc.zip`. The build runs the release verifier; repeat it with `dotnet run --project tools/release/VerifyRelease.csproj -c Release -- build/FoodTrucks unity/FoodTrucks/ModManifest.asset`. The player-facing change note is in `workshop/CHANGENOTE-0.10.9.md`; checks and native-test status are recorded in `docs/release/0.10.9/crew-employee-screen.md`. Rename the candidate archive before a final Workshop release.

A supplied local package installs as a complete `FoodTrucks` folder in `ModsLocal`, with the game closed. The DLL or source checkout alone is not installable. Open the folder through **Mods → Mod Creator → Browse mod folder**.

Developer actions and the test bridge require `-foodtrucks-test`; normal players do not need it. Safe uninstall remains available without that option. The full development key/command reference is in `docs/test-bridge.md`.

Historical build-specific checked, pending and unverified observations are preserved in `docs/evidence/readme-history-before-0.10.0.md`. They are not the current release acceptance checklist. The technical design and development history remain in `docs/design.md` and `docs/implementation-plan.md`.

MIT, see `LICENSE.txt`. The truck uses the SDK example's physics setup and dirt textures. Harmony's notice is included in `Dependencies/Harmony-LICENSE.txt`.

## Previous release verification

Unlimited rosters retain the [0.10.4 native checks](docs/release/0.10.4/verification.md). Cart closure retains the [0.10.5 checks](docs/release/0.10.5/verification.md), including pending deliveries, standing orders and earned payroll. Scheduled crews use timed offscreen travel.
