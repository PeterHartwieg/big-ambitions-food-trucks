# Milestone 3 checks: serving and queues

Build `0.4.0`. Customers now come to the cart as people: they walk up from the pavement, queue beside the cart, are served at the window and walk off. Sales now run through a minute-by-minute queue: customers arrive at random, wait up to about 20 minutes and walk on at a full queue (6 at a cart). Over a day this sells within 3% of the old rule (`docs/balance/cart-economics.md`). You look at how it looks and moves; I read the rest from `Player.log`.

## 1. Automated (me)

- [ ] `dotnet test` passes; `tools/build-mod.sh --generate` builds; the package has the four `data/*.json` files, `rules.json` included.
- [ ] Installed on homepc with the game closed, after your recording is done and the mod was parked.
- [ ] After the session: the log lines in section 3.

## 2. In game (about 15 minutes, one launch)

Use a save whose name starts with **FT**, never the series save. Other gameplay mods off. Debug keys, holding Ctrl: **F7** free food cart, **F9** demand here.

| # | Do | Look for |
|---|---|---|
| 1 | Cart with a hot dog grill and hot dogs (F7 for a free cart). Open at a busy spot at lunch (offices nearby, 11:30 to 13:30), stand at the window and watch for five minutes | Do people walk up from the pavement on their own, line up beside the cart, step forward, stop at the window for a moment and walk off? Does it look natural on camera, or do they bump, slide, spin, stand in the road or walk through the cart? |
| 1b | Same spot, watch one customer from the pavement to the window | The "Sold ..." message and the money come as they reach the window, not before or after. They stand there a few seconds, not a blink |
| 2 | Walk 20 to 30 m away for a minute, then come back | The line grows while nobody serves, some give up and walk off, and it starts moving again when you're back |
| 3 | Walk about 150 m away, then back | People vanish while you're away and the line is back when you return. No one left standing alone |
| 4 | Grab the cart while people are queuing | Everyone walks off; nobody is left standing after half a minute |
| 5 | Open again and sleep or skip time | Nobody left standing at the spot afterwards |
| 6 | If you like: a coffee cart at a morning spot (fast service) | Does a fast queue still look right, or do people pile up at the window? |

For the trailer, item 1 is the shot: a lunch spot with offices, game speed normal. Anything that looks off, write it down, or note the in-game time so I can find it in the log.

## 3. Log lines (me)

- Startup: `Version 0.4.0 loading.`, `Demand data loaded ... cart queue limit 6`, `Active` for every patch, no errors.
- `Cart ... open ...` with `Window: customer service skill 50, speed 1.00 ...`.
- `Queue view: line at cart ...: N of 10 places on the ... side ..., M ways in and out` with N close to 10 and M at least 2.
- `Queue view: customer agent speed ... stopping distance ...` with a stopping distance under about 0.5 m (otherwise people only reach their place by a jump after 20 s).
- `Sale: ...` lines; `Cart ... closed ...: ... walked away: ... tired of waiting, ... at a full queue ...`.
- `Queue view: cart ... line removed with N people (out of range / cleared / scene changed)`, and at quit `Queue view: N people shown this session, M at most at once` with M at most 16.
- No `Queue view:` errors and no `customer expressions unavailable` line.

Not checked this time, on purpose: loading without the mod (the save only fills a queue field that has been in the schema since 0.3.0), pricing, the day summary and Econoview (unchanged code paths). The uninstall step changed only in that it also removes the people; I check it in the log if you run it, but it isn't on the list.
