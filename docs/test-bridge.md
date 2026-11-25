# Test bridge

The test bridge lets an agent read and drive a running game: an HTTP server inside the mod, a CLI and MCP client, a mock, a scenario runner, a playthrough runner and a log checker. This page describes the code at HEAD. It is not evidence that anything works in game; dated in-game results live in [CHECKS.md](../CHECKS.md).

| Piece | Where | What it does |
|---|---|---|
| Bridge | `unity/FoodTrucks/Scripts/TestBridge*.cs` | HTTP on `127.0.0.1:8330` inside the game, only with `-foodtrucks-test` |
| Notice log | `Notices.cs` | Every mod notice is a `Notice:` line and every phone text a `Phone:` line in `Player.log` |
| Lease store | `tools/bridge-locks/`, `tools/ftlib/locks.py`, `tools/bridge-lock` | Per-instance ownership, shared by the game, the clients and the lifecycle scripts |
| `tools/ft` | CLI | Health, state, calls, op queries, events, shots |
| `tools/ft-mcp` | MCP over stdio | The same calls as tools, registered in `.mcp.json` as `foodtrucks-game` |
| `tools/mock-bridge` | Mac | A fake bridge with the same guards and reply shapes |
| `tools/scenario` | Mac | Runs a scenario file and writes a report |
| `tools/play`, `tools/playthrough` | Mac | The blind tester's controls and the playthrough runner and referee |
| `tools/check-log` | Mac | Checks a `Player.log` against a version's rules |
| `tools/contact-sheet` | Mac | One HTML page of a run's screenshots |
| `tools/run-remote.sh`, `install-remote.sh`, `park-remote.sh` | Mac to the Windows PC | Start, stop, tunnel, install and park the normal game |

## Operator rules

- The bridge, the debug hotkeys and the mod's developer console commands exist only when the game starts with `-foodtrucks-test`. Without it the log says `Test bridge: off (the game was started without -foodtrucks-test).` and only the player's safe uninstall step (`Ctrl+Shift+F11`, `foodtrucks_uninstall`) remains.
- The server binds `127.0.0.1` only. Other machines reach it through an SSH tunnel.
- `POST /call` needs `Content-Type: application/json` (else 415) and refuses any request with an `Origin` or `Sec-Fetch-Site` header (403), so a browser page cannot drive it. Use the CLI or MCP client.
- Every changing command needs the instance's lease token and the current session (see [Rigs and leases](#rigs-and-leases) and [Transport](#transport)).
- Commands that change a save work only in a loaded city whose save name starts with `FT`. They also refuse when that save's character folder holds any save not starting with `FT` (the game's `Recover #N` and `Recover Midnight` aside): `This save's character also has saves not starting with FT (<name>); make FT saves with a character of their own.` Make FT saves with `new_game` or a fresh character, never by saving a personal city under a new name.
- `save`, `load` and `new_game` take only names starting with `FT`. `load` from a city needs that city to be FT too, because leaving skips the unsaved-changes question.
- The tools are coordination between trusted local processes, not an OS security boundary. Never commit tokens, saves or game files.

### Developer actions

With the flag, `Ctrl` hotkeys and `foodtrucks_*` console commands run the mod's debug actions. The bridge's `debug` command runs the same actions by name, without the second press. "Twice" actions need a second press within 10 seconds.

| Name (for `debug`) | Keys | Does |
|---|---|---|
| `spawn truck`, `spawn cart` | K, J | A free truck or cart beside the player |
| `spawn hand truck` | Alt+J | A vanilla hand truck |
| `licence` | Alt+K | A free vendor licence and permits for every food vehicle |
| `new items` | U | One box of each new food and machine into the vehicle here |
| `hire` | Alt+U | A customer service worker, skill 60, unassigned |
| `status`, `demand here` | L, Alt+L | Show the mod's save data, or the demand reading for the cart here |
| `grip`, `zone point` | G, Alt+G | Log hand and grip points, or the player's position |
| `event here`, `gig here` | V, Alt+V, twice | A city event or a booked catering gig at the nearest place |
| `tax statement`, `tax fixture` | X, Alt+X, twice | Issue this year's statement now, or record $200,000 of street food today |
| `commissary bays`, `commissary deliveries`, `overnight check` | F3, Alt+F3, Shift+F3 | Log bay positions, deliver paid orders now, dry-run the 04:00 check |
| `uninstall` | Shift+F11 | The safe uninstall step, also without the flag |

## Rigs and leases

| Instance | Bridge URL | Where | Started with |
|---|---|---|---|
| `normal` | `http://127.0.0.1:8330` through a tunnel | Peter's Windows desktop session | `tools/run-remote.sh start` |
| `agent1` | `http://127.0.0.1:18330` through a tunnel | Isolated Windows test rig | `tools/agent-tests/agent` in the Driven Rides checkout |
| `agentmac` | `http://127.0.0.1:28330`, no tunnel | Isolated local Mac rig | `tools/agent-tests/mac-agent` in the Driven Rides checkout |

The game always listens on 8330 inside its own machine; the clients pick the instance from the URL's port (`tools/ftlib/locks.py`; any other port uses `BA_BRIDGE_INSTANCE`). In game, the instance comes from the profile folder. Prefer a rig over the normal game for anything disposable.

### Leases

Each instance has one lease. Every changing bridge command, UI click, console or debug command, load, save, quit and screenshot asserts it, and so do launch, stop, install and park scripts while the game is offline. Reads stay open: `/health`, `/events`, and `state`, `op`, `parks`, `places`, `map`, `day_summary`, `ui list`, `ui tree` and `vendor_fault status`.

- Pick a unique owner per agent session and keep it: `export BA_LOCK_OWNER="my-project-my-session"`. Acquiring creates a random token, kept with mode 0600 under `~/.big-ambitions-bridge/client-tokens/` (`BA_LOCK_TOKEN_DIR` overrides; `BA_LOCK_TOKEN` supplies one directly). The store keeps only its SHA-256, owner, purpose and pending work under `~/.big-ambitions-bridge/<instance>/` on the game's machine. A matching owner name without the token cannot take over.
- Leases never expire. Release explicitly when done, with the city paused. Release is refused while commands, loads, drives, `run_until` or other background work still hold a permit; a crashed process's permits are pruned by PID and start time, but its lease stays recoverable with its token.
- Never delete lock files to get past an unknown owner; arrange a handoff. An old `game-lock.txt` reservation blocks the normal game until its holder releases it.
- Acquire is idempotent for the same token and owner. `/health` reports `lock_protocol: 1`, `control_process_pid` and a public `control_lock` (holder, purpose, pending count, `expires_automatically: false`), never the token.

Lease commands: `python3 tools/bridge-lock setup|status|acquire|release|assert [--instance normal|agent1] [--purpose TEXT]` over SSH to the PC; online, `tools/ft call lock op=acquire owner=NAME purpose=TEXT` (also `op=status`, `op=release`); MCP `ft_lock`. Raw HTTP clients send `lock_token` (32 to 256 characters) at the top level of the `/call` body, never inside `args`.

### Normal game on the Windows PC

```sh
export BA_GAME_HOST=homepc BA_LOCK_OWNER="my-project-my-session"
python3 tools/bridge-lock setup
python3 tools/bridge-lock acquire --instance normal --purpose "Disposable FT test"
tools/run-remote.sh start          # launches with -foodtrucks-test, opens the tunnel, waits for /health
tools/ft call time pause=true
tools/run-remote.sh stop           # quits a test session only; stop --force ends the owner's wedged process
python3 tools/bridge-lock release --instance normal
```

