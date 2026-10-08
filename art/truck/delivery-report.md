# Detailed food truck delivery

Added faceted black fenders, mud flaps, chassis rails, a strapped fuel tank and exhaust, cab steps, protective rails, generator louvres, service panels, panel seams and painted rivets, amber markers, serving lamps, pictogram menu, sliding window frames and glass, a griddle, stainless shelves and hood, striped awning underside and paired stays, rear windows and hinges, ladder, tank rack, blank plate recesses, bumper tread/wear, roof A/C and exhaust hood. Roof equipment stays below 3.05 m; the roof remains at 2.90 m.

| Object | Before | After |
| --- | ---: | ---: |
| Truck_Body | 1,832 | 4,216 |
| Truck_Wheel_FL | 400 | 400 |
| Truck_Wheel_FR | 400 | 400 |
| Truck_Wheel_RL | 400 | 400 |
| Truck_Wheel_RR | 400 | 400 |
| Truck_Hatch | 48 | 164 |
| Truck_Collision | 96 | 138 |

Visible total: 5,980 / 8,500. Collision is excluded.

Final unmodified scorer: **SCORE PASS**, zero failures in both the saved Blender scene and re-imported FBX. UVMap and UV2 each cover 54.56% with zero overlap. One shared material and six regenerated external 2048 × 2048 textures.

The build script was rebuilt from an empty asset scene, retaining the original HumanRef scale reference. The four wheels were verified identical before/after rebuilding. Object names, all origins, hinge, opening and export settings remain intact. New marker/work-light faces have their own atlas islands and EM_1.R entries, so they follow the headlight circuit without changing channel meanings. Texture pixel checks are recorded in texture_verification.json.

The collision hull was expanded to enclose the new fenders, steps and rear hardware. A single front bumper plane encloses the added plate bezel without unnecessary tiny collision facets. Roof equipment is excluded from the hull as permitted.

FoodTruck.blend and FoodTruck.fbx are final; the hatch is saved closed with zero rotation. Rotate local Y +80° to inspect the open awning. Required renders 07–10 and final reviews 11–12 are in renders/. The v1 archive, scorer and spec.json were not changed.

No required specification item remains blocked. In-game appearance and integration were not checked in this Blender task.
