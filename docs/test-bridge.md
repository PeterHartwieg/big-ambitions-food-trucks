# Test bridge

Tooling that lets an agent drive and read the game on the Windows PC without Peter's hands. Built 12 October 2026 after the 0.9.8 agent run, where Peter had to walk, drive and click world objects for the agent, notices faded before the agent could read them, and the clock kept running while the agent worked.

The pieces:

| Piece | Where | What it does |
|---|---|---|
| Notice log | `unity/FoodTrucks/Scripts/` | Every notice and phone text the mod shows is also a `Notice:` or `Phone:` line in `Player.log` |
| Test bridge | `unity/FoodTrucks/Scripts/TestBridge*.cs` | HTTP on `127.0.0.1:8322` inside the game, only when the game was launched with `-foodtrucks-test` |
| `tools/ft` | Mac | Command-line client for the bridge |
| `tools/ft-mcp` | Mac | The same calls as an MCP server (stdio), registered in `.mcp.json` |
| `tools/mock-bridge` | Mac | A fake bridge with canned state, for working on the client and scenarios without the game |
| `tools/scenario` | Mac | Runs a scenario file (steps and checks) against the bridge, writes a report |
| `tools/check-log` | Mac | Checks a `Player.log` against a build's expected log lines |
| `tools/contact-sheet` | Mac | One HTML page of the screenshots a run took |
| `tools/run-remote.sh` | Mac → homepc | Starts the game in Peter's desktop session with the test flags, opens the ssh tunnel, stops the game |
| Soak test | `tests/` | Many simulated days through Core with seeded random play, checking invariants |

## Safety

- The bridge starts only with `-foodtrucks-test` on the game's command line. Without it nothing listens, and the Workshop build behaves as before.
- It binds `127.0.0.1` only. The Mac reaches it through `ssh -L 8322:127.0.0.1:8322 homepc` (`tools/run-remote.sh tunnel`).
- Commands that change a save (money, spawns, time jumps, debug actions) are for throwaway **FT** saves only. `load` and `new_game` refuse any save whose name doesn't start with `FT`, and the bridge refuses every changing command while the loaded save's name doesn't start with `FT`.

## Transport

All bodies are UTF-8 JSON. Every reply has `ok`. A failed call has `ok: false` and `error` (one sentence a person can act on).

### `GET /health`

Answered on the HTTP thread from values the main thread refreshes every frame, so it works while the game loads.

```json
{ "ok": true, "mod_version": "0.9.10", "bridge_version": 1, "scene": "menu|loading|city", "save": "FT bridge", "day": 3, "time": "14:05", "paused": false, "frame": 123456, "load": { "name": "FT bridge", "status": "pending|done|failed" } }
```

### `POST /call`

```json
{ "cmd": "state", "args": { } }
```

Runs on the Unity main thread, one call after another. The HTTP thread waits up to `timeout_s` (default 10, an `args` field) for the result; past that the reply is `ok: false, error: "timed out on the main thread; …"` and the command still finishes in the game. Reply: `{ "ok": true, "op": 17, "result": { … } }`, or `{ "ok": false, "op": 17, "error": "…" }`.

Every call gets an operation number `op`. `{ "cmd": "op", "args": { "id": 17 } }` answers at once, whatever the main thread is doing, with `{ "ok": true, "result": { "id": 17, "cmd": "save", "status": "pending|done|failed", "result": { … }, "error": "…" } }` (`result` only when done, `error` only when failed). After a timeout, ask this instead of sending the call again. The bridge keeps the last 500 operations.

Each request is served on its own thread, so `/health`, `/events` and `op` answer while a `/call` waits.

### `GET /events?since=N`

The bridge keeps the last 2,000 events in a ring buffer: every line the mod logs (`Log.Info`, `Log.Error`), every notice and phone text, and Unity errors and exceptions from any source. Reply:

```json
{ "ok": true, "next": 812, "dropped": 0, "events": [ { "seq": 811, "real": "2026-10-12T14:03:11.204Z", "day": 3, "time": "14:05", "kind": "log|error|notice|phone|unity_error", "text": "…" } ] }
```

`next` is the `since` to pass next time. `dropped` counts events that fell out of the buffer between `since` and the oldest kept.

## Commands

