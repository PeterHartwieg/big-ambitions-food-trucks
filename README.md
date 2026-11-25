# Food Trucks for Big Ambitions

Start with a food cart and grow into a fleet of food trucks. Choose your equipment and menu, find customers around the city, work the serving window yourself or send a crew along a weekly route.

**Version 0.10.12, 7 November 2026.** Each cart and truck can run its own marketing, picked on the vehicle's BizMan page: Flyers, Local ads or Citywide ads bring 10%, 20% or 30% more passers for $20, $45 or $80 a day on a cart and $60, $140 or $240 on a truck. A level starts the next day and its fee is charged at midnight; if the fee can't be paid, the marketing turns off and a notice says so. Earlier 0.10.x releases added the daily shift limit on the Schedule tab, commissary and event addresses with map and GPS actions, the "Street food crew" job in the employee list, safe uninstall for vehicles left with the private driver, truck logos, the dedicated drinks fridge and selling a cart at a commissary. The [evidence table](#evidence) below says which of these were seen in game.

Bugs and balance changes are still possible. We recommend a new save or a backup of your existing save before enabling the mod. Read the removal instructions before unsubscribing.

## Start small and grow

Take Food Safety, buy your vendor licence and get your first cart from General US Trucks. Fit two pieces of equipment plus an optional dedicated drinks fridge, load matching food and work a legal pavement pitch. Learn when and where your menu sells, then save for a truck with four equipment slots plus its own drinks-fridge place, more storage per piece and room for two people working at once.

To retire a cart, close it, park it in a commissary bay and let go. Click it on foot and choose **Sell cart**. The confirmation quotes the cart and its onboard equipment and stock at the game's normal resale prices. Selling ends that cart's business and rental; crew become unassigned, with earned wages retained. Shared commissary stock and orders stay with you.

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

Build with the .NET SDK, Unity 2022.3.62f2 and the official Big Ambitions SDK: `dotnet test`, then `tools/build-mod.sh`. Package the release candidate with `python3 tools/package-release.py --output build/FoodTrucks-0.10.12-rc.zip` and rename it before a final Workshop release. The player-facing change note is `workshop/CHANGENOTE-0.10.12.md`. Contributor rules and release steps are in [AGENTS.md](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/AGENTS.md); decisions are in [docs/design.md](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/docs/design.md).

A supplied local package installs as a complete `FoodTrucks` folder in `ModsLocal`, with the game closed. The DLL or source checkout alone is not installable. Open the folder through **Mods → Mod Creator → Browse mod folder**.

Developer actions and the test bridge require `-foodtrucks-test`; normal players do not need it. Safe uninstall remains available without that option. The development key and command reference is [docs/test-bridge.md](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/docs/test-bridge.md).

**Not released yet:** a versioned vendor API lets another mod bring its own customers to crew-run carts and trucks; Street Food keeps selling, booking and paying. It is inert without such a mod: saves and customers stay as before. Core checks pass; the Unity build and in-game checks are pending ([contract and status](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/docs/vendor-api.md)).

MIT, see `LICENSE.txt`. The truck uses the SDK example's physics setup and dirt textures. Harmony's notice is included in `Dependencies/Harmony-LICENSE.txt`.

## Evidence

Checked means seen in game on the named rig and date. Pending means not yet run in game. Unverified means the build was not launched in game. Full rows and scope: [CHECKS.md](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md).

| Feature | Status | Scope |
|---|---|---|
| Vehicle marketing (0.10.12) | Checked | MK1–MK7 on the Windows test rig, 7 November 2026; the bumped 0.10.12 package itself was not relaunched ([details](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md#vehicle-marketing)) |
| Daily shift limit (0.10.11) | Pending | Automated checks only; the combined 0.10.11 package is unverified ([details](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md#daily-shift-limit)) |
| Commissary addresses (0.10.10) | Checked | Five addresses, cards, GPS and calls on the Windows rig, 28 October 2026; the combined 0.10.10 package is unverified ([details](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md#commissary-addresses)) |
| Crew employee screen (0.10.9) | Pending | Automated checks only ([details](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md#crew-employee-screen)) |
| Event and catering addresses (0.10.8) | Partly checked | Focused Windows passes on 25 October 2026; some legacy cases pending ([details](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md#event-and-catering-addresses)) |
| Truck logos and drinks fridge (0.10.6, 0.10.7) | Checked on Mac | Editing, reload, removal and fridge stock; rendered shop and wider lifecycle checks pending ([details](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md#truck-logos-and-cooler)) |
| Cart sale and unlimited rosters (0.10.4, 0.10.5) | Checked | Mac stocked sale, cancel, payroll and reload; Windows empty-cart sale, cancel and reload, 23 October 2026 ([details](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md#cart-sale-and-rosters)) |
| Vendor API v1 | Pending | In source, not in a released package; in-game checks pending ([details](https://github.com/PeterHartwieg/big-ambitions-food-trucks/blob/main/CHECKS.md#vendor-api-v1)) |
