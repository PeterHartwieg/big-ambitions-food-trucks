# Run log: food truck

- Model: GPT 6.1 Sol in Codex CLI 0.159.3, reasoning high, `codex exec ... < /dev/null` with `-c mcp_servers.blender.default_tools_approval_mode="approve"`
- Prompt: `prompt.md` as it was before the two fixes below, with `{OUT}` replaced by this folder
- Start: 8 October 2026, 11:10 local time. Wall time 13 min 59 s.
- Tool calls: 41 (22 Blender MCP calls, 19 shell commands)
- Tokens: 2,064,251 input (1,974,528 cached), 20,356 output (6,141 reasoning). About $0.58 at $2 input, $0.10 cached and $10 output per million (prices unconfirmed).
- Interventions: none
- Scorer: Sol ran it until it passed; my own rerun after the run also printed `SCORE PASS`.
- Unity: `FoodTruck.fbx` imported into an empty Unity 2022.3.62f2 project with rotation 0 and scale 1 on every node and two UV channels; positions match the table in `../README.md`. Rotating `Truck_Hatch` to local Z = +80° swings it out over the curb (centre moves from x 1.12 to 1.57).
- Light masks checked against the geometry: `EM_1` R on the headlights (front), G on the tail lights and B on the reverse lights (rear); `EM_2` R on the right-side blinkers, G on the left-side ones.

| Object | Triangles |
|---|---:|
| Truck_Body | 1,832 |
| Truck_Wheel_FL, FR, RL, RR | 400 each |
| Truck_Hatch | 48 |
| Visible total | 3,480 of 8,500 |
| Truck_Collision | 96 |

UV fill 63% on both maps, no overlap. Collision hull strictly convex.

## Found during the run, fixed in the prompt afterwards

- **Shelf versus envelope.** The prompt asked for a 0.20 m shelf (x to −1.30) while `spec.json` limits the model to x ≥ −1.26. Sol noticed, built a 0.16 m shelf and said so. The prompt now asks for 0.16 m.
- **Scorer crash in the sandbox.** `blender -b` started from Codex's shell crashes during Metal GPU detection (exit 139, a macOS crash dialog each time). Sol worked around it by starting the scorer from the running Blender through MCP. Both prompts now say to do that from the start.

Renders: `renders/01`–`06` are Sol's own; `renders/review/` are the fixed review views, closed and with the hatch open. Sol also left `delivery-report.md` and `build_summary.json`.

## Detail pass — 8 October 2026

Rebuilt detailed model, regenerated all six textures, exported with original FBX settings. Reviewed all angles and serving close-up. Final: Truck_Body 4216, hatch 164, wheels 400 each, visible 5980, collision 138. SCORE PASS (blend and FBX), no failures. Marker/work-light pixels independently verified.

## Shaping pass, 8 October 2026 (version 3)

Peter: "too boxy, especially around the front". Prompt `prompt-shape.md`, with two Wikimedia Commons step-van photos as references (a 1997 Chevrolet P30 for the front, a taco truck for the side; the photos are kept outside this repo). 13 min 16 s, 46 tool calls, about $0.60, no interventions. Truck_Body 4,216 → 6,400, collision 138 → 122, visible 8,164 of 8,500. Rounded cap with five marker lights, 12° split windscreen, narrower sloping hood, twin square headlights, curved fenders, bevelled box edges, stainless diamond-plate lower body. My scorer rerun: `SCORE PASS`; Unity import: rotation 0, scale 1, same positions, hatch opens at z = +80. The marker and work lights share `EM_1` R with the headlights, so they light together.

The detail pass before it (version 2, `prompt-detail.md`): 15 min 28 s, 41 tool calls, about $0.61, no interventions; Truck_Body 1,832 → 4,216. Archived in `v2/`.

## Taller compartment, 9 October 2026 (version 4)

The serving window was too low for the interior: a 1.8 m person on the floor (z 0.94) showed only their torso. Edited `build_truck.py` by hand (Opus 5.5 subagent) and rebuilt headless with `Blender -b FoodTruck.blend --python art/truck/build_truck.py` from the worktree root.

- Compartment roof 2.90 → 3.10 (ceiling 2.76 → 2.96); the cab keeps 2.90, with a sloped step at y −0.98…−0.92 and side wedges closing it. Roof units, the rear rollover, rear drip rail, rear upper markers and the side drip channel (now compartment only) rise 0.20; the ladder reaches 3.06.
- Serving opening z 1.20…2.15 → 1.80…2.82; hinge and `Truck_Hatch` origin at 2.82. Frame, sill, head, shelf (top 1.75), supports, sliding panes (stretched to the new height, track still at x −0.815…−0.785), lamps and menu panel move with it; the hatch's bands and stays are stretched to the new height.
- Interior fittings +0.60: worktop 1.65…1.75 on a stainless cabinet down to the floor (two door seams), back counter, griddle station, shelves, hood, tanks; backsplash 1.75…2.94.
- Collision: roof cut-off 2.901 → 3.101; candidate planes now need 2.6° between them (was 1.8°), because a sliver corner near the rear bumper broke strict convexity (1.5 mm).
- `spec.json`: hinge, near points, envelope, extent and the window's clear box updated to the new heights.

Triangles: Truck_Body 6,400 → 6,492 (budget 6,500), hatch 164, wheels 400 each, visible 8,256, collision 122 → 126. UV fill 58%. Scorer: `SCORE PASS` (blend and FBX). Renders: `renders/review-sheet.png` (hatch open), `renders/review-sheet-closed.png`, single views in `renders/review/`. Not yet re-imported into Unity.

Follow-up the same day: cleared the aisle behind the hatch. The griddle station and its knobs, its shelf and the hood now stop at x −0.42 (were −0.59, −0.50, −0.45); the worktop at −0.43 and the cabinet at −0.42. A box x −0.74…−0.44, y 0.20…2.00, z 0.95…2.74 (three crew bodies at Unity x 0.59) is clear of Truck_Body; the inner sliding pane stays at x ≥ −0.754. Truck_Body 6,492 tris, `SCORE PASS`.
