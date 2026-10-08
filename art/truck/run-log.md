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
