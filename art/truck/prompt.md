# Task: model the final food truck for a Big Ambitions mod

You are driving a running Blender 5.2 instance through the `blender` MCP server (`execute_blender_code`, scene summaries, screenshots, viewport and thumbnail renders, API and manual search). Work on your own until the task is finished; nobody will answer questions during the run.

The asset is the food truck of a public Workshop mod for the PC game Big Ambitions (Unity 2022.3, HDRP, New York setting). The player drives it, parks at the curb and sells through a serving window on the curb side. It replaces the mesh of the game's example car inside the same vehicle prefab, so its size, wheel positions and pivots are fixed and must match the numbers below exactly. The game's cars are clean low-poly shapes with flat colours and simple painted details, not photoreal sculpts. Neutral art: no text, logos, brand names, licence plate numbers or flags anywhere.

## Coordinates (read carefully)

Blender units are metres, Z up. **The truck's front faces −Y. Its left (driver's) side is +X, its right (curb, serving) side −X.** This is what the FBX settings below turn into Unity's forward (+Z) and right (+X); it was measured in Unity. Do not build it facing +Y.

The origin (0, 0, 0) is on the ground, halfway between the front and rear axles, on the centre line.

## The vehicle: a compact step van food truck

A classic American step van converted into a food truck: a short sloped hood and a flat windscreen in front, a tall boxy body behind it with a flat roof, a big serving window with a top-hinged awning hatch on the curb side, and a small serving shelf under the window. Single rear wheels (no duals).

## Size and layout (Blender coordinates)

- **Overall**: 5.80 m long (Y from −2.65 at the front bumper to +3.15 at the rear bumper), 2.20 m wide across the body (X from −1.10 to +1.10; mirrors may stick out to ±1.25), 2.90 m high (roof at z = 2.90). Ground clearance under the body about 0.35 m.
- **Wheels**: four wheels, 0.76 m diameter, 0.23 m wide, axle along X. Hubs at:
  - front left (+0.87, −1.80, 0.38), front right (−0.87, −1.80, 0.38)
  - rear left (+0.87, +1.80, 0.38), rear right (−0.87, +1.80, 0.38)
  Dark tyre, simple light-grey rim with a hub cap; the tread can be a plain cylinder.
- **Wheel arches**: the body must leave each wheel room to turn and bounce: no body geometry within 0.44 m of the hub along Y, below z = 0.84, between x = 0.745 and 0.995 on either side (mirrored for the right side).
- **Cab** (front, about y −2.65 to −0.9): hood, grille, bumper, two round or rectangular headlights, a windscreen, side windows, a driver's door on the left (+X) and a door on the right, two side mirrors.
- **Serving window** on the right (−X) side, behind the cab: an opening from y = +0.20 to +2.00 and z = 1.20 to 2.15 in the side wall at x = −1.10, at least 0.30 m deep (nothing of the body between x = −1.09 and −0.82 inside it), with a simple interior visible through it: a back counter and a dark interior wall.
- **Serving shelf**: a stainless shelf along the bottom of the window, sticking out 0.16 m from the side (x from −1.10 to −1.26), top surface at z = 1.15, from y ≈ +0.25 to +1.95.
- **Hatch**: one flat awning panel that covers the window opening when closed (on the outside face, from y = +0.20 to +2.00, z = 1.20 to 2.15, about 0.04 m thick), hinged along its top edge. The hinge line runs along Y at x = −1.10, z = 2.15; rotating the hatch about that line swings its bottom edge outwards (towards −X) and up to make an awning. A visible hinge rail on the body along that line.
- **Rear**: a rear door, two tail lights, two reverse lights, a step bumper.
- **Lights**: headlights (front), tail/brake lights (rear), reverse lights (rear), and amber blinkers at the four corners. Each light is its own faces with its own UV island so the emission masks below can light it.
- **Fuel cap**: a small round cap on the left (+X) side near (+1.10, +2.30, 0.9).
- **Paint**: a cheerful single body colour (for example a warm cream or a soft red) with a contrasting stripe at the bottom of the body; stainless trim around the window; black rubber and dark grey plastics; dark tinted glass.

## Spec

