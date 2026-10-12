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
{ "ok": true, "mod_version": "0.9.10", "bridge_version": 1, "scene": "menu|loading|city", "save": "FT bridge", "day": 3, "time": "14:05", "paused": false, "frame": 123456 }
```

### `POST /call`

```json
{ "cmd": "state", "args": { } }
```

Runs on the Unity main thread. The HTTP thread waits up to `timeout_s` (default 10, an `args` field) for the result; past that the reply is `ok: false, error: "timed out on the main thread"` and the command still finishes in the game. Reply: `{ "ok": true, "result": { … } }`.

### `GET /events?since=N`

The bridge keeps the last 2,000 events in a ring buffer: every line the mod logs (`Log.Info`, `Log.Error`), every notice and phone text, and Unity errors and exceptions from any source. Reply:

```json
{ "ok": true, "next": 812, "dropped": 0, "events": [ { "seq": 811, "real": "2026-10-12T14:03:11.204Z", "day": 3, "time": "14:05", "kind": "log|error|notice|phone|unity_error", "text": "…" } ] }
```

`next` is the `since` to pass next time. `dropped` counts events that fell out of the buffer between `since` and the oldest kept.

## Commands

Positions are world metres (`x`, `y`, `z`); `heading` is degrees clockwise from +z, as the mod's log lines give them. Vehicle `id` is the game's vehicle id string, as the mod's log lines print it.

| `cmd` | `args` | Does | `result` |
|---|---|---|---|
| `state` | `{}` | Reads everything below. Changes nothing | see "State" |
| `console` | `{ "line": "IRSForcePayment 1" }` | Runs a line in the game's debug console (`DebugLogConsole.ExecuteCommand`) | `{ "output": ["lines the console logged during the call"] }` |
| `debug` | `{ "action": "spawn truck" }` | Runs one of the mod's debug actions by its `Name` in `DebugActions` (the twice-confirmed ones run without the second press) | `{ "action": "…", "notices": ["…"] }` |
| `time` | `{ "pause": true }`, `{ "speed": 4 }`, `{ "run_until": { "day": 4, "time": "04:05" }, "speed": 20 }` | Pauses, sets the game's speed, or runs the clock at `speed` until the time comes, then pauses. Time passes the normal way, so daily processing, the 04:00 check and midnight all run | `{ "day", "time", "paused", "speed" }` at once; poll `state` or `/health` to see `run_until` finish |
| `money` | `{ "set": 50000 }` | Sets the player's money (a plain balance change, no transaction) | `{ "money" }` |
| `teleport` | `{ "who": "player" \| "<vehicle id>", "x", "z", "heading"? }` or `{ "who", "place": "bay:<site>" \| "commissary:<site>" \| "vehicle:<id>" }` | Moves the player (on foot, out of any vehicle) or a mod vehicle. `y` comes from the ground. A vehicle lands on the road at that point; `bay:` puts a truck legally in the bay facing along the paint, `commissary:` puts the player at the garage door, `vehicle:` puts the player on the pavement beside the vehicle's hatch side | `{ "x", "y", "z", "heading" }` |
| `vehicle` | `{ "id", "op": "open" \| "close" \| "step_out" \| "back_to_hatch" \| "enter" \| "exit" \| "restock" \| "rent_bay" }` | The same code the vehicle panel's button runs, with its checks and messages | `{ "notices": ["…"], "state": { the vehicle's entry from "state" } }` |
| `ui` | `{ "op": "list" }` | Lists the visible, interactable buttons and dropdown options, and the visible text blocks of open dialogs and the phone | `{ "buttons": [ { "id": 3, "text": "OPEN HERE", "path": "…" } ], "texts": ["…"] }` |
| `ui` | `{ "op": "click", "text": "OPEN HERE" }` or `{ "op": "click", "id": 3 }` | Invokes the button's `onClick` (text match is case-insensitive and must be unique) | `{ "clicked": "…" }` |
| `ui` | `{ "op": "escape" }` | What Esc does: closes the top dialog, list or the phone | `{ "closed": "…" }` |
| `shoot` | `{ "name": "bay-day", "pose": "<pose>", "target": "<vehicle id>" \| "bay:<site>" \| "commissary:<site>", "width": 1600, "height": 900 }` or `{ "name", "x", "y", "z", "yaw", "pitch", "fov" }` | Renders one frame from a camera of the bridge's own (the game's camera is untouched, no UI in the picture) and writes a PNG | `{ "path": "C:\\…\\FoodTrucksTest\\shots\\bay-day.png" }` |
| `save` | `{ "name": "FT bridge" }` | Saves the city under that name | `{ "name" }` |
| `load` | `{ "name": "FT bridge" }` | Loads a save from the main menu or from the city | `{ "loading": true }`; poll `/health` for `scene: "city"` |
| `new_game` | `{ "name": "FT bridge" }` | Starts a new game from the main menu with defaults (story mode off if the game allows, Normal), skipping intros and pop-ups the bridge can close | `{ "loading": true }` |
| `quit` | `{}` | Quits the game | `{}` |

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
  "clock": { "day": 3, "weekday": "Wednesday", "time": "14:05", "paused": false, "speed": 1 },
  "money": 8164.0,
  "player": { "x": 0, "y": 0, "z": 0, "heading": 0, "in_vehicle": null, "at_hatch": null, "carrying": null },
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
    "spaces": [ { "site": "hells-kitchen", "vehicle": "…" } ],
    "orders": [ { "n": 3, "site": "…", "items": "2 boxes …", "total": 1544.84, "due_day": 4 } ],
    "stock": [ { "site": "…", "item": "Hotdog", "amount": 500 } ],
    "unpaid": { "rent": 0, "fines": 0 }
  },
  "ledger_today": { "street_food": 0.0, "crew_waiting": 0.0 },
  "notices_recent": ["the last 20 notice texts, newest last"]
}
```

Fields the game side can't fill yet are `null`, never left out, so scenarios can test for them.

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