Positions are world metres (`x`, `y`, `z`); `heading` is degrees clockwise from +z, as the mod's log lines give them. Vehicle `id` is the game's vehicle id string, as the mod's log lines print it.

| `cmd` | `args` | Does | `result` | Status |
|---|---|---|---|---|
| `state` | `{}` | Reads everything below. Changes nothing | see "State" | built, not yet run in game |
| `console` | `{ "line": "IRSForcePayment 1" }` | Runs a line in the game's debug console (`DebugLogConsole.ExecuteCommand`) | `{ "output": ["lines the console logged during the call"] }` | built, not yet run in game |
| `debug` | `{ "action": "spawn truck" }` | Runs one of the mod's debug actions by its `Name` in `DebugActions` (the twice-confirmed ones run without the second press) | `{ "action": "…", "notices": ["…"] }` | built, not yet run in game |
| `time` | `{ "pause": true }`, `{ "speed": 4 }`, `{ "run_until": { "day": 4, "time": "04:05" }, "speed": 20 }` | Pauses, sets the game's speed, or runs the clock at `speed` until the time comes, then pauses. Time passes the normal way, so daily processing, the 04:00 check and midnight all run | `{ "day", "time", "paused", "speed" }` at once; poll `state` or `/health` to see `run_until` finish | built, not yet run in game |
| `money` | `{ "set": 50000 }` | Sets the player's money (a plain balance change, no transaction) | `{ "money" }` | built, not yet run in game |
| `teleport` | `{ "who": "player" \| "<vehicle id>", "x", "z", "heading"? }` or `{ "who", "place": "bay:<site>" \| "commissary:<site>" \| "vehicle:<id>" }` | Moves the player (on foot, out of any vehicle) or a mod vehicle. `y` comes from the ground. A vehicle lands on the road at that point; `bay:` puts a truck legally in the bay facing along the paint, `commissary:` puts the player at the garage door, `vehicle:` puts the player on the pavement beside the vehicle's hatch side | `{ "x", "y", "z", "heading" }` | built, not yet run in game |
| `vehicle` | `{ "id", "op": "open" \| "close" \| "step_out" \| "back_to_hatch" \| "enter" \| "exit" \| "restock" \| "rent_bay" }` | The same code the vehicle panel's button runs, with its checks and messages | `{ "done": true, "notices": ["…"], "state": { the vehicle's entry from "state" } }`; `done` says whether it happened | built, not yet run in game |
| `commissary` | `{ "op": "order", "site": "hells-kitchen", "items": [ { "item": "ba:itemname_hotdog", "boxes": 2 } ] }` | The phone order form's confirm without the call: the same Core quote and order code and log lines, nothing paid until delivery. `item` is the item key or its name | `{ "orders": [ { "n", "site", "item", "units", "goods", "fee", "total", "due_day" } ] }` | built, not yet run in game |
| `crew` | `{ "op": "assign" \| "unassign", "vehicle": "<id>", "employee_id": "<id>" }` | The BizMan vehicle page's crew dropdown: assign puts the hire in the next free slot | `{ "done": true, "notices": ["…"], "state": { the vehicle's entry } }` | built, not yet run in game |
| `op` | `{ "id": 17 }` | The outcome of an earlier call (see `POST /call`) | `{ "id", "cmd", "status", "result"?, "error"? }` | built, not yet run in game |
| `ui` | `{ "op": "list" }` | Lists the visible, interactable buttons and dropdown options, and the visible text blocks of open dialogs and the phone | `{ "buttons": [ { "id": 3, "text": "OPEN HERE", "path": "…" } ], "texts": ["…"] }` | built, not yet run in game |
| `ui` | `{ "op": "click", "text": "OPEN HERE" }` or `{ "op": "click", "id": 3 }` | Invokes the button's `onClick` (text match is case-insensitive and must be unique) | `{ "clicked": "…" }` | built, not yet run in game |
| `ui` | `{ "op": "escape" }` | What Esc does: closes the top dialog, list or the phone | `{ "closed": "…" }` | built, not yet run in game |
| `shoot` | `{ "name": "bay-day", "pose": "<pose>", "target": "<vehicle id>" \| "bay:<site>" \| "commissary:<site>", "width": 1600, "height": 900 }` or `{ "name", "x", "y", "z", "yaw", "pitch", "fov" }` | Renders one frame from a camera of the bridge's own (the game's camera is untouched, no UI in the picture) and writes a PNG | `{ "path": "C:\\…\\FoodTrucksTest\\shots\\bay-day.png" }` | built, not yet run in game |
| `save` | `{ "name": "FT bridge" }` | Saves the city under that name | `{ "name" }` | built, not yet run in game |
| `load` | `{ "name": "FT bridge" }` | Loads a save from the main menu or from the city | `{ "loading": true }`; poll `/health` for `scene: "city"` | built, not yet run in game |
| `new_game` | `{ "name": "FT bridge" }` | Starts a new game from the main menu with defaults (story mode off if the game allows, Normal), skipping intros and pop-ups the bridge can close | `{ "loading": true }` | not available: `ok: false` (see "Game side, as built") |
| `quit` | `{}` | Quits the game | `{}` | built, not yet run in game |

Poses for `shoot` (the camera is placed relative to the target and looks at it):

| Pose | Camera |
|---|---|
| `hatch-pavement` | 3 m out from the truck's hatch side at eye height (1.65 m), looking into the window |
| `hatch-close` | 1.5 m out from the hatch, eye height |
| `vehicle-side` | 6 m out from the hatch side, 1.8 m up, the whole vehicle in frame |
| `vehicle-overhead` | 12 m up and 8 m back, looking down 45° |
| `queue` | 8 m along the pavement from the hatch's front end, 1.8 m up, looking along the line |
| `cart-counter` | 1.5 m from the cart's queue side, 1.9 m up, looking down at the counter |
| `bay-pavement` | from the pavement across from the bay's middle, eye height |
| `bay-road` | from the driving lane 15 m along from the bay, 1.5 m up |
| `door` | 6 m out from the commissary's door, eye height, looking at it |

## State

```json
{
  "save": "FT bridge", "scene": "city",
  "clock": { "day": 3, "weekday": "Wednesday", "time": "14:05", "paused": false, "speed": 1, "run_until": null, "arrived_at": null },
  "load": { "name": "FT bridge", "status": "done", "error": null },
  "money": 8164.0,
  "player": { "x": 0, "y": 0, "z": 0, "heading": 0, "in_vehicle": null, "at_hatch": null, "carrying": null,
              "activity": "on_foot", "clock_held_by": null },
  "licence": { "until_day": 60, "permits": 2, "course_done": true },
  "vehicles": [
    {
      "id": "…", "kind": "truck|cart", "name": "Food truck",
      "x": 0, "y": 0, "z": 0, "heading": 0,
      "legal": true, "legal_reason": "…", "hatch_side": "left|right", "hatch_to_pavement": true,
      "open": true, "serving": "player|crew|nobody", "crew": ["name"],
      "pieces": [ { "item": "hotdoggrill", "units": 62, "cap": 80 } ],
      "stock": [ { "item": "Hotdog", "amount": 62 } ],
      "queue": { "length": 4, "places": [ { "x": 0, "z": 0, "occupied": true } ] },
      "commissary": { "site": "hells-kitchen", "in_bay": true },
      "route": { "stops": 2, "next": "…" }
    }
  ],
  "commissary": {
    "bays": [ { "site": "hells-kitchen", "status": "resolved|pending|failed", "point": { "x": 0, "z": 0, "along_x": 0, "along_z": 1 }, "entrance": { "x": 0, "z": 0 } } ],
    "spaces": [ { "site": "hells-kitchen", "vehicle": "…" } ],
    "orders": [ { "n": 3, "site": "…", "items": "2 boxes …", "total": 1544.84, "due_day": 4 } ],
    "stock": [ { "site": "…", "item": "Hotdog", "amount": 500 } ],
    "unpaid": { "rent": 0, "fines": 0 }
  },
  "ledger_today": { "street_food": 0.0, "crew_waiting": 0.0 },
  "notices_recent": ["the last 20 notice texts, newest last"],
  "transactions_today": [ { "label": "…", "type": "…", "amount": -1544.84, "day": 3, "time": "08:00" } ],
  "employees": [ { "id": "…", "name": "…", "skill": 60.0, "satisfaction": 80.0, "wage": 14.5 } ]
}
```

Fields the game side can't fill yet are `null`, never left out, so scenarios can test for them.

## Game side, as built

Built 12 October 2026 in `unity/FoodTrucks/Scripts/` (`Notices.cs`, `TestBridge*.cs`). Not yet run in game; every game call below comes from the 1.0 decompile, and the ones to check first are listed in CHECKS.md, "Test bridge". Where the build differs from the contract above, this section wins.

**Startup.** One line at the mod's initialisation load: `Test bridge: listening on http://127.0.0.1:8322/.` or `Test bridge: off (the game was started without -foodtrucks-test).` A taken port logs `Test bridge: could not listen on 127.0.0.1:8322 (…); … The bridge is off.` With the flag the bridge also sets `Application.runInBackground`, so the game keeps running when its window loses the focus. `Test bridge: stopped.` when the mod unloads.

**Notice log.** Every notice the mod shows is a `Notice: <text>` line and every text it sends a `Phone: <contact>: <text>` line, with the text resolved as the game shows it (`LocalizorManager.Localize(key, data)`). A key the game has no text for is logged as `<key> {name=value, …}`. The player's own line in a call (the commissary order read-back) is `Phone: <contact>: [player] <text>`. What the player sees is unchanged.

**FT guard.** `console`, `debug`, `time`, `money`, `teleport`, `vehicle`, `commissary`, `crew`, `save` and `ui escape` need the city and a loaded save whose name starts with `FT`. `ui click` needs it in the city but works at the main menu. `state`, `ui list`, `shoot`, `op` and `quit` work with any save. `load` from the city needs an FT city. `save`, `load` and `new_game` take only names that start with `FT`.

**Scene.** `menu` covers the main menu and the character creator; `loading` covers the loading screen and the step between a save and the city; `city` is the city with a save loaded.

**`/events`.** `since` is the first number wanted: the reply has the events numbered `since` and later, and `next` is the number after the newest. Numbers start at 1. The mod's own error lines arrive once, as `error`, not again as `unity_error`.

**Operations.** Every `/call` reply carries `op`, and `{ "cmd": "op", "args": { "id": N } }` gives that call's status (see `POST /call`). Requests are served each on their own thread; the calls still run one after another on the main thread.

**`/call` that finish later.** `save` (until the file is written), `teleport` of the player (until the move out of a building or vehicle ends) and `shoot` (8 frames) answer when they finish, within `timeout_s`. Past it the reply is `timed out on the main thread` and the work still finishes.

**`time`.** `speed` multiplies the player's own game speed from Options (`GameManager.SetMinutesMultiplier`), from above 0 to 60; 1 is normal play. `speed` alone also unpauses. `run_until` takes `day` as a number or `"+N"` (days from today; default today) and `time` as `"HH:MM"`; it runs the clock at `speed` (default 1), checks every frame, and at the time pauses (the game's own pause, `SetPlayerPause`) and sets the speed back to 1. With the bridge on, a Harmony prefix on `GameManager.RunMainGameTick` (logged `Patch GameManager.RunMainGameTick(Single), test bridge: Active.`) keeps any step of the clock from going past the target minute, so it stops on that minute (its hour's events included, for a target on the hour). `state.clock.run_until` is the target while it runs; `state.clock.arrived_at` is where it stopped ("day D HH:MM") until the next `time` call. Anything that unpauses is refused while a screen holds the clock (the phone, the map, a call, an activity, a menu) or a time skip runs. `state.clock.speed` reads the game's multiplier back.

**`money`.** Writes the balance directly. The top bar follows on its next update.

**`teleport`.** `y` is the highest surface of the game's Ground layer at `x`, `z`. The player goes through the game's `GameManager.SetPlayerPosition` (out of a building or garage first, then a NavMesh warp) after leaving the hatch or the driver's seat, and must land within 3 m of walkable ground. A vehicle goes through `VehicleHelper.TeleportVehicleToGround` and is refused while it is open. `bay:<site>` uses the bay's first resolved point and faces the truck the way that puts its hatch to the pavement (the mod's hatch check), else along the paint. `commissary:<site>` is for the player only and puts them 1.5 m out from the entrance nearest the bay (the first entrance while the bay isn't resolved), facing it. `vehicle:<id>` is for the player only: 1.5 m out from the vehicle's side at the hatch (a truck's right, a cart's queue side), facing it.

**`vehicle`.** The reply's `done` says whether the operation happened, read from what it should change: `open` the vehicle is open afterwards, `close` it is closed, `step_out` the player is off the hatch, `back_to_hatch` the walk has started or the player is placed, `enter` the player is in it or walking to it, `exit` they are out, `restock` the cargo changed, `rent_bay` the vehicle's space is new or moved. A refusal by the mod's own checks gives `ok: true, done: false` with its notice. `open`, `exit`, `restock` and `rent_bay` are buttons on the vehicle's panel, which shows only while the player drives the truck or pushes the cart, so they are refused otherwise (`enter` first). `open` and `exit` park with the game's Park button, which does nothing while the game is paused, so they are refused while paused. `close` is the hatch panel's Close when the player is at that truck's hatch, otherwise the close a grab runs (`CartSelling.CloseByPlayer`). `enter` is the vehicle overlay's Enter vehicle (`DriveVehicle`): further than 2.5 m away the player walks there first, and the reply comes before they arrive. `back_to_hatch` also replies at once and the player then walks to the rear door. `restock` runs the Commissary button's Restock (or its "nothing in stock" message); `rent_bay` runs its Rent or Move. `notices` has only the notices shown during the call.

**`ui`.** Each entry has `kind`: `button` or `option`. Options are listed for every visible dropdown of the game's own kind (`UI.Elements.Dropdown`), open or not; clicking one selects it as its row would. The commissary's list picker is such a dropdown with a confirm button: click the option, then the confirm button. Ids number the last list, top to bottom; a click by id refuses an entry no longer on screen. A click by text lists the near matches when none or several fit. `texts` reads the open phone, call, confirmation, warning, list picker, pause menu, activity panel and the notices on screen; at the main menu, every canvas. `escape` runs the game's `CancelButtonHandler.HandleEscapeClick`, except for the list picker, which the game's Esc ignores and the bridge closes with its Cancel. With nothing open it refuses, because Esc would open the pause menu. It works in the city only.

**`shoot`.** The camera copies the game camera's settings and HDRP camera data, renders 8 frames into its own texture, then the PNG is written and the camera removed. `fov` defaults to 60. Vehicle poses use the truck's hatch (on its right, at the player's place behind it) or the cart's queue side; `bay-pavement` stands 6 m from the bay on its pavement side, `bay-road` 15 m along and 3 m towards the road; `door` stands 6 m out from the entrance nearest the bay.

**`save`.** `GameManager.SaveGame(name)`, as the pause menu's Save; the reply comes when the file is written.

**`load`.** From the city it is refused unless the loaded save's name starts with FT too, since leaving skips the game's unsaved-changes question. A second load while one is pending is refused. Progress is in `state.load` and `/health`'s `load`: `pending` from the call, `done` once the city is up with that save and the mod has attached to it since the call (so reloading the same name doesn't count the old city), `failed` with `error` when the game reports a broken save or nothing arrives within 300 seconds. Log lines `Test bridge: load '<name>' done: day D, the mod's save data attached.` or `Test bridge: load '<name>' failed: ….` Finds the newest save of that name in the current version's folder. From the main menu it runs what the Load screen's button runs (`TransitionToSave` then `LoadScene.LoadTransitionToSave`) without its two questions (a save never played with mods, a different mod list). From the city it goes to the main menu first as the pause menu's Main menu does (`LoadScene.LoadMainMenuFromCity`, without asking about unsaved changes) and loads from there.

**Limits** (from `docs/test-bridge-mac-review.md`, kept as they are). A list picker takes two clicks, the option and then the game's confirm button, whose label only a live `ui list` shows. Map picks, drags and placing items in the world can't be done through `ui`. `ui list` checks canvases, fading and the screen's edges, not scroll masks or windows on top, so a clipped or covered control can still be listed; list again after any change before clicking by id.

**`new_game`.** Not available: `ok: false`. The game makes a new game's character only in the character creator's screens (`MainMenuController.StartNewGame`, then the Intro scene sets the character and the save name), which the bridge can't fill in reliably. Start a game by hand once, save it under an FT name and use `load`.

**`quit`.** Replies, then half a second later waits for any save being written and calls `Application.Quit`.

**`state`.** As above, with these readings:

- `clock`, `money`, `player`, `licence`, `commissary`, `ledger_today` are null outside the city; `licence` and the rest are null while the mod's save data isn't loaded.
- `player.carrying` is the item name in the player's hands. `player.activity` is `driving` (in a truck or car), `pushing` (a cart or hand truck), `starting` (from Open until the hatch's activity panel runs), `at_hatch`, `walking` (a walk the game runs to a goal: enter, back to the hatch) or `on_foot`. `player.clock_held_by` is null, `time skip`, or the open screens that hold the clock (`phone`, `map`, `call`, `activity panel`, `pause menu`, `confirmation`, `warning`; `a screen` when none of those is known).
- `licence.until_day` is null without a licence; `permits` counts vehicles with a permit; `course_done` is the game's diploma check for the Food Safety course.
- A vehicle's `name` is its type's name; `heading` is null when it isn't on the map. For a truck `legal` is the game's own wheel parking test, as its panel's LEGAL/ILLEGAL (`Legality.VehicleLegallyParked`), and `legal_reason` starts `wheel parking test:`; for a cart it is the mod's pavement rule (`Legality.Check`), `legal_reason` starting `pavement rule:`.
- `hatch_side` is `right` for a truck (where its hatch is); for a cart it is the side its queue formed on, null while no line is laid out. `hatch_to_pavement` is the mod's hatch check for a truck, null for a cart.
- `serving` is `player` when the sales tick counts the player at the window, `crew` when a crew member serves, else `nobody` (always `nobody` when closed).
- `pieces[].item` is the equipment id from the demand tables. `stock[]` lists the cargo that isn't equipment: `item` the game's name, `key` the item key.
- `queue` is null while closed. `length` is Core's customers at the window and in line; `places` are the spots the visible line uses (empty while it isn't shown), `occupied` for the first `length` of them.
- `commissary` is null unless the vehicle has a space or stands in a bay; `site` is its space, `in_bay` whether it stands legally in any bay, `bay_site` which one.
- `route.next` is a line of text, "stop N, HH:MM to HH:MM at x …, z …", for the first stop still to close today.
- `commissary.orders[]` adds `item` and `units`; `total` is goods plus fee. `commissary.stock[]` adds `key`. `unpaid` sums the rent and fines the game refused.
- `commissary.bays` has every commissary: `resolved` with the bay's first point and the building's entrance nearest it, `failed` when the search found no legal parking, `pending` until the city's parking lanes let it search. `shoot`'s `door` pose and `teleport`'s `commissary:` use that entrance.
- `transactions_today` is the game's transactions of today and yesterday, oldest first; `label` is the transaction type resolved with its data, as close to the bank app's text as the bridge can read it.
- `employees` are the hires the crew dropdown offers (hired, without a building or vehicle), best customer service first.
- `load` is null before the first `load` call.
- `ledger_today.street_food` is today's street food revenue in the mod's ledger; `crew_waiting` is the crew's takings not yet paid in.