- At most 8,500 triangles for all visible objects together: `Truck_Body` at most 6,500, each wheel at most 400, the hatch at most 200. The collision mesh doesn't count. Spend triangles on silhouettes, not hidden faces.
- Real size in metres. Rotation 0 and scale 1 applied on every object.
- Objects, with these exact names and origins:
  - `Truck_Body`: everything except the wheels and the hatch. Origin at (0, 0, 0).
  - `Truck_Wheel_FL`, `Truck_Wheel_FR`, `Truck_Wheel_RL`, `Truck_Wheel_RR`: one object per wheel, origin exactly at its hub (positions above). L is the +X side.
  - `Truck_Hatch`: the serving hatch, origin on its hinge line at (−1.10, 1.10, 2.15).
  - `Truck_Collision`: a convex collision hull of the whole truck (body and wheels, without mirrors), at most 150 triangles. Build it with `bmesh.ops.convex_hull` so it is strictly convex. Hide it from renders.
- **One material** shared by all visible objects, with **two UV maps** on every visible object: the first (`UVMap`) for all textures below, the second (`UV2`) a non-overlapping layout inside 0–1 used by the game for dirt (it may be a copy of the first). Both without overlaps, inside 0–1, and packed so that at least 40% of the texture is covered (check with the scorer).
- **Textures**, all 2048 × 2048 PNG in the output folder, all in the first UV map. They are made for the game's vehicle shader, which takes the same set as the game's example car:
  - `T_FoodTruck_D.png`: base colour in RGB; alpha 1 on the painted body panels and 0 elsewhere.
  - `T_FoodTruck_MaskMap.png`: R metallic (1 on chrome and stainless, 0 elsewhere), G ambient occlusion (1 by default, darker in recesses if you want), B 0, A smoothness (about 0.6 paint, 0.9 glass and chrome, 0.2 rubber).
  - `T_FoodTruck_N.png`: a flat normal map, RGB (128, 128, 255).
  - `T_FoodTruck_E.png`: emission colour: warm white on headlights and reverse lights, red on tail lights, amber on blinkers, black elsewhere.
  - `T_FoodTruck_EM_1.png`: emission masks, R = headlights, G = tail/brake lights, B = reverse lights; 1 on the light, 0 elsewhere.
  - `T_FoodTruck_EM_2.png`: blinker masks, R = right (−X) blinkers, G = left (+X) blinkers, B 0.
  In the Blender material, connect only the base colour (and, if you like, emission for the renders); the other maps are for the game.
- Clean geometry: no loose vertices or edges, no flipped normals, no n-gons, closed parts where the shape allows.
- Export `FoodTruck.fbx` with exactly these settings (they were checked in Unity 2022.3; with them Blender −Y becomes Unity +Z, scale 1 and rotation 0):

  ```python
  bpy.ops.export_scene.fbx(filepath=OUT + "/FoodTruck.fbx", use_selection=True, object_types={"MESH"},
      global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
      axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_space_transform=True,
      mesh_smooth_type="OFF", use_triangles=True, use_tspace=True, bake_anim=False,
      add_leaf_bones=False, path_mode="RELATIVE", embed_textures=False)
  ```

  with only the seven objects selected.
- Save the scene as `FoodTruck.blend` in the output folder. Keep it under 20 MB (don't pack the images).

## Starting point

Blender has `start.blend` open: metric units and a wireframe box `HumanRef_1.8m` (a 1.8 m person for scale; leave it alone, it isn't rendered or exported). Don't open other files, and don't write outside the output folder.

## Checking your work

A scorer measures the result against this spec. Run it after exporting, read every failure, fix it, export again and rerun until it prints `SCORE PASS`. Start it from the running Blender with `execute_blender_code`, as a separate background Blender process, because a `blender -b` started from your shell sandbox crashes while probing the GPU:

```python
import subprocess
p = subprocess.run(["/Applications/Blender.app/Contents/MacOS/Blender", "-b", OUT + "/FoodTruck.blend",
    "--python", "art/tools/score.py",
    "--", OUT + "/spec.json"], stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=300)
result = {"exit": p.returncode, "log": p.stdout[-4000:]}
```

It writes `score.json` in the output folder. Do not edit the scorer or `spec.json`.

## Output folder

`{OUT}`

Put everything there. After each major step (blockout, detail, UVs and textures, final), save a viewport screenshot or render to `{OUT}/renders/` named `01-blockout.png`, `02-detail.png` and so on, and look at it before moving on. Save the build code you used as `{OUT}/build_truck.py` so the model can be regenerated.

## Finish

End with a short report: what you built, triangles per object, the final scorer result, and anything in the spec you could not meet and why.
