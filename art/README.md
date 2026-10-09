# Art: cart and truck models

The final food cart and food truck for Milestone 8, built in Blender 5.2 and exported as FBX for the SDK project. Until Milestone 8 the game uses the placeholders built by `unity/BuildEditor/CartAssets.cs` (cart) and the SDK's example car (truck). Nothing here is referenced by the build yet.

| Folder | Contents |
|---|---|
| `cart/` | `FoodCart.blend`, `FoodCart.fbx`, `T_FoodCart_BaseColor.png`, `spec.json`, `prompt.md`, `build_cart.py`, `score.json`, `renders/`, `run-log.md` |
| `truck/` | `FoodTruck.blend`, `FoodTruck.fbx`, six `T_FoodTruck_*.png` textures, the same support files |
| `tools/` | `score.py` (checks a model against its `spec.json`), `render_views.py` (four fixed review renders) |
| `start.blend` | The empty start scene: metres and a 1.8 m wireframe person, `HumanRef_1.8m` |

## Pipeline

The models are built by GPT 6.1 Sol in Codex, driving Blender through the Blender Lab MCP connector. It won a blind benchmark against Opus 5.5 on a grill and a cart (October 2026). Each model has a `prompt.md` with fixed names, origins and budgets, and a `spec.json` that `score.py` checks; the prompt tells the model to run the scorer until it passes.

```bash
blender -b art/cart/FoodCart.blend --python art/tools/score.py -- art/cart/spec.json
blender -b art/cart/FoodCart.blend --python art/tools/render_views.py -- art/cart/renders/review open
```

The scorer checks triangle budgets per object, origins and wheel hubs, hinge lines, applied transforms, clean geometry, a strictly convex collider, one material, UVs (no overlap, inside 0–1, at least 40% fill), texture sizes, attachment points that must lie on the mesh, zones that must stay empty, and a clean FBX re-import with unchanged counts and origins.

## Axes

Blender is Z up. **Models face −Y in Blender, with their left side on +X.** With the export settings in the prompts (`axis_forward="-Z"`, `axis_up="Y"`, `bake_space_transform=True`, `apply_scale_options="FBX_SCALE_ALL"`), Unity 2022.3.62f2 imports them with rotation 0 and scale 1, and maps

| Blender | Unity |
|---|---|
| +X | −X |
| +Y | −Z |
| +Z | +Y |

so Unity = (−x, z, −y). Checked by importing a marker FBX into an empty Unity 2022.3.62f2 project on 8 October 2026. The benchmark prompts said "+Y forward", which with these settings would have pointed the models backwards in game.

Every position below is in Unity coordinates, relative to the prefab root, which is the FBX root.

## Food cart

Replaces the box renderers in `CartAssets.BuildCart`. Everything the code places itself stays as it is: the mesh was built around those numbers, and the scorer checks them.

**Meshes** (`cart/FoodCart.fbx`, all nodes at rotation 0, scale 1):

| Node | Position | Notes |
|---|---|---|
| `Cart_Body` | (0, 0, 0) | Box, counter, handle, fenders, feet, umbrella. 2,948 tris. Extents x −0.80…0.80 (umbrella), y 0…2.30, z −0.77…1.40 |
| `Cart_Wheel_L` | (−0.455, 0.25, 0) | 0.50 m diameter, 0.05 m wide, spins about local X. 388 tris |
| `Cart_Wheel_R` | (0.455, 0.25, 0) | Same |
| `Cart_Lid` | (0, 0.95, 0.025) | Steam-well lid, hinge along local X; open at localEulerAngles.x = −100 (checked in Unity). Stays closed under the front equipment spot (plan, "Cart counter layout"). 88 tris |
| `Cart_Collision` | (0, 0, 0) | Convex hull of body and wheels, x ±0.48, y 0…0.95, z ±0.70. 84 tris. Not needed while the root keeps its BoxCollider |

The body box itself is x ±0.40, z ±0.70, counter top at y 0.95, underside at about 0.25. The umbrella's pole stands at (0, 0.95, 0.60); the canopy is 1.60 m across, top 2.30 m, rim no lower than 2.00 m.

**Attachment points** (constants in `CartAssets.cs`; the mesh matches them):

