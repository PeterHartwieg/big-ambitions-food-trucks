# Task: make the food truck less boxy, especially the front

You are driving a running Blender 5.2 instance through the `blender` MCP server. Work on your own until the task is finished; nobody will answer questions during the run.

Blender has `{OUT}/FoodTruck.blend` open: the food truck you built and detailed earlier for a Big Ambitions mod. The reviewer's verdict: **it is too boxy, especially around the front.** Every edge is a hard 90° corner and the cab reads as a box on a box. Reshape it so it reads as a real step van with soft, rounded forms, still in the clean low-poly, flat-colour style of the game's cars. Keep all the detail you added unless a reshaped panel replaces it.

Read `{OUT}/prompt.md` (the original spec) and `{OUT}/prompt-detail.md` (the detail pass) first; every rule in them still applies. Do not change anything the game relies on: object names, every origin, the wheel sizes and positions, the hatch's hinge line and the serving window opening, the overall size (front bumper at y = −2.65, rear bumper at +3.15, roof top at 2.90, body ±1.10), the export settings, the texture set and its channel meanings, and the UV rules.

## Reference photos

Look at both before you start (use the `-1600` copies; the full-size files are larger):

- `stepvan-p30-left-front-1600.jpg` (a Wikimedia Commons reference photo, kept outside this repo): a plain step van from the front left. **This is the shape to match for the front**: the sloped hood with its rounded edges, narrower than the box; twin square headlights each side of an upright grille; a two-pane windscreen raked back with a centre pillar; the rounded cap above the windscreen with a row of amber marker lights; the rounded roof edges; the round front wheel opening; the plain wide bumper; the rub rail along the side.
- `taco-truck-stepvan-1600.jpg` (a Wikimedia Commons reference photo, kept outside this repo): a step-van food truck from the side. Use it for the profile (short sloping nose, rounded roof corners at both ends), the stainless lower body with a diamond-plate pattern (as a texture), the stainless trim around the rear wheel opening, and the serving window with its awning frame. Its window is on the driver's side; ours stays on the right (−X) side.

Both photos carry text, a business name and a licence plate; copy none of it. Keep our dimensions where they differ from the photos. Keep the cream body and a teal stripe; the lower body below the rub rail may become stainless diamond plate like the taco truck's.

## What to change

Reference shape: the classic American step van (the kind used for parcel delivery and converted into food trucks), as in the photos above.

- **Front of the box**: round the front of the body where it meets the roof, with a curved cap over the windscreen (a radius of about 0.25–0.35 m seen from the side), and round the two front vertical corners of the box (about 0.15–0.2 m radius seen from above).
- **Windscreen**: rake it back noticeably (about 10–15° from vertical) instead of standing it upright, split in two panes with a centre pillar, set in a frame, with the A-pillars following the rake.
- **Hood and nose**: a short hood that slopes down to the front, with rounded front corners and a softened top edge, narrower than the box; the grille set into a surround, the headlights in housings with a slight bulge, the bumper with rounded or chamfered ends.
- **Fenders**: front fenders as rounded shapes that blend into the hood and cab sides instead of flat panels, around the arch flares.
- **Rest of the body**: bevel all long edges of the box (the roof edges and the rear vertical corners about 0.06–0.1 m, two segments), chamfer the bumpers, step and skirt edges, round the mirror heads, and soften the roof units' corners. Nothing should look like an untouched cube.
- **Profile check**: from the side, the outline should run from the bumper, up the sloped hood, along the raked windscreen, round over the cap into the roof; from the front, the cab should taper slightly from the box to the hood.

Use bevels with one or two segments, a few extra edge loops and moved vertices; avoid subdivision surfaces and dense meshes. Smooth shading is fine on rounded panels, but keep hard edges where panels meet (mark sharp edges or split them, so the export keeps them flat with `mesh_smooth_type="OFF"`).

## Budget

`Truck_Body` at most 6,500 triangles, `Truck_Hatch` at most 200, wheels unchanged at 400 each, visible total at most 8,500. Rounding costs triangles: spend them on the front and the box's outer edges first, where they change the silhouette.

Update `Truck_Collision` to follow the new outline (still a convex hull from `bmesh.ops.convex_hull`, at most 150 triangles).

## Process

1. Save `renders/11-shape-before.png` (front three-quarter view and a straight side view).
2. Reshape the front first and save `renders/12-front.png` (front three-quarter, straight front and straight side views). Compare it with the brief above before moving on.
3. Round the rest of the body, save `renders/13-body.png`.
4. Fix the UVs for changed faces, regenerate all six textures and check that the lights still sit on their masks. Save `renders/14-final.png` from front three-quarter, side and rear three-quarter.
5. Update `{OUT}/build_truck.py` so it rebuilds this model from an empty scene, and rebuild from it once to make sure it works.
6. Export `FoodTruck.fbx` with the original settings, save `FoodTruck.blend`, and run the scorer as described in `{OUT}/prompt.md` (from the running Blender, as a subprocess) until it prints `SCORE PASS`. Do not edit the scorer or `spec.json`.

`{OUT}/v1/` holds the first version; don't change it.

## Finish

End with a short report: what you reshaped, the triangles per object before and after, the final scorer result, and anything you could not do and why.