## Scenarios

A scenario is a JSON file in `tests/game/scenarios/`. `tools/scenario run <file>` runs its steps in order and stops at the first failed check unless the step says `"soft": true`. Reports go to `build/scenarios/<name>-<time>/` (`report.md`, `report.json`, `events.jsonl`, and the shots fetched from the PC).

```json
{
  "name": "commissary-pay-on-delivery",
  "checks": "CHECKS.md, Commissary look and payment (0.9.10), rows 1 to 3",
  "needs": { "scene": "city", "save_prefix": "FT" },
  "steps": [
    { "call": "debug", "args": { "action": "spawn truck" } },
    { "call": "money", "args": { "set": 20000 } },
    { "remember": "money", "from": "state", "path": "money" },
    { "wait_event": "Commissary order \\d+: 2 boxes", "timeout_s": 30 },
    { "expect": "state", "path": "money", "equals": "$money" },
    { "call": "time", "args": { "run_until": { "day": "+1", "time": "08:05" }, "speed": 20 } },
    { "wait_state": "clock.time", "equals": "08:05", "timeout_s": 300 },
    { "expect": "state", "path": "commissary.orders", "length": 0 },
    { "shoot": { "name": "bay-day", "pose": "bay-pavement", "target": "bay:hells-kitchen" } },
    { "note": "Rows 6 to 9 need eyes: see the contact sheet." }
  ]
}
```