| Point | Position | Check |
|---|---|---|
| `LeftHandIK`, `RightHandIK` | (∓0.30, 1.16, −0.75) | The push bar's axis passes through both (bar surface 17 mm away, bar 35 mm thick) |
| `LoadingPosition`, `NavMeshTarget` | (0, 0, −1.40) | Nothing of the cart in x ±0.40, z −1.70…−0.85 up to 2.0 m |
| `playerOffsetWhenActive` | −1.40 | |
| `BoxPlaceholder` | (0, 0.5, 0), inside the body, no boxes | The game only switches the placeholder's children on by the cargo count, so an empty one shows nothing and the cargo logic is unchanged |
| `EquipmentFront` | (0, 0.963, 0.30) | Equipment spot behind the umbrella pole, just above the closed lid (top 0.962). A 0.40 × 0.36 m piece turned to any angle stays within 0.27 m of the spot: z 0.03…0.57, clear of the pole at z 0.60 |
| `EquipmentRear` | (0, 0.963, −0.30) | Spot in front of the push bar: z −0.57…−0.03 at any angle, 0.18 m clear of the bar and the hands at z −0.75. The counter is empty here up to 1.17 m (lids flush), and the pieces (up to 0.70 m tall) stay under the canopy's 2.00 m rim |

**Swap steps:**

1. Import `FoodCart.fbx` with Read/Write off and scale 1, `T_FoodCart_BaseColor.png` as sRGB.
2. One HDRP/Lit material with `_BaseColorMap` = `T_FoodCart_BaseColor` (base colour white) for all four visible meshes.
3. In `BuildCart`, instead of the seven `Box(...)` calls, instantiate the four visible mesh nodes under the root on layer `Vehicles` and pass their renderers to `renderers`. Leave out `Cart_Collision`, or keep it disabled for later.
4. Widen the root `BoxCollider` and the `NavMeshObstacle` to cover the wheels: centre (0, 0.6, 0), size (0.96, 1.2, 1.5). The umbrella stays outside the collider, so people can walk under it.
5. Optional: spin the wheels about local X with the rolling speed (done, `CartParts`). The lid no longer opens: equipment stands over it.

## Equipment

Ten compact pieces for the cart's counter spots, one folder each in `equipment/` (`<Name>.fbx` with `<Name>_Body` and `<Name>_Collision`, and `T_<Name>_BaseColor.png`). Each fits 0.40 × 0.36 m, origin at the bottom centre, front (Blender −Y) facing the customer, which is +Z in Unity. Heights 0.40 to 0.70 m.

`tools/build-mod.sh` copies each FBX and texture into `unity/FoodTrucks/Vehicles/EquipmentArt/`, and `EquipmentAssets` makes an HDRP/Lit material and a prefab per model there (body mesh only, no collider). Only the `.meta` files, materials and prefabs are committed. `data/economy.json`'s `display` field picks a piece's model by name: the coffee machine shows `CoffeeStation`, the cooler `DrinksCooler`. The other eight models wait for their foods. The hot dog grill, burger station, ice cream counter, pizza oven and display case have no compact model yet and show nothing; giving one a `display` is all that's needed once it exists.

On the cart, `CartDisplays` puts the first fitted piece on `EquipmentFront` and the second on `EquipmentRear`, turned 90° to face the side the queue forms on while the cart is open, and the cart's right side otherwise.

## Food truck

Replaces the example car's meshes inside the copied `TurboHonza` prefab (`ModAssets.GenerateTruck`). This is version 3 (detail and shaping passes, modelled after the step-van reference photos); `truck/v1/` and `truck/v2/` keep the earlier versions.

**Meshes** (`truck/FoodTruck.fbx`, all nodes at rotation 0, scale 1, two UV channels):

| Node | Replaces | Position | Notes |
|---|---|---|---|
| `Truck_Body` | `CarHolder/Body/CheapSaloon` | (0, 0, 0) | 6,400 tris. Body x ±1.10 (mirrors and shelf to −1.25 / +1.26), y 0.20…2.90 with roof units to 3.05, z −3.15…2.65 (5.80 m) |
| `Truck_Wheel_FL` | `Wheels/FrontLeft` | (−0.87, 0.38, 1.80) | 0.76 m diameter (radius 0.38), 0.23 m wide, 400 tris |
| `Truck_Wheel_FR` | `Wheels/FrontRight` | (0.87, 0.38, 1.80) | |
| `Truck_Wheel_RL` | `Wheels/RearLeft` | (−0.87, 0.38, −1.80) | |
| `Truck_Wheel_RR` | `Wheels/RearRight` | (0.87, 0.38, −1.80) | |
| `Truck_Hatch` | new | (1.10, 2.15, −1.10) | Serving hatch with its struts, hinge along local Z; open at localEulerAngles.z = +80 (checked in Unity). 164 tris |
| `Truck_Collision` | `Colliders/BodyCollider` mesh | (0, 0, 0) | Convex hull, 122 tris, x −1.231…1.26, y 0…2.90 (roof units left outside), z −3.15…2.65. Convex MeshCollider, layer 12 as before |

