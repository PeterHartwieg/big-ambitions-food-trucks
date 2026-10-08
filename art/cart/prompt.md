# Task: model the final food cart for a Big Ambitions mod

You are driving a running Blender 5.2 instance through the `blender` MCP server (`execute_blender_code`, scene summaries, screenshots, viewport and thumbnail renders, API and manual search). Work on your own until the task is finished; nobody will answer questions during the run.

The asset is the push cart of a public Workshop mod for the PC game Big Ambitions (Unity 2022.3, HDRP, New York setting). The player pushes it by its handle, parks it on a sidewalk and sells street food from it. It replaces a placeholder made of boxes, so its size, pivots and attachment points are fixed by the game code and must match the numbers below exactly. Game props are clean, simple low-poly shapes with flat colours, not photoreal sculpts. Neutral art: no text, logos, brand names or flags anywhere.

## Coordinates (read carefully)

Blender units are metres, Z up. **The cart's front faces −Y. Its left side is +X, its right side −X.** This is what the FBX settings below turn into Unity's forward (+Z) and right (+X); it was measured in Unity. Do not build it facing +Y.

The origin (0, 0, 0) is on the floor, under the middle of the cart, directly below the wheel axle. The game turns the cart around this point, so the axle must stay there.

## Reference

a reference photo of a New York hot dog cart (kept outside this repo) (look at it first) shows a New York hot dog cart: a stainless steel box, a counter with steam wells and lids, push handles, spoked wheels with fenders, a side door and a striped umbrella. Use it for parts and style. The layout below differs from the photo (the wheels sit in the middle, the handle is a single bar) and the numbers win.

## Size and layout (Blender coordinates)

- **Body**: a stainless steel box, 0.80 m wide (X from −0.40 to +0.40) and 1.40 m long (Y from −0.70 to +0.70). Flat counter top at z = 0.95. Underside of the box at about z = 0.25.
- **Wheels**: two spoked wheels, 0.50 m diameter, 0.05 m wide, axle along X, hubs at (+0.455, 0, 0.25) and (−0.455, 0, 0.25). Each with a fender above it attached to the body side. Nothing else may come within the wheels' volume.
- **Feet**: so the cart stands level, a small swivel caster under the front end (near y = −0.60) and a folding foot or caster under the rear end (near y = +0.60), both touching the floor (z = 0).
- **Handle**: one horizontal push bar at the rear, along X, centred at y = +0.75, z = 1.16, about 0.035 m thick, spanning x from −0.375 to +0.375, with dark rubber grips around x = ±0.30. Two uprights from the body's rear top edge (y ≈ +0.70, z = 0.95) up to the bar. **The player's hands are placed at (±0.30, 0.75, 1.16)**, so the bar must pass through those points.
- **Counter top**: the game shows up to six cargo boxes standing on the counter in the zone x −0.325 to +0.325, y −0.55 to +0.55, from z 0.97 up. **Nothing may stick up into that zone**: steam well lids, rims and everything else inside it stay flush, at most z = 0.965.
- **Steam well and lid**: one rectangular steam well about 0.30 m (X) × 0.25 m (Y), centred near (0, −0.15), with a flat hinged lid. The lid's hinge runs along X on the well's rear (+Y) edge, at y = −0.025, z = 0.95; when the lid rotates about that line it opens upwards. A second, fixed lid next to it (part of the body) is fine.
- **Umbrella**: a pole standing on the counter at (0, −0.60), outside the cargo zone, and a round striped canopy (8 to 12 panels, two colours, for example red and yellow) 1.60 m across, centred over the pole, its top at (0, −0.60, 2.30) and its lower rim no lower than z = 2.00 so people fit under it. Between z = 1.30 and 1.98 only the pole may be there.
- **Serving side and door**: the right side (−X face) has a small door panel with a handle and hinges; the left side (+X face) is plain or has a menu-board-sized blank panel (no text).
- **Behind the handle** (y from +0.85 to +1.70, any height up to 2.0) stays empty: that is where the player walks.

## Spec

- Under 4,000 triangles for all visible objects together: `Cart_Body` at most 3,000, each wheel at most 400, the lid at most 100. The collision mesh doesn't count. Spend triangles on silhouettes, not on hidden faces.
- Real size in metres. Rotation 0 and scale 1 applied on every object.
- Objects, with these exact names and origins:
  - `Cart_Body`: everything except the wheels and the hinged lid (box, counter, fixed lid, fenders, feet, handle, umbrella). Origin at (0, 0, 0).
  - `Cart_Wheel_L` (+X side) and `Cart_Wheel_R` (−X side): one object per wheel, origin exactly at its hub, (±0.455, 0, 0.25).
  - `Cart_Lid`: the hinged steam-well lid, origin on its hinge line at (0, −0.025, 0.95).
  - `Cart_Collision`: a convex collision hull of the cart without the umbrella and handle bar (body, wheels, z from 0 to about 1.2), at most 150 triangles. Build it with `bmesh.ops.convex_hull` so it is strictly convex. Hide it from renders.
- One material shared by the visible objects, using one base-colour texture `T_FoodCart_BaseColor.png`, 1024 × 1024, saved in the output folder. Flat colour blocks per part (steel, darker steel, rubber, the two umbrella colours, door details). UVs without overlaps, inside 0–1, and packed so that at least 40% of the texture is covered (check with the scorer).
- Clean geometry: no loose vertices or edges, no flipped normals, no n-gons, closed parts where the shape allows.
- Export `FoodCart.fbx` with exactly these settings (they were checked in Unity 2022.3; with them Blender −Y becomes Unity +Z, scale 1 and rotation 0):

  ```python
  bpy.ops.export_scene.fbx(filepath=OUT + "/FoodCart.fbx", use_selection=True, object_types={"MESH"},
      global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
      axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_space_transform=True,
      mesh_smooth_type="OFF", use_triangles=True, use_tspace=True, bake_anim=False,
      add_leaf_bones=False, path_mode="RELATIVE", embed_textures=False)
  ```

  with only the five objects selected.
- Save the scene as `FoodCart.blend` in the output folder.

## Starting point

Blender has `start.blend` open: metric units and a wireframe box `HumanRef_1.8m` (a 1.8 m person for scale; leave it alone, it isn't rendered or exported). Don't open other files, and don't write outside the output folder.

## Checking your work

A scorer measures the result against this spec. Run it after exporting, read every failure, fix it, export again and rerun until it prints `SCORE PASS`. Start it from the running Blender with `execute_blender_code`, as a separate background Blender process, because a `blender -b` started from your shell sandbox crashes while probing the GPU:

```python
import subprocess
p = subprocess.run(["/Applications/Blender.app/Contents/MacOS/Blender", "-b", OUT + "/FoodCart.blend",
    "--python", "art/tools/score.py",
    "--", OUT + "/spec.json"], stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=300)
result = {"exit": p.returncode, "log": p.stdout[-4000:]}
```

It writes `score.json` in the output folder. Do not edit the scorer or `spec.json`.

## Output folder

`{OUT}`

Put everything there. After each major step (blockout, detail, UVs and texture, final), save a viewport screenshot or render to `{OUT}/renders/` named `01-blockout.png`, `02-detail.png` and so on, and look at it before moving on. Save a small Python file with the build code you used as `{OUT}/build_cart.py` so the model can be regenerated.

## Finish

End with a short report: what you built, triangles per object, the final scorer result, and anything in the spec you could not meet and why.
