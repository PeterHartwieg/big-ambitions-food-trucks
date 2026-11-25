# Vehicle marketing prices

Marketing multiplies one vehicle's passers (design: Marketing). `data/rules.json` is the authority for the levels, footfall factors and daily fees. Snapshot of the shipped values at 0.10.12, for reading only:

| Level | Footfall | Cart a day | Truck a day |
|---|---|---|---|
| none | 1.00 | $0 | $0 |
| flyers | 1.10 | $20 | $60 |
| local | 1.20 | $45 | $140 |
| citywide | 1.30 | $80 | $240 |

## Why these prices

The prices came from the balance calculator run at 20 passers per hour (21 October 2026). At that rate each extra 10% of passers adds about $35 to $55 a day to a cart the player serves at a typical pitch, and $140 to $170 to a truck the player works with a hire. Each step up costs more than the one before, so the top level is the worst deal per extra passer:

- Citywide pays for a cart only at good pitches.
- Every level pays for a truck the player works with a hire, even at a typical pitch.
- Crewed vehicles gain less than the fee. A truck its crew runs alone gains about $40 a step. A cart that already sells out gains nothing, because extra passers are worth little once the window or stock is at capacity.

The shipped `passersPerHour` in `data/demand.json` is 16, not the 20 the prices were set at. Raising it is a separate tuning decision. At 16, every level earns back about a fifth less than the figures above, so marketing is a little weaker than it was priced for.

## What has been checked

The feature itself passed in game: picking a level, the midnight charge, the ledger entry, cancelling on an unpaid fee and save/load are checks MK1 to MK7 in [CHECKS.md](../../CHECKS.md#vehicle-marketing). The earnings per level above are calculator estimates. Nobody has measured them in game.
