# Task: add detail to the food truck model

You are driving a running Blender 5.2 instance through the `blender` MCP server. Work on your own until the task is finished; nobody will answer questions during the run.

Blender has `{OUT}/FoodTruck.blend` open: the food truck you built earlier for a Big Ambitions mod (a compact step van, cream with a teal stripe). It passes its scorer, but the reviewer finds it **too plain**: big flat sides, wheel openings that are just cut-outs, an empty roof, little to look at from any angle. Make it read as a lived-in, working New York food truck at game-camera distance, in the same clean low-poly, flat-colour style as the game's cars. Neutral art: no text, logos, brand names, numbers or flags anywhere (abstract pictograms are fine).

Read `{OUT}/prompt.md` first: it is the original spec, and every rule in it still applies. Do not change anything that the game relies on: the object names, every origin, the wheel sizes and positions, the hatch's hinge line and the serving window opening, the export settings, the texture set and its channel meanings, and the UV rules. Keep the light faces and their emission masks working; new lights (marker lights) need their own mask entries.

## Detail to add

Pick what reads best; the list is a menu, not a checklist, but cover every view (front, both sides, rear, roof):

- **Wheels and arches**: black wheel-arch flares or fenders around all four openings, mud flaps behind the rear wheels, a visible chassis rail, fuel tank and exhaust pipe between the axles on the left side, a side skirt or step under the body.
- **Sides**: a rub rail or trim strip along the body, vertical panel seams and rivet lines (geometry or texture), amber side marker lights, a small generator compartment door with vents on the left side, a menu board panel next to the serving window on the right (blank or with simple food pictograms, no text), and a pair of small exterior lights above the window.
- **Serving window**: a proper stainless frame, two sliding glass panes (one open), a visible interior with a back counter, shelves, a griddle or fryer, and an exhaust hood above it; two simple struts holding the awning, as part of `Truck_Hatch`; stripes or a contrasting colour on the awning's underside.
- **Cab**: door handles, a step under each door, windscreen wipers, A-pillars, mirror arms, a front bumper with a blank licence plate recess, turn-signal housings.
- **Rear**: rear-door hinges and a small window in each door, a ladder to the roof, a step bumper with a tread pattern, a blank plate recess, a propane tank rack.
- **Roof**: an air-conditioning unit and an exhaust vent hood, a roof edge drip rail, small marker lights at the roof corners. Roof equipment may rise above the 2.90 m roof up to **3.05 m** (the scorer allows this now); the roof itself stays at 2.90.
- **Texture**: panel lines, a soft darker gradient low on the body and in recesses (bake or paint it into the base colour and the mask map's AO channel), slightly worn edges on the bumpers and steps. Keep it subtle and flat-shaded; no photo textures.

## Budget

- `Truck_Body` at most 6,500 triangles (it uses about 1,800 now, so there is room), `Truck_Hatch` at most 200, wheels unchanged at 400 each, visible total at most 8,500. Spend triangles on what changes the silhouette and what the camera sees from street level; use the texture for small flat detail.
- Keep `Truck_Collision` as it is unless the new parts stick out past it; the roof equipment may stay outside the hull.

## Process

1. Look at the current model from every side and save `renders/07-before.png`.
2. Add the detail, saving renders `08-detail-sides.png`, `09-detail-rear-roof.png`, `10-textured.png` and looking at each before you move on.
3. Update `{OUT}/build_truck.py` so it rebuilds the detailed model from an empty scene, then rebuild from it once to make sure it works.
4. Export `FoodTruck.fbx` with the original settings, save `FoodTruck.blend`, and regenerate all six textures.
5. Run the scorer as described in `{OUT}/prompt.md` (from the running Blender, as a subprocess) until it prints `SCORE PASS`. Do not edit the scorer or `spec.json`.

The previous version is archived in `{OUT}/v1/`; don't change it.

## Finish

End with a short report: what you added, the triangles per object before and after, the final scorer result, and anything you could not do and why.