`run-remote.sh` also has `status`, `tunnel`, `tunnel-stop` and `wait`. `tools/install-remote.sh` stages `build/FoodTrucks` with its SHA-256 manifest; `tools/park-remote.sh park|restore` moves the installed mod out of `ModsLocal` and back for uninstall tests. All need the game closed and the lease.

### Windows agent rig

Use `agent1` for disposable tests when the PC is on. Its launcher, tunnel and lease commands (`agent lock acquire|status|release`) are in the Driven Rides checkout; never start it with the normal-game launcher. Point clients at it with `BA_BRIDGE_URL=http://127.0.0.1:18330`.

### Mac fallback rig

When the Windows PC is unavailable, use the manually launched `agentmac` rig from the sibling Driven Rides checkout. Read `../hart-self-driving/tools/agent-tests/MAC.md` (repository `PeterHartwieg/hart-self-driving`), resolved next to the main checkout, not next to a worktree. It never contacts the PC and needs no tunnel or schedule.

```sh
export BA_LOCK_OWNER="my-project-my-session" BA_BRIDGE_URL=http://127.0.0.1:28330
python3 tools/agent-tests/mac-agent status                 # in the Driven Rides checkout
python3 tools/agent-tests/mac-agent lock acquire --purpose "Local fallback test"
python3 tools/agent-tests/mac-agent start                  # headless; --mode background for screenshots
# Food Trucks tools/ft, tools/scenario and MCP now reach the rig with the same owner.
python3 tools/agent-tests/mac-agent stop
python3 tools/agent-tests/mac-agent lock release
```

The `agentmac` lease is separate from the Windows ones and lives under `~/.big-ambitions-bridge/agentmac`. Set the environment before starting the MCP server. The rig's mods are installed snapshots, not repository links: confirm its installed Food Trucks snapshot contains the code under test before claiming a result. If you swap in a test package, back up first, then stop, restore the original install, check its hashes and release. Headless mode has no graphics device, so screenshots need background mode and full-frame UI checks remain unverified there.

### Your own Mac game

With Big Ambitions closed, `python3 tools/install-local-mac.py [--game-root DIR]` verifies `build/FoodTrucks` against `build/FoodTrucks.sha256`, replaces only `ModsLocal/FoodTrucks` and keeps the previous package under `FoodTrucksPackageBackups`. Launch through Steam with the flag (quit Steam first so it accepts the arguments):

```sh
open -a /Applications/Steam.app --args -applaunch 1331550 -foodtrucks-test -console -screen-fullscreen 0 -screen-width 1440 -screen-height 900
tools/ft health
```

`python3 tools/package-release.py` packs a verified build into `build/FoodTrucks-<manifest version>-rc.zip`; it builds, uploads and publishes nothing.

## Transport

All bodies are UTF-8 JSON. Every reply has `ok`, and `session` beside it. A failed reply has `error`, one sentence a person can act on.

### Sessions

`session` is 8 random hex characters chosen at each bridge start. A new session means the game or mod restarted: operation ids, event numbers and remembered state from before are void. Changing commands must carry `"expect_session"` from `/health`; without it the request is refused (`Changing commands require expect_session from /health`). A stale session is refused before anything is numbered or queued: `the bridge restarted (session <new>); reconnect first.`

The client remembers the first session it sees and sends it on every call. On a change it blocks further calls locally. A running scenario fails at once, soft steps and `--keep-going` included. MCP `ft_health` then reports `restarted`, `reconnect_required` and the previous session; `ft_reconnect` pins the new session, discards old op ids and cursors, and resumes events from `since: 1`. The CLI starts fresh on each invocation, so it has no reconnect command; `ft events --follow` announces a restart and resumes from event 1.

### `GET /health`

Answered on the HTTP thread from a per-frame snapshot, so it works while loading: `mod_version`, `bridge_version` (1), `scene` (`menu`, `loading`, `city`), `save`, `day`, `time`, `paused`, `time_skip`, `frame`, `load` (`name`, `status`), plus the lease fields above.

### `POST /call` and operations

Body: `{ "cmd": "...", "args": { ... }, "expect_session": "...", "lock_token": "..." }`. Calls run one at a time on the Unity main thread; each request has its own HTTP thread, so health, events and op queries answer while a call waits. The HTTP thread waits `args.timeout_s` (default 10, clamped to 1 to 600 by client and bridge).

- A queued call gets an operation number: `{ "ok": true, "op": 17, "result": {...} }` or `{ "ok": false, "op": 17, "error": "..." }`.
- Past `timeout_s` the reply is `ok: false` with the op and `timed out on the main thread; the command goes on: ...`. The work still finishes in game.
- `{ "cmd": "op", "args": { "id": 17 } }` answers at once with `{ id, cmd, session, status: pending|done|failed, result?, error? }` and gets no op number of its own. The bridge keeps every pending operation and the last 500 completed ones.
- Some replies have no op id: op queries, `lock` calls, malformed bodies (not a JSON object, no `cmd`, missing `expect_session`), lease refusals, unknown endpoints and a stopped bridge.
- After a main-thread timeout the client polls the original op for up to 30 seconds and returns its outcome. It never resubmits. If it is still pending, query it later with `tools/ft op N` or `ft_op`. Never resend a timed-out mutation.
- A transport failure before any reply arrives has an unknown outcome: inspect `state` and the log before repeating a changing command.

Long calls answer when their work ends, within `timeout_s`: `save` (file written), player `teleport` (out of a building or vehicle), `walk` (arrival or a stop, at most 600 s), `use` clicks and hand truck moves (walk and door, at most 120 s), `shoot` (its frames). On unload or mod disable the bridge stops taking calls, fails queued and pending ones with `the bridge stopped`, and clears operations, events, `run_until` and load status; event numbers restart at 1.

### `GET /events?since=N`

A ring of the last 2,000 events: mod log lines, errors (`ERROR: ` prefix, once), notices, phone texts and Unity errors from any source. `since` is inclusive; pass the reply's `next` unchanged next time. `dropped` counts events lost from the buffer; the scenario runner fails on any loss.

```json
{ "ok": true, "next": 812, "dropped": 0, "events": [ { "seq": 811, "real": "2026-10-12T14:03:11.204Z", "day": 3, "time": "14:05", "kind": "log|error|notice|phone|unity_error", "text": "..." } ] }
```

Stamps use the same clock as `/health`. A jump of more than 30 game minutes in one frame logs `Test bridge: the clock jumped from day D HH:MM to day D HH:MM in one go: <why>.` (a time skip, a hospital collapse, or something else setting the time).

## Clients

`tools/ft` uses `BA_BRIDGE_URL` (default `http://127.0.0.1:8330`), `BA_LOCK_OWNER` and, for fetching screenshots from the PC, `BA_GAME_HOST`.

```sh
tools/ft state 'vehicles[kind=truck].id'
tools/ft call time run_until.day=+1 run_until.time=07:59 speed=20
tools/ft call commissary --json '{"op":"order","site":"hells-kitchen","items":[{"item":"ba:itemname_hotdog","boxes":2}]}'
tools/ft op 17
tools/ft events --since 1 --follow --grep 'Commissary|Crew'
tools/ft shoot hk-door --pose door --target commissary:hells-kitchen --compare --game-view --fetch build/shots
```

