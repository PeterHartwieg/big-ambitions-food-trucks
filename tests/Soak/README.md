# Core soak

`SoakTests.SixtyDays` runs 20 fixed seeds, each for 60 days, in the ordinary test suite.
The seed determines the fleet (one to four carts/trucks), starting pieces, crew,
routes, hourly player choices, failed payments, rejected cargo and save moments.
Failures include seed, day, time and the last 20 actions. Individual runs print
elapsed time and coverage; required coverage prevents an empty simulation passing.

```sh
dotnet test tests/FoodTrucks.Core.Tests.csproj
dotnet test tests/FoodTrucks.Core.Tests.csproj --filter Category=Soak
dotnet test tests/FoodTrucks.Core.Tests.csproj --filter 'FullyQualifiedName~SixtyDays' --logger 'console;verbosity=detailed'
FT_SOAK_LONG=1 dotnet test tests/FoodTrucks.Core.Tests.csproj --filter Category=SoakLong
```

The long test runs seeds 0–199 for 365 days each. It is reported **skipped** unless
`FT_SOAK_LONG=1`; returning without running is not counted as a pass.
To replay one short failure, temporarily select its seed in `Seeds`, or use
VSTest's display-name filter for that theory row. `new SoakWorld(seed).Run(days)`
is the common entry point for both lengths.

## Call order and checks

The harness uses public APIs and shipped data. `SoakWorld` owns plain equivalents
of game cargo batches, vehicle poses, employee identity and player money; Core
owns the saved service, routes, wage markers, commissary and sales books.
All loaded Core references are replaced before continuing.

Each hour first settles the preceding open-window wage hour. At midnight, routes
and windows have already sold through the boundary, then payroll and crew takings
are settled before marking the day summary. Route hours are counted from
`PlannedShiftMinutes`, including its unsaved midnight preview. The commissary
retries pending charges, generates rent, attempts deliveries and checks the night,
as its adapter does. The player then chooses an action. Each vehicle catches up
through every leg boundary and transit arrival before that hour ends. Sleep
disables player actions and serving for 1440 minutes; it keeps all hourly events
and route catch-up running. Uninstall occurs during the last simulated hour.

`SalesSimulator` simulates individual minutes internally; calls batch up to an
hour using `SalesClock`, splitting at route boundaries and random save moments.
Each sale checks its own minute and consumes one actual cargo unit; global
invariants are checked hourly. This keeps the short matrix within 15 seconds
without stepping inactive vehicles every minute.

Checks include:

- Balance change equals the independent accepted-payment book. Payment keys
  prevent duplicate sales, order payments, wages, payouts, rent, fines and refunds.
  Refusal changes neither book nor balance. Order booking takes no money.
- Initial units plus paid deliveries equal remaining stock plus sales and
  refunds, by item. A second conservation check follows wholesale goods value
  through FIFO cargo, sold costs and refunds. Restock conserves units, loads
  machines first, follows the loader's limits and returns game-rejected leftovers
  with their original batches. Shared pieces and fractional stock weights use
  the shipped tables. Every fitted pool stays within capacity.
- Crew sales accrue by vehicle/calendar day and pay exactly once at the next
  midnight, or at uninstall for the last day. Sale-day and 28-day tax totals,
  per-vehicle revenue/cost and lifetime revenue match the independent sales book.
  Wages, rent, fines and delivery-fee ledger totals match accepted payments.
- A route-opened window sells only within its planned open leg. Legs do not
  overlap and stops never exceed 24 hours. Half-hour route edits use
  `RouteEditor`; rejected edits change nothing. Overnight edits preserve
  yesterday's committed late leg. Invalid older routes are loaded through
  `SaveCodec` and get no plan: overlong stops, overlapping stops, negative
  opening times and a late stop overlapping the next working morning. Routes
  include days off, short travel, midnight crossings and failed parking arrivals.
- Every employee is credited at most once per hour. Route and player-opened
  window hours share Core's paid-hour marker; fired employees settle earned
  hours before removal, and payroll attributes its wages to the vehicles worked.
- Deliveries are paid no earlier than 08:00, in due-day/order order, exactly once.
  Cancelled orders never return. Delivered units and prices equal the original
  orders. Rent release/re-rent cannot regenerate the same vehicle/day charge.
- The exact 04:00 fine set matches the vehicle flags and exemptions, including
  vehicles without a space. A repeated night check produces no extra fine.
- Random `SerializeForSave → Load → Serialize` snapshots compare the complete
  JSON trees and current schema version, then continue on loaded state, including
  active queues, transit, pending payments and takings when present.
- Uninstall pays every remaining crew taking, refunds only retained commissary
  goods (no delivery fees or unpaid orders), clears orders/stock/spaces, cannot
  refund twice and cannot deliver cancelled orders afterwards.

## Boundaries

This is Core integration evidence, not an in-game check. No Unity code is copied,
and neither Core nor the test project configuration is changed.

Real parking/hatch geometry, employee sickness/training, game money-event
exceptions, visual customers/held payments, UI, fuel, catering, reputation and
licence renewal are not simulated: they are separate modules or game-only
adapters outside this brief's interaction chain. Demand is supplied as varying
plain customer rates; prices and wages are fixed at 500 and 1200 cents to keep
the accounting oracle simple. Opening assets and a valid licence/permits are
fixtures. Wages can overdraw, like the adapter's forced payment; deliveries,
rent and fines require funds, and deliveries also receive seeded refusals.

Game-owned cargo, poses and balance persist beside the Core snapshot, as the
game saves them separately; SaveCodec's round-trip assertion covers every Core
field, not game save serialization. Initial cargo is an explicit opening asset
balance rather than fictitious paid deliveries. The harness never removes
equipment, so it never generates the documented legacy/removal overfill case;
new loads are always required to fit.

The stock-only uninstall invariant belongs to `Commissaries.Refund`. The complete
Unity uninstall also refunds vehicle purchase price and paid vehicle cargo;
`Uninstall.RefundCents` is not a Core API, so that calculation and removal are
excluded, with vehicle cargo retained in the stand-in conservation balance.

The short matrix was checked using an in-process xUnit runner because this
sandbox blocks VSTest's TCP socket. The normal `dotnet test` entry point builds
successfully but aborts at the socket handshake. The long run remains opt-in.

Checked 12 October 2026: the isolated short matrix took 8.23 s (20 passed).
The complete suite took 11.06 s wall time: 1,678 passed, zero failed, one skipped
(the opt-in long soak). Core and the test project file are unchanged.