Wheelbase 3.60 m (the example car: 2.70 m), track 1.74 m (1.50 m).

**Values to set on the prefab** (the example car's in brackets):

| Field | Value |
|---|---|
| Visual wheel transforms | the four hub positions above |
| `*_WheelController` positions | hub + (0, 0.164, 0), so y = 0.544 (0.470), keeping the example's suspension offset [inference] |
| WheelController radius / width | 0.38 / 0.23 (0.305 / 0.190) |
| Wheel `ShadowCaster` scale | (0.279, 0.358, 0.358), the example's ratio to wheel width and radius ((0.231, 0.287, 0.287)) |
| NWH mass module `dimensions` | (2.20, 2.90, 5.80) (1.99, 1.41, 4.58) |
| `NavMeshObstacle` | centre (0, 1.45, −0.25), extents (1.10, 1.45, 2.90) |
| `RefuelingPosition` | (−1.45, 0, −2.30): fuel cap on the left at (−1.10, 0.90, −2.30) ((−1.2, 0, −1.5)) |
| `LoadingPosition` | (0, 0, −3.50), 0.35 m behind the rear door, as on the car ((0, 0, −2.633)) |
| `Driverside` / `Passengerside` | (∓1.35, 0, 1.15), beside the cab doors [suggested; nudge in the editor] ((−1.243 / 1.007, 0, 0.117)) |
| `Spotlights` | (0, 1.78, 2.28): the car's height, 0.375 m behind the front as on the car [inference] ((0, 1.78, 1.93)) |
| `SmokeBrokenCar` | (0, 1.00, 2.20), under the hood [suggested] ((0, 0.85, 1.64)) |
| Spoiler box colliders | remove; the truck has no spoiler |
| Rigidbody mass and `centerOfMass` | not an art value; a step van is several times the car's 1,400 kg |

**Serving window** (for the window interaction later): opening at x = 1.10, y 1.20…2.15, z −2.00…−0.20, centre (1.10, 1.675, −1.10), at least 0.30 m deep. Stainless shelf on the outside, top at y 1.15, x 1.10…1.26.

**Material:** duplicate `M_HonzaCar.mat` (the game's `SH_Vehicle` graph) and swap each texture for its counterpart. Import settings as for the example: Read/Write on for the meshes (the damage component deforms them [inference, as in the tooling research]), `T_FoodTruck_N` as a normal map (not sRGB), the others as default textures.

| Slot | Texture | Contents |
|---|---|---|
| `_BaseColorMap`, `_MainTex` | `T_FoodTruck_D` | Base colour; alpha 1 on painted panels [inference: the example's alpha looks like a paint mask] |
| `_MaskMap` | `T_FoodTruck_MaskMap` | R metallic, G AO, B 0, A smoothness |
| `_NormalMap` | `T_FoodTruck_N` | Flat |
| `_EmissionTexture`, `_FrontLights`, `_BackLights`, `_BlinkerLeft`, `_BlinkerRight` | `T_FoodTruck_E` | Light colours |
| `_EmissionMask` | `T_FoodTruck_EM_1` | R headlights plus the amber marker and work lights, G tail/brake, B reverse |
| `_IndicatorsMask` | `T_FoodTruck_EM_2` | R right blinkers and side repeaters, G left ones |

The channel meanings come from mapping the example car's masks back onto its mesh: `EM_1` R lies on the headlights, G and B on the rear lights, `EM_2` R on the right side and G on the left [inference; not yet seen lit in game]. The dirt textures stay the example's (`SM_HonzaCar_Dirt*`); the second UV map is a non-overlapping copy of the first, for them.

## Status

| | Cart | Truck |
|---|---|---|
| Scorer | pass | pass |
| Unity 2022.3.62f2 import (empty project) | checked | checked |
| In the SDK project / prefab | pending (Milestone 8) | pending (Milestone 8) |
| In game | pending | pending |
| Look review (Peter) | pending | pending |
