# Marketing and 20 passers an hour

Written 21 October 2026 for the marketing levels in `data/rules.json`. Every number here comes from the balance calculator [calc] and none was checked in game. Method as in the [14 October pass](balance-pass-2026-10-14.md): the `pass` report in `tools/balance`, at passersPerHour 20, the ceiling price, a dry weekday in May, a typical-tier pitch unless a row says otherwise. Figures are weekday gross per day.

## The passers raise

The same change took passersPerHour from 16 to 20, after an AI playtest and playtester feedback showed carts and trucks earning too little. The 16-passer figures differ from the [14 October pass](balance-pass-2026-10-14.md) ($697 for the cart, $1,563 for the truck with a hire) because the calculator and the tables have changed since then; that is drift, not a mistake.

| Vehicle, typical pitch | Weekday gross at 16 | At 20 |
|---|---|---|
| Hot dog cart with cooler, player-served | $753 [calc] | $830 [calc] |
| Truck, played with one hire | about $1,480 [calc] | $2,001 [calc] |

## What each step of passers adds

Marketing multiplies a vehicle's passers. The calculator ran each +10% of passers from 20: 22 is flyers, 24 local, 26 citywide. 28 is one step past citywide, kept to show where the gains tail off. Each cell is what that step adds to the day over the one before.

| Vehicle | 20→22 | 22→24 | 24→26 | 26→28 |
|---|---|---|---|---|
| Hot dog cart with cooler, player-served, typical | $53 | $38 | $38 | $34 |
| The same, good pitch | $68 | $62 | $61 | $58 |
| Truck played with one hire, typical | $141 | $151 | $167 | $119 |
| The same, good pitch | $227 | $232 | $282 | $253 |
| Truck run by a crew of 1, typical | about $40 a step (noisy) | | | |
| The same, good pitch | about $20 to $40 a step | | | |
| Crewed hot dog cart with Extra Storage, typical or good | $0 | $0 | $0 | $0 |
| The same, poor pitch | about $40 a step | | | |

All [calc]. The crewed cart sells out at 80 hot dogs on the typical and good pitches, so more passers sell nothing more. Only at a poor pitch, where it doesn't sell out, do they help.

## Pricing

Each level costs more per step than the one before it. A step's extra cost against what that step adds at a typical pitch:

| Step | Cart costs | Cart gains (player-served) | Truck costs | Truck gains (played with a hire) |
|---|---|---|---|---|
| None → flyers (×1.10) | $20 | $53 | $60 | $141 |
| Flyers → local (×1.20) | $25 | $38 | $80 | $151 |
| Local → citywide (×1.30) | $35 | $38 | $100 | $167 |

All [calc]. Flyers and local pay at a typical pitch for a player-served cart or a truck the player works with a hire. Citywide is close to even for that cart at a typical pitch ($38 against $35) and pays clearly at a good one ($61). For the truck with a hire it clears both. A truck its crew runs alone gains about $40 a step, less than even flyers costs a truck.

Marketing gives a crewed cart that sells out nothing. That matches what an AI playtest saw: four crewed hot dog carts making $30 to $80 a day each. Their limit is stock on board, not passers.
