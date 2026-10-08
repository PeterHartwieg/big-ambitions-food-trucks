# Rounded step-van shape pass

Reshaped the cab around a 12° raked, two-pane windscreen, 0.30 m front roof cap, 0.18 m front corner transitions, and narrower sloping hood with softened edges. Added twin square headlights per side in rounded housings and five amber cap markers. Replaced the flat arch flares with circular fender shoulders, retaining rubber gaskets and adding stainless rear arch trim. Rounded the roof edges and rear corners, chamfered bumpers, skirts and steps, rounded mirror heads and softened roof equipment. Kept the cream paint and teal stripe, with a subtle procedural stainless diamond-plate band.

Existing working details remain except where the new panels, lamps and fenders replace their earlier versions. Wheel vertex hashes match exactly. Object names, origins, hatch hinge, serving opening and original FBX settings remain unchanged. The six 2048² textures were regenerated; all 316 light faces and paint-mask classifications passed the channel check. Both UV layouts have 51.44% coverage, zero overlaps and no coordinates outside 0–1.

| Object | Before triangles | After triangles |
| --- | ---: | ---: |
| Truck_Body | 4,216 | 6,400 |
| Truck_Hatch | 164 | 164 |
| Truck_Wheel_FL | 400 | 400 |
| Truck_Wheel_FR | 400 | 400 |
| Truck_Wheel_RL | 400 | 400 |
| Truck_Wheel_RR | 400 | 400 |
| Visible total | 5,980 | 8,164 |
| Truck_Collision | 138 | 122 |

The updated `build_truck.py` was executed from the running Blender after clearing the asset scene, rebuilding geometry, UVs, textures, collision, FBX and blend. The collision is a conservative convex hull built with `bmesh.ops.convex_hull`, with 36 support planes simplifying the detailed outline. Roof equipment and mirrors are excluded as permitted by the brief.

Final scorer: **SCORE PASS**, exit 0, zero failures for the saved blend and reimported FBX. Log: `scorer-shape.log`; full measurements: `score.json`; light checks: `shape_validation.json`. The scene is 0.26 MB with unpacked images. No edits were made to `v1`, `spec.json` or the scorer. No requested work remains incomplete. Game appearance remains unverified in Unity/in game.

Review images: `renders/11-shape-before.png`, `renders/12-front.png`, `renders/13-body.png`, and `renders/14-final.png` (front three-quarter, side, rear three-quarter with the hatch open). The saved/exported hatch is closed.

Rebuild in running Blender:

```python
TRUCK_STAGE = 'all'
path = 'art/truck/build_truck.py'
exec(compile(open(path).read(), path, 'exec'))
```
