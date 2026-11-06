# Vehicle marketing prices

Marketing multiplies one vehicle's passers (design: Marketing). The shipped levels, from `data/rules.json`:

| Level | Footfall | Cart a day | Truck a day |
|---|---|---|---|
| none | 1.00 | $0 | $0 |
| flyers | 1.10 | $20 | $60 |
| local | 1.20 | $45 | $140 |
| citywide | 1.30 | $80 | $240 |

These prices came from the balance calculator run at 20 passers per hour (21 October 2026). At that rate each extra 10% of passers adds about $35 to $55 a day to a cart the player serves at a typical pitch, and $140 to $170 to a truck worked by the player with a hire. Each step up costs more than the one before, so the top level is the worst deal per extra passer:

- Citywide pays for a cart only at good pitches.
- Every level pays for a truck the player works with a hire, even at a typical pitch.
- Crewed vehicles gain less than the fee. A truck its crew runs alone gains about $40 a step, and a cart that already sells out gains nothing.

The shipped `passersPerHour` in `data/demand.json` is still 16. Raising it to 20 is a separate tuning decision. At 16, every level earns back about a fifth less than the figures above, so marketing is a little weaker than it was priced for.

Nothing here has been checked in game yet.