`key=value` values are JSON when they parse, else strings; dots build nested objects. `--json OBJECT` or `--args OBJECT` after `call` supplies arguments; a bare `--json` or `--raw` prints the raw reply. Readable output includes the op id. `ft shoot` takes `--pose`/`--target`, or `--at X,Y,Z` with `--look-at X,Y,Z` or `--yaw`/`--pitch`, plus `--fov`, `--width`, `--height`, `--frames`, `--timeout` (default 20 s), `--compare`, `--game-view` and `--fetch DIR`.

MCP tools: `ft_lock`, `ft_health`, `ft_reconnect`, `ft_state`, `ft_call`, `ft_op`, `ft_events`, `ft_wait_event`, `ft_ui_list`, `ft_ui_click`, `ft_shoot` and `ft_check_log`. The server is one process per agent session and loads `tools/` once, so restart it after tool changes.

## Commands

Positions are world metres (`x`, `y`, `z`); `heading` is degrees clockwise from +z. A vehicle `id` is the game's vehicle id string. Guard: `read` needs no lease; `city` needs a loaded city; `FT` needs a loaded FT city with an FT-only character. Every non-`read` command also needs the lease.

| `cmd` | Guard | Arguments | Does and returns |
|---|---|---|---|
| `state` | read | `{}` | Everything in [State](#state) |
| `console` | FT | `{ "line": "IRSForcePayment 1" }` | Runs a debug-console line; `output` holds the lines it logged |
| `debug` | FT | `{ "action": "spawn truck" }` | Runs a [developer action](#developer-actions) by name; `notices` |
| `time` | FT | `{ "pause": true }`, `{ "speed": 4 }`, `{ "run_until": { "day": 4 \| "+1", "time": "04:05" }, "speed": 20, "keep_needs": true }` | Pause, set speed (above 0, at most 60; the game's minutes multiplier), or run until a minute and pause. See [Clock](#clock) |
| `money` | FT | `{ "set": 50000 }` | Sets the balance directly, no transaction |
| `teleport` | FT | `{ "who": "player" \| "<id>", "x", "z", "y"?, "heading"?, "clear"? }` or `{ "who", "place": "bay:<site>" \| "commissary:<site>" \| "vehicle:<id>" \| "address:<addr>", "clear"? }` | Moves the player (out of any vehicle) or a closed mod vehicle onto ground. See [Teleport](#teleport). Returns pose, `surface`, `in_the_way_after` |
| `walk` | FT | `{ "x", "z" }` or `{ "place": ... \| "exit" }` | The game's click-to-move, pushing any held cart. Refused while paused, in a vehicle, at a hatch or with no route. Returns `arrived`, `path` (`complete`/`partial`), `metres`, `left`, once within 1.5 m or still for 3 s |
| `drive` | city; FT for `start` | `{ "id", "place": "bay:<site>" \| "address:<addr>" \| "stop:<N>", "speed"?: 1..3 }`; `{ "op": "status" \| "check" }` | Simulated player trip in the truck the player sits in: time advances by distance x 1.35 / 8 game minutes (at least 2), then it parks. Trucks only; refused when open, indoors, paused, during `run_until` or a time skip. `check` keeps the old Self Driving diagnostics |
| `vehicle` | FT | `{ "id", "op": "open" \| "close" \| "step_out" \| "back_to_hatch" \| "enter" \| "exit" \| "restock" \| "rent_bay" }`; `transfer` adds `direction: take\|return`, `item`, `units`? | Runs the vehicle's own button with its checks. `done` says whether it happened, `why` names a refused choice. See [Vehicles](#vehicles) |
| `vehicle` | FT | `{ "op": "buy", "kind": "truck" \| "cart" }` | Debug spawn beside the player, charged at the type's price as a dealer purchase. Nothing is charged if nothing spawns |
| `vehicle` | FT | `{ "id", "op": "fast_forward", "enabled"? }`, `{ "id", "op": "skip_work", "minutes": 5..720 }`, `{ "id", "op": "stop_work" \| "work_window" }` | The serving panel's native controls. See [Serving work controls](#serving-work-controls) |
| `price` | FT | `{ "vehicle", "item", "price": "14.50" \| "auto" }` | The vehicle page's price field |
| `route` | FT | `{ "op": "add_stop", "vehicle", "address", "days"?: [..] \| "all", "open"?: "HH:MM", "close"?: "HH:MM" }` | The map pick by address: the spot `StopPicker.Snap` finds by the building's first entrance, added on each day (default all) at the given hours or the map pick's defaults (from 11:00 for up to 3 hours). Returns `address`, pose, `distance`, per-day `added`/`stops` |
| `route` | FT | `{ "op": "paste_day", "vehicle", "from": "monday", "to": [..] \| "all" }` | The Week plan tab's copy and paste of one day. Returns `done`, `problem` |
| `route` | FT | `{ "op": "edit_stop", "vehicle", "index": 0 }` or `{ "op": "edit_stop", "vehicle", "day": "monday", "stop": 0 \| "address": "ba:street_...#N" }`, with `"open"?`, `"close"?` | The Week plan tab's new hours for a stop already on the route (`RouteEditor.SetTimes`, then `RouteStops.Changed`). See [Route edit_stop](#route-edit_stop) |
| `route` | FT | `{ "op": "catalogue", "start"?: 0, "count"?: 1..8 }` | Read-only city export: native stopping-place checks for carts and trucks at each building entrance, then the prefab hull against permanent obstacles. Rows give `key`, `entrance`, `kind` and a pose or `refusal`; page with `next` until `total`. Does not promise vacancy |
| `commissary` | FT | `{ "op": "order", "site", "items": [ { "item", "boxes" } ] }` | The phone order's confirm: same quote, order and log lines, paid on delivery. The first order of a commissary's morning carries the flat `delivery_fee` |
| `commissary` | FT | `{ "op": "bay_candidates", "site" \| "address", "radius"?: 5..80 }` | Legal truck spots round a commissary's bay origins or any building's entrances, with `same_side`, `walk`, `automatic` and `rejected` per spot. Reads only. See [Bay candidates](#bay-candidates) |
| `crew` | FT | `{ "op": "assign" \| "unassign", "vehicle", "employee_id" }`, `{ "op": "schedule", "vehicle", "employee_id", "day", "start", "end", "station"?: "serving1" \| "serving2" }`, `{ "op": "auto_schedule", "vehicle" }` | The vehicle page's crew dropdown and Schedule tab. Whole hours, at most 12; `start == end` removes the day's shifts. Refusals read `Shift refused: NotCrew`, `BadHours`, `TooLong` or `Clash` |
| `fixture` | FT | `{ "name": "logistics", "site"?, "stock"?, "targets"?, "vehicle"? }` | Builds a logistics supply chain to a commissary in the loaded FT save. See [Logistics fixture](#logistics-fixture) |
| `logistics` | city; FT for `run` | `{ "op": "status" \| "run", "warehouse"? }` | Reads warehouse plans, or runs the game's 02:00 warehouse deliveries now and completes them inside the call |
| `parks` | read | `{}` | Park triggers with kind, neighbourhood, bounds, centre and the 5 nearest entrances within 150 m |
| `day_summary` | read | `{ "day"? }` | The mod's books for one day: per-vehicle ledger lines, units sold (today and yesterday only), stops and minutes, cargo now, fleet lines |
| `places` | read | `{ "radius"?: 20..500 }` | Up to 40 buildings round the player: address, name, type, business, commissary. No demand figures |
| `map` | read | `{ "op": "filters" }` or `{ "filter", "limit"? }` | The city map's highlights as data (`for_rent[:type]`, `business:<type>`, `for_sale`, `yours`, `offices`, `factories`, `nightlife`, `shows`, `pitches`, `commissaries`), nearest first, at most 100 |
| `use` | city for `list`; FT for `click` | `{ "op": "list", "radius"?: 3..80 }`, `{ "op": "click", "id" }` | A left click on a world thing. Buildings enter through native navigation and `Interact`. Refused while paused, in a taxi, a time skip, the subway, the designer or while placing |
| `taxi` | FT | `{ "address" }` | The taxi map's destination, with a taxi already waiting (clicked with `use`): fare, ride minutes and set-down |
| `phone` | FT | `{ "app": "contacts" \| "employees" \| "bizman" \| "econoview" \| "market" \| "persona" \| "rivals" }` | Opens the phone app; its screen is then `ui`'s |
| `call` | city for `list`; FT for a call | `{ "op": "list" }` or `{ "contact" }` | Lists contacts, or opens a conversation and presses its call button when the game shows it usable |
| `eat` | FT | `{ "item"? }` | Eats from the paper bag in hand, as the hands panel does |
| `floorplan` | FT | `{}` | The current floor as 0.5 m squares. See [Floor plan controls](#floor-plan-controls) |
| `place` | FT | `{ "square": "C4" }` or `{ "x", "z" }`, with `"rotation"?`, `"move"?: NAME`, `"check"?`; or `{ "on": NAME, "check"? }` | Places held paid furniture, moves furniture already there, or sets stackable furniture on another piece |
| `drop` | FT | `{ "x"?, "z"? }` (both or neither) | Puts down the held box or bag still packed, on an editable floor only; returns a mop to its station |
| `pickup` | FT | `{ "id" }` | Picks up a placed box or bag from the last `use list` |
| `handtruck` | city for `list`; FT otherwise | `{ "op": "list" }`, `{ "op": "grab" \| "release" \| "load", "id"? }`, `{ "op": "take", "id"?, "item" }` | Ordinary hand trucks and flatbeds through their native storage, Park and cargo buttons. Unpaused clock needed. Partial loads return `done: false`; inspect state before retrying a `still walking` reply |
| `nav_probe` | FT | `{ "radius"?: 6 }` | Walk-to points, wall tests, path status and gaps for items near the player |
| `ui` | see [UI](#ui) | `list`, `tree`, `click`, `set`, `type`, `escape` | Screen controls |
| `job` | FT | `{ "op": "auto" }` | With Real Life Overhaul, plays the running job as a competent player: minigame jobs through their time skip at a fixed share of base pay, courier and taxi shifts from RLO's own pay figures |
| `licence` | FT | `{ "class": "car" \| "commercial", "buy"? }` | With Real Life Overhaul, a driving licence's standing and price, or bought outright for the school path's cost |
| `help` | city | `{}` | The game's help pages and, with Real Life Overhaul, its guide pages |
| `shoot` | city | `{ "name", "pose", "target" }` or `{ "name", "x", "y", "z", "yaw", "pitch" \| "look_at" }`, with `width`, `height`, `fov`, `frames`, `compare`, `game_view` | Renders a PNG from the bridge's own camera. See [Screenshots](#screenshots) |
| `save` | FT | `{ "name" }` | Saves; answers only after serialization, compression and a fresh non-empty file. Returns `name`, `path`, `bytes` |
| `load` | any | `{ "name" }` | Loads the newest save of that name. Poll `load.status` |
| `new_game` | main menu | `{ "name", "preset"? }` | A new custom game with Normal values (or a saved custom preset), tutorial off, character `FT Playtest`, then paused and saved under the name |
| `quit` | any | `{}` | Replies, waits for any save being written, then quits |
| `vendor_fault` | read for `status`; FT otherwise | `{ "action": "arm", "point": "sell_after_ledger" }`, `{ "action": "status" \| "disarm" }` | Fault test builds only. See [Test builds](#test-builds) |
| `lock` | none | `{ "op": "status" \| "acquire" \| "release", "owner"?, "purpose"? }` | The lease, see [Leases](#leases) |
| `op` | read | `{ "id" }` | An earlier call's outcome |

`crew_drive_probe` exists only in developer builds; see [Crew driving probe](#crew-driving-probe).

### Clock

`run_until` runs at `speed` (default 1), stops on the exact target minute (a bridge-only Harmony prefix on `GameManager.RunMainGameTick` keeps each step within 0.01 minute of it), pauses and restores speed 1. `day` is a number or `"+N"`. `state.clock.run_until` shows the target and `arrived_at` the stop until the next `time` call. Anything that unpauses is refused while a screen or time skip holds the clock; read `player.clock_held_by` first. A refused `time` call changes nothing.

`keep_needs` defaults to true with `run_until` and false otherwise; pausing, arriving or leaving the city turns it off. While on and the clock runs, energy or hunger below 40 is topped up through the game's needs API and logged. Happiness is not topped up: the game rebuilds it hourly from its modifiers. A collapse logs `Test bridge: the player collapsed at day D HH:MM (...)`.

### Teleport

For `x`/`z` the bridge reads every Ground-layer surface stacked at that point and takes the one nearest an explicit `y` (within 1.5 m), else a walkable one nearest the mover's height. For the player, walkable means the player's NavMesh within 1 m of the surface and, within 100 m, a complete walking route; a player move with no walkable surface is refused. The reply's `surface` says which height was taken and why.

Before moving, the place is tested for anything standing there: the vehicle's collider footprint or a person's capsule, from 0.3 m above ground. Parked lane cars and the building's delivery-job van (shown or hidden) count for vehicles; static scenery does not count for the player. `"clear": true` returns lane parked cars to their pool; nothing else is cleared. After a vehicle moves, `in_the_way_after` lists anything now overlapping and its parking state and district are read again. `bay:<site>` picks a free place on the paint with the hatch to the pavement; `commissary:<site>` puts the player 1.5 m out from the bay-side entrance; `vehicle:<id>` beside the hatch or queue side; `address:` 1.5 m out from the first entrance. Teleporting the player cancels pending walks.

### Vehicles

`exit`, `restock` and `transfer` are buttons on the panel shown while the player drives the truck or pushes the cart; enter first. `rent_bay` needs only the vehicle in a commissary's bay. `exit` uses the game's Park button, which does nothing while paused. `open` needs the player on foot beside a parked, closed vehicle and works while paused; a truck then takes the player to its hatch. `enter` and `back_to_hatch` reply when the walk starts: wait for `player.activity` (`driving`, `pushing`, `at_hatch`) and the matching `in_vehicle` or `at_hatch`. Close a vehicle before teleporting it.

A mod refusal is `ok: true, done: false` with its notice. `notices` covers the call plus 5 frames and 0.25 s; per-minute notices (for example the empty-window warning after `step_out`) arrive later through `/events`, so wait for them with `wait_event`.

### Route edit_stop

Name the stop one way: `index` (0-based into the week's stops, Monday first, as `state`'s `route.week` lists them), or `day` with `stop` (0-based among that day's stops) or `address` (the one stop that day at the spot `add_stop` picks for that building, `StopPicker.Snap`, within 1 m, `StopEdit.SameSpotMetres`). Mixed identifiers, a missing stop and an address with two stops that day are refused.

Give `open`, `close` or both; a missing one keeps the stop's own. A `close` at or before the open (given or kept) is after midnight, as for `add_stop`: `"02:30"` after an open of 20:00 is 26:30, so any close up to 24 hours after the open can be written with hours 0 to 23 (the route's latest close is `LatestCloseMinute`, 27:00). A refusal from Core (`Overlap`, `BadTimes`, `Unreachable`, ...) changes nothing and comes back in `problem`.

The edit is fitted to travel times first (`RouteStops.Changed`), and yesterday's crew plan is dropped too when the stop is yesterday's late one. When the stop is the one the crew has open and its close after fitting has already passed, the cart closes there and then (`CartSelling.CloseByCrew`, as removing that stop does). A later `load` of a save from before the edit has the old hours. Same write gates as `add_stop`.

Reply: `{ "done", "problem", "vehicle", "index", "day", "open", "close", "notices", "state" }`, with `index` the stop's place in the week after the edit (new hours can move it within its day), and `open`/`close` the stored hours, `HH:MM` with hours past 23 for a close after midnight (`26:30`).

### Bay candidates

Give exactly one of `site` or `address` (`ba:street_<name>#<number>`). A spot is legal by the automatic search's probe (`BaySnap`: the game's wheel test, lined up with and centred in the parking lane): up to 60 per origin at least 4 m apart, nearest first, plus the automatic pick, which the bridge computes with the real search (30 m, same side, short walk) and always lists. Per spot: `heading`, `distance`, `same_side` (the straight walk from the door crosses no driving lane), `walk` (`short` when a complete NavMesh path is at most 1.6 times the straight line plus 15 m; `too_long`, `no_route`, `unknown`), `walk_m`, `straight_m`, `automatic` and `rejected` (why the search would pass it over). To fix a bay by hand, put a spot's `x`/`z` into `data/commissaries.json`; a measured bay is used only on its door's side of the street.

### Logistics fixture

Makes the loaded FT save deliver to a commissary through a logistics plan: a commissary space for the first food vehicle without one, a warehouse (`FT Fixture Warehouse`, rent-free like the console's `RentBuilding`), pallets, stock (default 200 hot dogs), a headquarters (`FT Fixture HQ`) with a logistics manager, a delivery vehicle and driver, and the plan with targets (default 40) at Hell's Kitchen by default. Steps run in order, each reuses what an earlier run made, and the first failure stops the rest (`not_run`); `complete` is true only when every step is `done` or `already`. It refuses inside a building and while commissary logistics patches are off.

Limits: the manager has no work shift, so BizMan's manager dropdown reads Unassigned and editing the headquarters' schedule unassigns them (the 02:00 run does not need a shift). No course check, rent is 0, and pallets placed beside the delivery spot are not checked against walls.

### Screenshots

`shoot` copies the game camera's settings into its own camera, renders `frames` (8 to 240, default 8) into a texture and writes a PNG; the player's view is untouched. For each frame it lifts the game's view-blocking fade, hover and map-filter outlines and the BuildingHider globals at renderer level, and restores each exactly in the same frame. `uncovered` and `outlined` list what was lifted; `compare: true` also writes `<name>-asis.png`, and `game_view: true` writes the game's own frame with UI as `<name>-game.png`. `state.view_blocked` and `state.game_camera` show what the game fades now and where its camera stands. A long render can outlast `timeout_s`; collect it through its op id.

Named poses never start inside geometry: a sphere sweep from the subject pulls the camera in to 0.4 m in front of the first solid, alternative ways are tried in order, and `pose_note` says what changed.

| Pose | Camera |
|---|---|
| `hatch-pavement`, `hatch-close` | 3 m and 1.5 m out from the truck's hatch at eye height (1.65 m) |
| `vehicle-side` | 6 m out from the hatch side, 1.8 m up, the whole vehicle in frame |
| `vehicle-overhead` | 12 m up and 8 m back, looking down 45 degrees |
| `queue` | 8 m along the pavement from the hatch's front end, 1.8 m up |
| `cart-counter` | 1.5 m from the cart's queue side, 1.9 m up |
| `bay-pavement`, `bay-road` | From the pavement across from the bay, or from the driving lane along it; nose-in lot bays use the aisle behind the stall |
| `door` | 6 m out along the walk from the commissary entrance |

### Save, load and new game

`load` from the main menu runs the Load screen's button without its two mod questions; from an FT city it goes to the menu first. It waits until no loading screen is left before starting. Progress is `state.load` and `/health`'s `load`: `pending`, then `done` once the city is up and the mod has attached since the call, or `failed` with `error` (broken save, a load that died with a second loading screen, or nothing within 300 s). A game loading flag left on with no screen is never cleared by the bridge; restart the game.

`new_game` refuses an existing save name, a name the game would alter and a load already under way. It runs the game's steps (custom game, Normal values, tutorial off, character `FT Playtest` with a random look), renames the save, pauses and saves. `state.load.new_game` names the step; it fails after 600 s. Each new game is a character of its own, so its saves pass the FT character check.

## State

```json
{
  "save": "FT bridge", "scene": "city",
  "clock": { "day": 3, "weekday": "Wednesday", "time": "14:05", "paused": false, "speed": 1, "run_until": null, "arrived_at": null, "keep_needs": false },
  "load": { "name": "FT bridge", "status": "done", "error": null, "new_game": null },
  "money": 8164.0,
  "player": { "x": 0, "y": 0, "z": 0, "heading": 0, "in_vehicle": null, "at_hatch": null, "carrying": null, "activity": "on_foot",
              "clock_held_by": null, "needs": { "energy": 82.5, "hunger": 74.0, "happiness": 80.0, "happiness_modifiers": [], "warnings": [] },
              "hands": null, "handtruck": null, "work": null, "inside": null, "homes": [], "riding": false },
  "job": null,
  "licence": { "until_day": 60, "permits": 2, "course_done": true, "todos": [] },
  "vehicles": [ { "id": "...", "kind": "truck", "name": "Food truck", "x": 0, "y": 0, "z": 0, "heading": 0,
                  "legal": true, "legal_reason": "wheel parking test: ...", "hatch_side": "right", "hatch_to_pavement": true,
                  "open": true, "serving": "crew", "crew": ["name"], "pieces": [ { "item": "hotdoggrill", "units": 62, "cap": 80 } ],
                  "stock": [ { "item": "Hotdog", "key": "ba:itemname_hotdog", "amount": 62, "set_price": null } ],
                  "queue": { "length": 4, "places": [] }, "commissary": { "site": "hells-kitchen", "in_bay": true, "bay_site": "hells-kitchen" },
                  "route": { "stops": 2, "next": "...", "problem": null, "week": [], "shifts": [] },
                  "parking": { "state": "Legal", "neighbourhood": "...", "hourly_price": 5.0, "unpaid": 0.0 }, "todos": [] } ],
  "other_vehicles": [ { "id": "...", "type": "...", "x": 0, "z": 0 } ],
  "commissary": { "bays": [], "spaces": [], "orders": [], "stock": [], "unpaid": { "rent": 0, "fines": 0 } },
  "ledger_today": { "street_food": 0.0, "sales_waiting": 0.0, "crew_waiting": 0.0 },
  "notices_recent": [], "transactions_today": [], "employees": [], "game_camera": null, "view_blocked": []
}
```

Fields the game side cannot fill are `null`, never missing. Readings worth knowing:

- `clock`, `money`, `player`, `licence`, `commissary` and `ledger_today` are null outside the city; mod fields are null while the mod's save data is not loaded.
- `player.activity`: `driving`, `pushing`, `starting`, `at_hatch`, `walking` or `on_foot`. `player.clock_held_by`: null, `time skip`, or the screens holding the clock (`phone`, `map`, `call`, `activity panel`, `pause menu`, `confirmation`, `warning`, `a screen`). `needs.warnings` uses the game's thresholds (`hungry`, `very_hungry`, `tired`, `exhausted`, `out_of_energy`).
- A truck the player drives reads `legal` from the vehicle panel; another truck from the game's wheel parking test; a cart from the mod's pavement rule. `legal_reason` says which.
- `serving` is `player`, `crew` or `nobody` (always `nobody` when closed). `queue` is null while closed.
- `route.week` lists each weekday's `closed` mark, `stops`, `route_day` (minutes from that day's midnight, past 1440 for late routes) and `gaps` with nobody on shift. `route.shifts` gives each crew member's weekly hours, wanted hours and shifts with `station`. `route.problem` is Core's route problem in snake case.
- `todos` are the phone to-dos as the last game minute's reconcile left them (`low_stock`, `empty_stock`, `missing_crew`, `route_problem`, `no_permit`, `no_commissary_space`, `health_grade_c`); a change made while paused shows after the clock moves.
- `commissary.bays[].status` is `resolved`, `failed` or `pending`; wait for `resolved` before bay shots. `orders[].total` is 0 until delivery sets the fee.
- `transactions_today` holds today's and yesterday's game transactions, oldest first, with `balance` to tell equal ones apart.
- `ledger_today.sales_waiting` is all player and crew takings not yet paid in; `crew_waiting` is a compatibility alias. Both pay at midnight, once per vehicle and day.
- `employees` are hires the crew dropdown offers, best customer service first.

## UI

`ui list` (read, any scene) lists visible interactable buttons, dropdown options (`option`), sliders (`value`, `min`, `max`, `whole_numbers`), toggles (`on`) and TextMeshPro text fields (`value`), plus the texts of open dialogs, the phone, overlays, the shop's purchase panel and the map's building card; at the main menu, every canvas. `ui tree { path, depth?, max? }` (read) dumps matching hierarchy nodes with components, colours, sprites and CanvasGroup alpha.

`click` (by `id` or a unique case-insensitive `text`), `set` (slider), `type` (text field; confirms nothing) and `escape` need a loaded FT city. Clicks on the game's save, load, delete and character controls and on pause-menu confirmations are refused; use the bridge's `save` and `load`. `escape` runs the game's Esc handler, closes a list picker with its Cancel, and refuses when nothing is open.

Discovery limits that still apply:

- Ids number the last list only. Refresh after any change; an id click refuses a control no longer on screen or whose label changed (`the control with id N now reads '<text>'; list again.`). Duplicate text needs an id.
- Visibility checks canvases, alpha and screen edges, not scroll masks or windows on top, so a clipped or covered control can still be listed.
- A list picker takes two clicks: the option, then the game's confirm button, whose generic label only a live `ui list` shows.
- Map picks and drags are not reachable through `ui`. Stops go in by address (`route add_stop`), furniture through `place`, `drop` and `pickup`.

## Floor plan controls

`floorplan` and `place` work inside a building the player rents or can edit, on the physical floor the player stands on, with no designer or active placement. They show geometry only: no layouts suggested, no prices, no furniture granted. `tools/play floorplan` prints one character per square: `@` player, `.` floor, `#` wall, `=` known exit, `X` blocked, blank void, letters and digits for items, `*` overlapping footprints, then one line per item with ID, covered squares, yaw and use squares.

Grid:

- Squares sit on the game's 0.5 m world lattice (`Item.gridSize` default; `PlacementSystem.UpdateGroundItemPosition` snaps with `floor(value / step + 0.5) * step`). Assets may override `gridSize`; the reply gives the native result.
- `origin` is the world centre of A1, aligned to the floor geometry's minimum world X/Z with one square of perimeter. Columns (A to Z, AA, ...) run toward +X; rows run toward +Z. For zero-based column `c` and row `r`: `x = origin.x + c * 0.5`, `z = origin.z + (r - 1) * 0.5`. C4 is `(origin.x + 1, origin.z + 1.5)`.
- The plan names building size and structure version, since a size alone does not fix the geometry. Equal structures at the same grid phase get equal labels.
- The item's anchor goes on the square, not a footprint corner. Coverage counts squares whose area intersects a projected collision footprint.

Geometry is sampled per square: floor rays against current-height colliders (holes and concave outlines kept), wall and `GroundUnplacable` overlap boxes, `InteriorElement.placementBlocked` surfaces and `ExitZone` doors. Narrow features between samples can be missed; non-box colliders use conservative bounds; flat decoration can overlap furniture. Use squares come from the item's cached `navMeshTargets` with the native wall test and path check; the survey does not refresh targets.

Placement:

- `place` resolves the square, then uses the game's own path: `ItemPanelUI.ClickPlace` for paid held furniture, or `PlacementHelper.StartPlacementMode` for `move NAME` (the nearest matching furniture on this floor), with the native ground update and collision validation. It refuses unpaid items, wall-mounted and ceiling items; stackable items need `on NAME`, which tries the named furniture's matching attachment points.
- Absolute rotation rounds to the item's `degreesPerRotation` (45 when unset). Grid snapping is set for the call and restored; refused while rotation input is active.
- `fits` is native geometric validity; `reachable` is separate and calls the native reach test after refreshing targets at the candidate. NavMesh obstacles may settle on later frames, so walking to the item is still a native check. `blocked_by` names the overlapping collider.
- The requested and snapped anchor must be on the same physical floor, so a long native ray cannot drop furniture to a lower level. A blocked surface can reject before the ghost moves (`position_resolved: false`).
- `check: true` evaluates the same candidate, then cancels and compares cargo (paid state, colours, contents), item IDs, poses, parents and attachments; `restored: false` is reported, never hidden. Checks hold the same save, session and STOP-file guards as real placements.
- Business and furniture requirements (attachment, height, entrance or business-type rules) are not evaluated by `place`; read the game's own warnings.

Pending native checks are listed in [CHECKS.md](../CHECKS.md).

## Serving work controls

`player.work` is null off the serving panel, else `{ vehicle, minutes, fast_forward, time_skip, stock_read_only }`. After `open`, wait for the matching `player.work.vehicle` and activity; commands refuse while the panel is starting. `fast_forward` is the panel's native toggle. `skip_work` sets the work slider to whole minutes (5 to 720) and presses the time skip; poll `time_skip` or `player.work`, because sell-out, closure, exhaustion or Cancel can end it early. `stop_work` ends the window's activity; `work_window` returns to an open cart's stance; a truck uses `back_to_hatch`. `skip_work` never turns on `keep_needs`.

## Building entry and carried bags

`use click` on a building refreshes native targets, checks the native navigation and minigame guards, then calls the building's own `Interact` within reach or after `MoveTowardsEntity`. It no longer depends on the physical pointer, which a HUD under the cursor could swallow. Vanilla and mod notices are captured once in the reply; `why`, `still_walking` and `still_entering` explain an entry that did not finish. `handled` means the interaction was dispatched, not that entry succeeded. No Harmony patch touches building entry. Native checks: [CHECKS.md](../CHECKS.md).

## Test builds

`tools/build-mod.sh` makes two test-only variants for Persistent City's checks. Each sets one MSBuild property on `Core/FoodTrucks.Core.csproj`, adding one compile symbol. The script and MSBuild both refuse the two together. With no flag the script builds the normal package in `build/FoodTrucks`: no fault type, `ContractVersion` 1.

| Flag | Property | Symbol | Package | Change |
|---|---|---|---|---|
| `--test-faults` | `-p:FoodTrucksTestFaults=true` | `FOODTRUCKS_TEST_FAULTS` | `build/FoodTrucks-test-faults` | Adds `FoodTrucks.VendorFaultHook` and `VendorFaultException` to `FoodTrucks.Core.dll`; `ContractVersion` stays 1 |
| `--test-contract-2` | `-p:FoodTrucksTestContract2=true` | `FOODTRUCKS_TEST_CONTRACT2` | `build/FoodTrucks-test-contract-2` | `VendorApi.ContractVersion` returns 2 and nothing else changes. There is no vendor v2 contract; this build exists only to test a version mismatch, and [vendor-api.md](vendor-api.md) stays the v1 contract |

`dotnet test tests/FoodTrucks.Core.Tests.csproj -c Release` takes the same properties; `VendorTestBuildTests` checks which variant it runs against. Never install a test package into a normal profile or upload one. Results from these builds do not count toward [Vendor API v1](../CHECKS.md#vendor-api-v1) acceptance.

The fault hook has one point, `sell_after_ledger`. When armed, the next `VendorService.Sell` whose `IVendorHost.SellOne` returns a sale disarms the hook and throws `VendorFaultException` after the ledger record. The sale then fails as `uncertain`, with no sale and reason `failed after the ledger record; do not retry`, and the opening's sequence is bumped. A failure before the record, or a refusal by `SellOne`, leaves the shot armed. Street Food's own sales never reach the hook. The armed state is memory only and resets whenever a save attaches or detaches.

`vendor_fault` is in every build's `FoodTrucks.dll` and looks the hook up at run time. Without `FOODTRUCKS_TEST_FAULTS` it answers `vendor_fault hook unavailable: this FoodTrucks.Core.dll was built without FOODTRUCKS_TEST_FAULTS (tools/build-mod.sh --test-faults).` A second `arm` is refused until `disarm` or until the shot fires. Replies: `arm` `{ armed: true, point, shots_remaining: 1 }`, `status` `{ armed, point, shots_remaining, fired }`, `disarm` `{ armed: false, fired }`.

### Crew driving probe

Developer only. Shipped crews travel on timers offscreen, and ordinary bridge builds have no physical probe: the probe types, hooks and `crew_drive_probe` command compile only with the `FOODTRUCKS_CREW_DRIVING_DEV` symbol, and no build script sets it. Normal builds keep an inert static `CrewDriveProbe` with no start method, component, ownership or hooks; `tools/tests/test_release_gate.py` and `tools/release/VerifyRelease.csproj` fail if probe types or strings survive in a release DLL. A normal build answers `crew_drive_probe` as an unknown command.

To run it, compile a separate developer package with the symbol defined, install it only on a test rig (`agentmac` or `agent1`) under your own lease, back up and later restore the rig's install, and load a disposable FT save of an FT-only character. All arguments go to `crew_drive_probe` with the usual `expect_session` and lease token. Results and pending gates are in [CHECKS.md](../CHECKS.md#crew-driving-probe).

Preconditions for `start`, from the source guards:

- The bridge is running, every probe hook is active and the mod's save state is writable.
- No probe, probe ownership or retained traffic record is left over.
- Outdoors, unpaused, no time skip, game speed 1, `Time.timeScale` 1 and a fixed step of at most 0.05 s.
- A spawned, owned food truck that nobody controls: not the player's active vehicle, not at its hatch, not on a `drive`.
- The truck is closed, with no crew, no route stops or shifts, no transit and no pending arrival. Park it stationary and aligned at the first route point yourself, with the player outside; the probe never parks or teleports.

| `op` | Arguments | Result |
|---|---|---|
| `status` (default) | `{}` | `Idle`, or the active or last run: phase, reason, session, generation, progress, speed, front clearance, tank and damage, contacts, blocker, cleanup counters |
| `waypoints` | `{ "id" }` or `{ "ids": [..] }` (1 to 512) | Up to 64 native road points within 100 m of the truck, or the named ones: position, `next`, `enter`, `exit`, `stop`, `give_way`, `disabled` |
| `body_contract` | `{ "id" }` | Read-only physical geometry and ground support; no traffic certificate |
| `trigger_coverage` | `{ "id" }` | Read-only common trigger-height selection over the Gley pool |
| `traffic_evidence` | `{ "id" }` | Nearby Gley vehicles, speeds and actions; compare successive samples |
| `visibility` | `{ "id" }` | Read-only collider roles and Gley masks |
| `obstacle_evidence` | `{}` | The decisive collider and query of the active or last run |
| `start` | `{ "id", "waypoints": [2 to 512 connected native ids], "max_speed_ms"?: 0.2..2 (default 2) }` | Starting snapshot with session and generation; poll `status` for admission refusal or driving |
| `stop` | `{ "session", "generation" }` from `status` | Synchronous hold and cleanup; a stale session or generation is refused |

Operational guards that the code enforces: only straight low-speed routes are admitted, with turns and give-way junctions refused; routes must start and end outside junctions; signals and obstacles stop the truck by physical braking. Save, pause, speed change, skip, interior entry, controller replacement, player takeover or new work on the truck interrupt the run with an immediate hold, which is reported apart from a braking stop. Fuel and damage are synchronised from the native vehicle, never refilled or repaired. Refusal before engine acquisition writes nothing to the native vehicle. While a probe owns a truck, `teleport` and every `vehicle` op except `enter` refuse it, and stopping the bridge stops the probe.

## Playthrough runner

`tools/playthrough` plays a fresh character's business through the bridge and stops at the first broken rule. It runs only in a loaded save whose name starts with `FT playtest`. Runs go to `build/playtest/<run>/`: `run.json` (plan, vehicles, notes, `stopped`), `snapshots.jsonl`, `events.jsonl`, `ledger.csv`, `check.json` and `report.md`.

The Phase A fixture is [tests/game/playthrough/phase-a.json](../tests/game/playthrough/phase-a.json): one crewed cart from the Hell's Kitchen commissary on the character's own money, three nearby stops at the map pick's default hours, licence from the debug action, and an optional growth step (off) that buys a truck once money less `reserve` covers it.

```sh
tools/playthrough setup tests/game/playthrough/phase-a.json --run phase-a-1 [--speed 20]
tools/playthrough run --run phase-a-1 --days 6 [--reload-every 7] [--day-timeout 900]
tools/playthrough check --run phase-a-1 [--log Player.log] [--version 0.9.10]
tools/playthrough report --run phase-a-1 [--balance auto|off|FILE]
tools/playthrough watch --run NAME [--stop-file F] [--poll 2] [--record-only]
```

- `setup` sets optional `starting_money`, buys each vehicle with `vehicle buy`, puts it in the bay and rents a space, grants the licence with the `licence` debug action, hires each crew with the `hire` debug action, orders equipment and food, and adds the stops by address. It saves `FT playtest d<day>`, runs to 08:05 on the delivery day, loads each vehicle in the bay and only then runs the auto schedule.
- `run` runs at speed 20 to each 05:00, snapshots `state` and `day_summary`, checks the window, saves `FT playtest d<day>` and, every `--reload-every` days, reloads and compares money, stock, crews and pieces.
- `check` reruns every check offline over all windows and lists every failure; a window without the data a check needs is "not checkable". With `--log` it also runs the log rules.
- `report` writes `report.md` and compares each vehicle's week against `tools/balance --reports playthrough`. A gap beyond 25% either way is a finding, never a failure.
- `watch` is the referee beside a blind tester. It changes nothing except, on a hard failure, pausing the game and writing the reason to the STOP file (default `build/playtest/<run>/STOP`). `--record-only` never pauses.

Hard checks per window: `money` (balance change equals the game's transactions within float rounding), `books` (the mod's own sale, crew sale, catering, rent, fine and order lines against the ledger), `stock` (per food, held plus delivered less sold), `moves` (at 05:00 every vehicle is closed, in its commissary's bay or near yesterday's late stop) and `log` (no `ERROR: ` line, no Unity error naming a FoodTrucks type). Refused deliveries, route problems, other wages and unreconcilable gaps are notes.

`tools/play` is the blind tester's interface: a whitelist over the bridge with plain output and no balance data. `tools/play --help` is its manual. It plays the game's own mechanics (taxi, walking, needs running down, shops and tills, the Food Safety course, phone calls, hiring, furniture), with one shortcut left: `buy truck|cart` is the paid debug spawn. Its game speed is capped at 20, walks run at speed 1, and with `FT_PLAY_STOP` set every acting command refuses once the STOP file exists. It needs only `tools/play` and `tools/ftlib/` (`bridge.py`, `paths.py`, `locks.py`, `__init__.py`, and `shots.py` for `screen --fetch`).

Evidence from these runs has two kinds, and reports must keep them apart:

- Real-player evidence comes from `tools/play` sessions refereed by `watch`: the tester reached the state through the game's own controls.
- Fixture-driven evidence comes from `setup` and `run`. Debug spawns, the free licence, debug hires, accelerated time and direct bridge commands set up the business, so these runs test the books, stock, moves and save round trips, not whether a player can get there. A mock run proves neither.

## Scenarios

A scenario is a JSON file in `tests/game/scenarios/`. Each starts with a note on how to prepare its named FT save; read it before running. Phone quantity entry, map picks and reference placement in those notes stay manual and never count as automated passes.

```sh
tools/scenario list
tools/scenario validate tests/game/scenarios/commissary-pay-on-delivery.json
tools/scenario run tests/game/scenarios/commissary-pay-on-delivery.json [--report-dir DIR] [--keep-going]
```

```json
{
  "name": "commissary-pay-on-delivery",
  "checks": "CHECKS.md, Commissary delivery and settlement, delivery success (CO1, CO2)",
  "needs": {},
  "steps": [
    { "note": "Prepare 'FT commissary-pay-on-delivery' by hand: ..." },
    { "call": "load", "args": { "name": "FT commissary-pay-on-delivery" } },
    { "wait_state": "load.status", "not_equals": "pending", "timeout_s": 300 },
    { "expect": "state", "path": "load.status", "equals": "done" },
    { "remember": "money", "from": "state", "path": "money" },
    { "call": "time", "args": { "run_until": { "day": "+1", "time": "08:05" }, "speed": 20 } },
    { "wait_event": "Commissary delivery: order (?P<n>\\d+)", "captures": { "order": "n" }, "timeout_s": 300 },
    { "expect": "state", "path": "commissary.orders", "length": 0 }
  ]
}
```

Top-level fields: `name`, `checks` (shown in reports), optional `needs` (`scene`, `save_prefix`) and `steps`. Step kinds:

- `call` with `args`, optional `expect_ok: false` and `timeout_s`.
- `remember` a value into `$name` from `state` or `result` (the last call's result), with optional `scale` (rounded to cents).
- `expect` on `state` or `result`: `equals`, `not_equals`, `less`, `greater`, `length`, `contains`, `matches`; `where` filters a list by field equality and `pick` reads a path in it. A missing path fails at once.
- `wait_state` polls `state` until a path meets the condition (`timeout_s`, `poll_s`); a missing path counts as not ready.
- `wait_event` matches a regex over new events from the previous step's start, or after step `after_step` (0 is the run's start). `captures` maps remembered names to named groups, parsing numbers. Matches are consumed in order.
- `no_event` asserts a regex did not appear between `from_step` and `to_step` (default: the previous step through this one).
- `shoot` (pose and target, or explicit camera), `sleep_s`, `note`. Any step may be `"soft": true`.

Paths are dotted with list indexes (`vehicles.0.stock.1.amount`) and filters (`vehicles[kind=truck].legal`). `$name` substitutes exactly; `vehicle:$name` builds a teleport target. Reports go to `build/scenarios/<name>-<time>/`: `report.md` (failures first), `report.json`, `events.jsonl`, fetched PNGs and an offline contact sheet `index.html`. Mock pictures are placeholders and cannot pass an art check.

## Log checks

`tools/check-log <Player.log> [<version>] [--rows ID,...|all]` checks a log against `tests/game/logchecks/<version>.txt`, taking the version from the log's last `Version ... loading.` line when none is given. Unknown versions get only the always-on checks.

```
# id | rule | regex | row (optional)
order.placed | must | Commissary order \d+: 2 boxes, .+ | row 0.9.10/1,0.9.10/2
no.notActive | never | Patch .+: Not active
```

Rules: `must` (at least once), `never`, `count=N`, `count>=N` (`count>=0` reports a count without requiring it) and `order` (regexes joined by ` >> ` appear in that order). Rules with rows count only when `--rows` selects one of them or `all`; rules without rows always count. The always-on checks list every `Patch ...: Active.` line and fail on any `Not active` patch line, any Food Trucks error line (`[Mod:Food Trucks] ERROR: `, also the older unspaced prefix) and any exception naming a FoodTrucks type. The exit code is non-zero on any failure. MCP `ft_check_log` and `tools/playthrough check --log` use the same checker.

## Mock bridge and tool tests

`tools/mock-bridge [--port 8330] [--clock-rate 1]` mirrors the command guards, errors, reply shapes, null fields, sessions, op ids, asynchronous entry and inclusive events. It simulates a small economy, a player's life (taxi, shops, school doors, recruitment, a bed, a 6 x 5 m room with a blocked square and a hole, hand trucks) and writes PNG placeholders. Its prices and numbers are made up and it cannot model Unity navigation, placement geometry, rendering, UI bindings or real saves, so a mock pass is not a game check.

```sh
python3 -m unittest discover -s tools/tests
FT_TEST_TRANSPORT=inprocess python3 -m unittest discover -s tools/tests
```

The second form runs the same HTTP handlers in memory where the sandbox forbids listening sockets; it does not exercise socket transport. Tests use a free loopback port and never connect to a game.
