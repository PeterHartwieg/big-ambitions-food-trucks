# Task: model a cotton candy machine for a Big Ambitions mod

You are driving a running Blender 5.2 instance through the `blender` MCP server (`execute_blender_code`, scene summaries, screenshots, viewport and thumbnail renders, API and manual search). Work on your own until the task is finished; nobody will answer questions during the run.

The asset belongs to a public Workshop mod for the PC game Big Ambitions (Unity 2022.3, HDRP, New York setting) that adds street food carts and trucks. It is a piece of equipment that stands on a food cart's counter (in one of two slots) or on the truck's counter behind its serving hatch, and the player can also buy it in a shop and carry it. Customers see it from the street, a few metres away, so it must say at a glance what the vendor sells. Game props are clean, simple low-poly shapes with flat colours, not photoreal sculpts. Neutral art: no text, logos, brand names or flags anywhere.

## What to build

A cotton candy maker: a square base cabinet, a wide round steel bowl on top with a spinner head in the middle and a clear bubble guard rim, plus a holder at one side with three pink and blue cotton candies on paper sticks.

## Coordinates (read carefully)

Blender units are metres, Z up. **The front of the equipment, the side that faces the customer, faces −Y.** Its left side is +X. This is what the FBX settings below turn into Unity's forward; it was measured in Unity. Do not build it facing +Y.

The origin (0, 0, 0) is on the floor (the surface it stands on), under the middle of its footprint. The game places it by this point.

## Size

- Everything visible inside x −0.20 to +0.20, y −0.18 to +0.18, z 0 to 0.50. The cart's counter is small; stay inside this footprint so any piece fits any slot. Use most of it: the piece should look full-size for a cart, not like a toy.
- It stands flat on z = 0: the underside covers the origin.
- Real proportions in metres; use `HumanRef_1.8m` to judge scale.

## Spec

- At most 1,500 triangles for `CottonCandyMachine_Body`. Spend them on the silhouette and the parts that read from a few metres away, not on hidden faces.
- Rotation 0 and scale 1 applied on every object.
- Objects, with these exact names:
  - `CottonCandyMachine_Body`: the whole visible model, origin at (0, 0, 0).
  - `CottonCandyMachine_Collision`: a convex collision hull around it, at most 60 triangles, built with `bmesh.ops.convex_hull` so it is strictly convex. Origin at (0, 0, 0). Hide it from renders.
- One material on `CottonCandyMachine_Body`, using one base-colour texture `T_CottonCandyMachine_BaseColor.png`, 512 × 512, saved in the output folder. Flat colour blocks per part. UVs without overlaps, inside 0–1, and packed so that at least 40% of the texture is covered (check with the scorer).
- Clean geometry: no loose vertices or edges, no flipped normals, no n-gons, closed parts where the shape allows.
- Export `CottonCandyMachine.fbx` with exactly these settings, with only the two objects selected:

  ```python
  bpy.ops.export_scene.fbx(filepath=OUT + "/CottonCandyMachine.fbx", use_selection=True, object_types={"MESH"},
      global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
      axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_space_transform=True,
      mesh_smooth_type="OFF", use_triangles=True, use_tspace=True, bake_anim=False,
      add_leaf_bones=False, path_mode="RELATIVE", embed_textures=False)
  ```

- Save the scene as `CottonCandyMachine.blend` in the output folder.

## Starting point

Blender has `art/start.blend` open: metric units and a wireframe box `HumanRef_1.8m` (a 1.8 m person for scale; leave it alone, it isn't rendered or exported). Don't open other files, and don't write outside the output folder.

## Checking your work

A scorer measures the result against this spec. Run it after exporting, read every failure, fix it, export again and rerun until it prints `SCORE PASS`. Start it from the running Blender with `execute_blender_code`, as a separate background Blender process, because a `blender -b` started from your shell sandbox crashes while probing the GPU:

```python
import subprocess
p = subprocess.run(["/Applications/Blender.app/Contents/MacOS/Blender", "-b", OUT + "/CottonCandyMachine.blend",
    "--python", REPO + "/art/tools/score.py",
    "--", OUT + "/spec.json"], stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=300)
result = {"exit": p.returncode, "log": p.stdout[-4000:]}
```

It writes `score.json` in the output folder. Do not edit the scorer or `spec.json`.

## Folders

- Repository: `{REPO}`
- Output folder: `{OUT}`

Put everything in the output folder. After each major step (blockout, detail, UVs and texture, final), save a viewport screenshot or render to `{OUT}/renders/` named `01-blockout.png`, `02-detail.png` and so on, and look at it before moving on. The last one, `04-final.png`, shows the finished model from the front at a slight angle from above, as a customer would see it. Save a small Python file with the build code you used as `{OUT}/build_cotton_candy_machine.py` so the model can be regenerated.

## Finish

End with a short report: what you built, triangles per object, the final scorer result, and anything in the spec you could not meet and why.