Step kinds: `call` (with optional `expect_ok: false`), `remember` (a value into `$name`), `expect` (`equals`, `not_equals`, `less`, `greater`, `length`, `contains`, `matches` on a dotted path of `state` or of the last call's result), `wait_event` (a regex over new events), `wait_state` (poll `state` until a path meets the condition), `no_event` (a regex that must not appear between two steps), `shoot`, `sleep_s` (real seconds), `note`. Paths are dotted with list indexes (`vehicles.0.stock.1.amount`) and filters (`vehicles[kind=truck].legal`).

## Log checks

`tests/game/logchecks/<version>.txt` holds a build's expected log lines, one per line:

```
# id | rule | regex
patch.runDaily | must | Patch BusinessHelper\.RunDaily: Active\.
crew.paidIn | must | Crew sales paid in: .+, day \d+: \d+ items
no.notActive | never | Patch .+: Not active
```

Rules: `must` (at least once), `never`, `count=N`, `count>=N`, `order` (the regexes listed on one line separated by ` >> ` appear in that order). `tools/check-log <Player.log> [<version>]` also always reports every `Patch …: Active.` and `Not active` line, every mod error line and every exception that names a FoodTrucks type, and exits non-zero on any failed `must`, `never` or `count` rule. Without a version it takes the version from the log's `Version … loading.` line.

## Using the Mac tools

Make FT saves by hand from a new character of their own, never a series city saved under a new name. `new_game` is unavailable. The bridge refuses changing commands or loading a target whose character also has a non-FT save. Run scenarios from the menu or a throwaway FT city: loading from an FT city discards its unsaved changes; loading from a non-FT city is refused. Start the game with `-foodtrucks-test`. Open the tunnel, then check health in another terminal:

```sh
export BA_GAME_HOST=homepc
ssh -N -L 8322:127.0.0.1:8322 "$BA_GAME_HOST"
# In another terminal:
tools/ft health
tools/ft call load name='FT bridge'
tools/ft state load
# Wait for load.status done; failed includes error.
tools/ft state
```

When available, `tools/run-remote.sh start` and `tools/run-remote.sh tunnel` handle launch and the tunnel. The client uses `BA_BRIDGE_URL`, default `http://127.0.0.1:8322`. `BA_GAME_HOST` is used only to fetch screenshots.

```sh
tools/ft state 'vehicles[kind=truck].id'
tools/ft call time pause=true
tools/ft call time run_until.day=+1 run_until.time=07:59 speed=20
tools/ft call money --json '{"set":20000}'
tools/ft --json call state
tools/ft call commissary --json '{"op":"order","site":"hells-kitchen","items":[{"item":"ba:itemname_hotdog","boxes":2}]}'
tools/ft call crew op=assign vehicle=TRUCK_ID employee_id=EMPLOYEE_ID
tools/ft op 17
tools/ft events --since 1 --follow --grep 'Commissary|Crew'
tools/ft ui
tools/ft shoot bay-day --pose bay-pavement --target bay:hells-kitchen --fetch build/shots
```

Values in `key=value` are JSON when possible, otherwise strings. Dots build nested arguments. `--json` before `call` prints the raw reply; after `call` it supplies arguments. `call --raw` also prints the raw reply. `wait-event` watches new events. Event cursors are inclusive: request `since` and later, then use the reply's `next` unchanged.

`open`, `exit`, `restock` and `rent_bay` require driving the truck or pushing the cart. Teleport the player beside it, unpause with `time pause=false`, call `vehicle enter`, and wait for `state.player.in_vehicle` to match its id. Keep time unpaused through entry and hatch walks and their completion waits. Unpause before `open` or `exit`. Check each vehicle reply's `done`. After `open` or `back_to_hatch`, wait for `player.activity: "at_hatch"` and the matching `player.at_hatch` for a truck. Enter replies can report done when the walk starts; wait for activity driving/pushing and the matching in_vehicle. Close an open vehicle before teleporting it. Panel refusals for `restock` and `rent_bay` return `ok: true, done: false` with any notices. Calling a panel operation while not driving/pushing remains a failed call.

Speed is above 0 and at most 60. The bridge handles `run_until.day: "+N"`, then pauses and restores speed 1. It stops exactly on the target minute, including hour events for an HH:00 target. `clock.run_until` shows the pending target and `arrived_at` records completion until the next time call. Screens and activities can hold the clock; inspect `player.clock_held_by` before unpausing. UI click and escape work only in a loaded FT city with its own character. Clicks on the game's save/load/delete controls refuse; use the bridge's save and load commands. A changed label makes an id click refuse and asks for a fresh list. Escape closes the top screen or list picker; it refuses when nothing is open. List-picker options need their confirm button too. Duplicate text needs an id from the latest `ui list`.

```sh
tools/scenario list
tools/scenario validate tests/game/scenarios/commissary-pay-on-delivery.json
tools/scenario run tests/game/scenarios/commissary-pay-on-delivery.json
```

Each scenario's first note says how a person prepares its named FT save. Read it before running. The scenario loads it and waits for `load.status` to leave pending, requires done with no error, then checks the city, save and mod data. The order fixtures have no confirmed orders; the scenario creates them. The crew fixture has a route but no crew; the scenario hires and assigns by employee id. The art fixture holds equipment and vanilla references. Phone quantity inputs, map picks and reference placement remain manual notes and do not count as automated passes. The phone counterpart uses the labels from `unity/FoodTrucks/Locales/en.json`; vanilla picker labels still need a live UI list. The mock includes these prepared saves.

Reports land in `build/scenarios/<name>-<time>/`: failures first in `report.md`, plus `report.json`, `events.jsonl`, fetched PNGs and an offline contact sheet `index.html`. Use `--report-dir DIR` or `--keep-going` as needed. Mock pictures cannot establish an art check. Payment checks use `transactions_today` (today and yesterday), signed amounts, dates and counts independently of other expenses. Truck legal uses the wheel test. Visual presentation, wages and day-summary details still need review. Bay shots poll `commissary.bays[site=...].status` for resolved.

`expect` and `remember` read `state` or `result` (the last call's result). For payment checks, `expect.where` filters a list by field equality and optional `pick` reads a path in that filtered list. `remember.scale` multiplies a numeric value, rounded to cents. `wait_event.captures` maps remembered names to named regex groups, parsing numbers (commas removed). Remembered `$name` values are substituted exactly; `vehicle:$name` builds a player teleport target. Relative days go unchanged to the bridge. Event waits consume matches in order and, by default, accept only events from the previous step's start onward. `wait_event.after_step: N` instead starts after completed step N (0 is the run's start), while still excluding previously matched events. To check a command followed by several state waits, set after_step to the step just before that command. Missing wait_state paths count as not ready until timeout; an expect on a missing path still fails immediately. `no_event` accepts `from_step` and `to_step` boundaries; step 0 is the run's start. Without them it checks the previous step through this one. A `wait_event` boundary is just after its match, so duplicate payments in one batch are detected. Lost events fail the run.

```sh
tools/check-log build/logs/game/<session>/Player.log 0.9.10 --rows 0.9.10/1,0.9.10/2
python3 -m unittest discover -s tools/tests
tools/mock-bridge --port 8322
```

Row rules count only for selected `--rows`; `--rows all` checks every row. Rules without rows always count. Every `[Mod:FoodTrucks] ERROR: ` line fails the always-on error check; older logs keep the word and stack heuristics. Unknown versions get only the always-on checks. Conditional diagnostics use `count>=0` to report observed counts without requiring a fallback or retry.

The mock mirrors the built command guards, errors, result shapes, null fields, asynchronous entry and inclusive events. It simulates the economy and writes valid PNG placeholders; it cannot model Unity navigation or rendering. Its normal clock is one game minute per real second; `--clock-rate 100` emulates a faster Options setting for tests without changing command speed semantics. Tests use a free loopback port and never connect to the game PC. In environments that forbid listening sockets, `FT_TEST_TRANSPORT=inprocess python3 -m unittest discover -s tools/tests` exercises the same HTTP handlers and JSON replies in memory, with MCP tested over stdio pipes. This does not verify socket transport.

Every normal command reply carries an `op` id, also shown by readable `ft call` output and in MCP results/errors. `ft op N` and MCP `ft_op` query it without creating another operation. `timeout_s` is clamped to 1..600 seconds by both client and bridge. After a main-thread timeout the client polls that id for up to 30 seconds and returns the original result or failure. It never repeats the command. If it stays pending, query the id later. A transport failure before an id arrives has an unknown outcome; inspect the game log before repeating a mutation. The bridge keeps 500 operations. Invalid request bodies and op queries have no new op id.

`.mcp.json` registers the stdio server as `foodtrucks-game`. It uses the same environment variables and fetches screenshots into `build/shots/`.
