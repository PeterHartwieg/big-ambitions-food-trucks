# Art: vehicles, equipment and props

Blender 5.2 sources and FBX exports for the cart, the truck, the counter equipment, the foods, the cash register and the commissary bay. `tools/build-mod.sh` copies each FBX into the SDK project as `<Name>_Model.fbx`, with its `_BC`, `_M` and `_N` maps: the cart to `Vehicles/CartArt`, the truck (plus `_E`, `_EM_1`, `_EM_2`) to `Vehicles/TruckArt`, equipment and icons to `Vehicles/EquipmentArt`, props to `Vehicles/PropArt` and the bay to `Commissary/BayArt`. Only `.meta` files, materials and prefabs are committed there. The suffix matters because the game finds a prefab by file name in any mod's bundle. The build editor scripts (`unity/BuildEditor/*Assets.cs`) check node names and positions against the tables below.

`cart/` and `truck/` hold the vehicles, `equipment/` and `products/` one folder per piece or food, `props/CashRegister/` the register, `commissary/` the bay set and `tools/` the scorer, look step, renderers, prompt generator and Sol runner. Each model folder has its `.blend`, `.fbx`, flat source texture, shipped maps, build script, `spec.json`, `score.json` and `renders/`. `start.blend` is an empty scene in metres with a 1.8 m wireframe person, `HumanRef_1.8m`.

## Pipeline

Models are clean low-poly shapes in flat colour blocks per part, as the game's props are. No text, logos, brand names, licence plate numbers or flags; abstract pictograms are fine. Each model has a `spec.json` that `score.py` checks: triangle budgets per object, origins and wheel hubs, hinge lines, applied transforms (rotation 0, scale 1), clean geometry (no loose parts, flipped normals or n-gons), a strictly convex collider built with `bmesh.ops.convex_hull`, one material, UVs (no overlap, inside 0 to 1, at least 40% fill), texture sizes, attachment points on the mesh, zones that must stay empty, and a clean FBX re-import with unchanged counts and origins.

```bash
blender -b art/cart/FoodCart.blend --python art/tools/score.py -- art/cart/spec.json
blender -b art/cart/FoodCart.blend --python art/tools/render_views.py -- art/cart/renders/review open
```

A `blender -b` started from Codex's shell sandbox crashes in Metal GPU detection (exit 139, a macOS crash dialog). Inside a Sol run, start the scorer from the running Blender with `execute_blender_code` as a `subprocess.run([...Blender, "-b", blend, "--python", "art/tools/score.py", "--", spec], stdin=subprocess.DEVNULL)`. Never edit the scorer or `spec.json` to make a model pass.

New equipment and food models are built by GPT 6.1 Sol in Codex, driving Blender through the Blender MCP connector. `art/tools/make_prop_prompts.py` is the only source of their prompt text: the `EQUIPMENT` and `PRODUCTS` tables hold each prop's label, height and description.

```bash
python3 art/tools/make_prop_prompts.py                                  # writes spec.json for every generated prop
python3 art/tools/make_prop_prompts.py --prompt art/equipment/Griddle  # prints one prop's prompt, writes nothing
art/tools/run_sol_prop.sh art/equipment/Griddle 9876 [images...]        # one Sol run on that Blender MCP port
```

A full rerun of the generator drops the hand-added `unity_front_axis` and `unity_operator_axis` fields from the equipment specs, so restore them afterwards (`git diff art/equipment`). The runner substitutes `{REPO}` and `{OUT}` in the prompt, attaches the images as references and writes `codex.log` and Sol's `report.md` to `build/art-runs/<Name>/` (ignored). Set `PROMPT_FILE` to run a custom prompt instead, for models outside the generator.

After a build, every model goes through the surface-look step, which also re-exports the FBX and runs the scorer:

```bash
art/tools/apply_look.sh art/equipment/Griddle             # one model; no arguments: every model
art/tools/apply_look.sh art/commissary/BaySign.spec.json  # one of several models in a folder
art/tools/render_icons.sh                                 # equipment icons and the mod's item icons
```

## Surface look

Peter, 11 October 2026, after playing: our models had "a very bright surface with very 'edgy' edges; other objects in the game feel like they have more texture". The game's 1.0 props were read from its `defaultlocalgroup_assets_prefabs` and `sharedtextures` bundles with UnityPy (hot dog heater, industrial grill, coffee machine, fryer, pizza oven, drinks fridges, cotton candy cart, van, delivery truck). Reference renders stay outside the repo.

| | The game's props | Ours before |
|---|---|---|
| Material | HDRP/Lit (or its own shader graphs) with base colour, mask and normal maps | Base colour only |
| Metallic | Steel is 1 (mask R) | 0 everywhere |
| Smoothness | Mask A: steel 0.31 to 0.50, plastics and paint 0.30 to 0.65, vehicle paint up to 0.95 | 0.5 everywhere |
| Ambient occlusion | Mask G: median 0.4 to 0.95, crevices 0 to 0.2 | None |
| Normal map | Baked from high-poly: 25 to 50% of texels tilted over 5 degrees | None |
| Mesh normals | No split normals under 60 degrees | Every corner split |
| Chamfers | 6 to 23% of edge length | 1 to 4% |
| Base colour | Steel albedo 0.65 to 0.85 sRGB with fine noise; machines at saturation 0.02 to 0.25 | Flat fills at 0.25 to 0.6 |
| Texture size | 256 px for props up to 1 m, 512 for carts and fryers, 1,024 for vehicles | Larger |

**Mask maps are imported as sRGB**, against HDRP's advice, because the game does. A built `Texture2D`'s `m_ColorSpace` carries the importer's sRGB flag: 1 on nearly all of the game's base colour maps, 0 on its normal maps, and 1 on 349 of its 352 mask maps (`T_HotdogHeater_Mask Map`, `IndustrialFryerMachine_M`, `Van_Mask`, `T_VanBig_M` and others). `game_look.py` writes our masks in the game's stored byte range, so the same decode renders them alike. Only G (AO) is affected: R is 0 or 1 and alpha is never decoded. `render_look.py` and the preview material decode the mask the same way. Our AO medians (stored 0.40 to 0.70) sit at the dark end of the game's range.

**What `game_look.py` does.** It works on the saved `.blend` and the build script's flat `T_<Name>_BaseColor.png` (the truck: `T_FoodTruck_D.png` and `_MaskMap.png`), which it leaves alone, so it can run again.

1. Normals: edges sharper than 60 degrees stay hard, shallower ones are smoothed, and a weighted-normal pass keeps big faces flat.
2. Bakes on the model's own UVs with Cycles: AO (distance a quarter of the model's size, 0.06 to 0.6 m), a tangent-space normal map of rounded edges (Bevel node, 1.8 texels, 2.5 to 15 mm), and an edge mask and two noise layers in object space, so nothing breaks at UV seams.
3. Sorts each texel into a class by its flat colour: steel (saturation under 0.16, value 0.40 to 0.92), dark (value under 0.22), white, paint, food, glass, fabric. A `look.json` beside the model overrides a palette colour, such as the cart's umbrella cloth as fabric.
4. Writes `T_<Name>_BC.png` (colours graded into the game's range, crevices darkened up to 30%, edges 22% lighter, fine variation), `T_<Name>_M.png` (R metallic, G 0.12 + 0.88 × AO, B 0, A class smoothness ±12% noise) and `T_<Name>_N.png`, at the source's size.
5. Re-exports the FBX with the flags under "Axes" and saves the `.blend` with a preview material.

| Class | steel | dark | white | paint | food | glass | fabric |
|---|---|---|---|---|---|---|---|
| Metallic | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| Smoothness | 0.42 | 0.30 | 0.45 | 0.40 | 0.35 | 0.80 | 0.20 |

The truck keeps its own mask's classes: metallic where it was, chrome down to 0.50, paint at most 0.72.

**Rules for new models.** Build with flat part colours and non-overlapping UVs (one atlas cell per face bakes best), then run `apply_look.sh`. Textures: 256 px for foods and small props, 512 for counter pieces and larger street props, 1,024 for the cart, 2,048 for the truck's base colour. Give boxes a one-segment chamfer of 2 to 6 mm and cylinders 8 or more sides. In Unity, `SurfaceLook.LitMaterial` makes the HDRP/Lit material from the three maps (`_MASKMAP`, `_NORMALMAP`, decals off), and the importers import the FBX normals and calculate Mikk tangents, which the normal maps were baked against. `render_look.py` renders day and night looks into each folder's `renders/look-*.png`.

**Street dirt.** The game's street dirt and litter are HDRP decal projectors on decal layer 0. `SurfaceLook.NoStreetDecals` sets every renderer on the cart, the truck, the display pieces and the register to rendering layer mask 1, as on the game's vehicles. The machine items (`ItemAssets`) keep Unity's default 257, as the game's furniture does.

## Axes

Blender is Z up. **Models face −Y in Blender, with their left side on +X.** Export with exactly these flags, with only the model's mesh objects selected:

```python
bpy.ops.export_scene.fbx(filepath=OUT + "/<Name>.fbx", use_selection=True, object_types={"MESH"},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_space_transform=True,
    mesh_smooth_type="OFF", use_triangles=True, use_tspace=True, bake_anim=False,
    add_leaf_bones=False, path_mode="RELATIVE", embed_textures=False)
```

Unity 2022.3.62f2 then imports every node with rotation 0 and scale 1 (checked with a marker FBX, 8 October 2026), so Unity = (−x, z, −y): Blender +X is Unity −X, +Y is −Z, +Z is +Y. Every position below is in Unity coordinates, relative to the prefab root, which is the FBX root.

## Food cart

Built by `cart/build_cart.py` (run inside Blender with `exec`). `CartAssets.BuildCart` fails the build if a node moves more than 5 mm from this table.

| Node | Position | Notes |
|---|---|---|
| `Cart_Body` | (0, 0, 0) | Box, counter, handle, fenders, feet, pole. x −0.50…0.50, y 0…2.30, z −0.77…0.70 |
| `Cart_Canopy` | (0, 0, 0) | Open umbrella, 1.60 m across, top 2.30, rim no lower than 2.00 so people fit under it. Shown while open for business |
| `Cart_Canopy_Furled` | (0, 0, 0) | Closed round the pole, y 1.44…2.27. Shown otherwise. The scorer keeps it out of the pusher zone and 0.30 m or more ahead of the push bar |
| `Cart_Wheel_L`, `Cart_Wheel_R` | (∓0.455, 0.25, 0) | 0.50 m diameter, 0.05 m wide, spin about local X (`CartParts`). Nothing else enters the wheels' volume |
| `Cart_Lid` | (0, 0.95, 0.025) | Steam-well lid, hinge along local X, open at x = −100. Stays closed under the front spot |
| `Cart_Collision` | (0, 0, 0) | Convex hull up to the counter. Unused: the root keeps its BoxCollider |

The body box is x ±0.40, z ±0.70, counter top at y 0.95. The pole stands at (0, 0.95, 0.60); between y 1.30 and 1.98 only the pole may be there. The root `BoxCollider` and `NavMeshObstacle` are centre (0, 0.6, 0), size (0.96, 1.2, 1.5); the umbrella stays outside, so people can walk under it. Import with Read/Write off, scale 1.

| Point | Position | Constraint |
|---|---|---|
| `LeftHandIK`, `RightHandIK` | (∓0.30, 1.16, −0.75) | The push bar's axis (35 mm bar) passes through both |
| `LoadingPosition`, `NavMeshTarget` | (0, 0, −1.40) | Nothing of the cart in x ±0.40, z −1.70…−0.85 up to 2.0 m, where the pusher walks. `playerOffsetWhenActive` −1.40 |
| `BoxPlaceholder` | (0, 0.5, 0), inside the body, no boxes | The game only switches its children on by cargo count, so an empty one shows nothing |
| `EquipmentFront` | (0, 0.963, 0.30) | Just above the closed lid. A 0.40 × 0.36 m piece at any angle stays within 0.27 m: z 0.03…0.57, clear of the pole |
| `EquipmentRear` | (0, 0.963, −0.30) | z −0.57…−0.03 at any angle, 0.18 m clear of the bar and hands. Pieces up to 0.70 m stay under the canopy rim |
| `EquipmentCooler` | (∓0.443, 0.525, −0.32) | Dedicated cooler, below |
| `Cash_Register` | (0.29, 0.95, 0.615), +90° about Y | Faces the cart's right (+X). While open with the queue on the left, `VehicleDisplays` mirrors it to (−0.29, 0.95, 0.615), −90°. 0.33 m from `EquipmentFront`, 0.19 m from the pole |

`VehicleDisplays` puts the first fitted piece on `EquipmentFront` and the second on `EquipmentRear`, turned 90° toward the worker side. The server stands at (∓0.85, 0, 0), the first customer at (±1.05, 0, 0), aligned with the midpoint of the fitted spots (Core `CartStance`). It shows `Cart_Canopy` while `StreetSales` reports the cart open and `Cart_Canopy_Furled` otherwise, checked every half second, and logs `Cart umbrella: cart <id> open, selling` or `furled, not selling` once per cart and state. The prefab starts furled.

## Equipment

Each piece fits 0.40 × 0.36 m, 0.40 to 0.70 m tall, origin at the bottom centre, front (Blender −Y) along Unity +Z. Each folder has `<Name>.fbx` with `<Name>_Body` and `<Name>_Collision`, the flat `T_<Name>_BaseColor.png`, the shipped maps and `Icon_<Name>.png`. The operating face points at the worker: +Z for controls, openings and handles, except PopcornMachine and IceCreamCabinet, whose operator doors are −Z. `spec.json` records `unity_front_axis` and `unity_operator_axis`; `CartStance.EquipmentYaw` applies them, and the register keeps the opposite yaw.

`EquipmentAssets` makes an HDRP/Lit material, a `<Name>_Display.prefab` (body only, no collider) and an icon sprite per model. The suffixes keep these apart from the mod's machine items (`griddle`, `corngrill`, `dumplingsteamer`), whose item prefabs `ItemAssets` builds in `unity/FoodTrucks/Items/Prefabs` from the same models. `data/economy.json`'s `display` field picks a piece's model by name; a piece without one shows nothing. The models that copy a game item were built with the game's item icon attached as a reference, kept outside the repo.

`StorageCabinet` is the Extra Storage piece and a game item (`foodtrucks:itemname_storagecabinet`). It is not in the prompt generator. Rebuild it with `blender -b --factory-startup --python art/equipment/StorageCabinet/build_storage_cabinet.py` from the repo root, then `apply_look.sh`. Front −Y, origin at the bottom centre. Its item icon is `unity/FoodTrucks/Items/Icons/storagecabinet.png`, from `render_icons.sh`.

**Icons.** `render_icons.sh` renders the game's item icon style: 512 × 512, transparent, front and left side seen from 26° left and 24° above, the model filling 86% of the frame (measured on `HotdogGrill` and `IndustrialGrill`). It writes `Icon_<Name>.png` into each equipment folder, loaded as `Assets/Mods/FoodTrucks/Vehicles/EquipmentArt/Icons/Icon_<Name>.png`, and the mod's item icons into `unity/FoodTrucks/Items/Icons`.

**Vehicle drawings.** `render_diagrams.sh` renders the BizMan vehicle page's layered drawings from one perspective camera per vehicle: `unity/FoodTrucks/UI/VehicleDiagram_Truck.png` (the open hatch from the pavement, register left out) and `VehicleDiagram_Cart.png` (right side, umbrella furled), both 2400 × 800 with every place empty, then one `VehicleLayer_<Kind>_<place>_<Model>.png` per place and display model, rendered with the vehicle as holdout and cropped. `data/vehicle-diagrams.json` (schema 2) gives the background, `drawOrder`, and per place a hotspot and each item's layer with its offset. `diagram_metas.py` gives new PNGs a Sprite meta and keeps existing GUIDs. Rerun after moving a place, adding a display model or changing a model; the cameras and place positions are in the script. `FT_DIAGRAM_COOLER_ONLY=1` on the Python renderer reuses the backgrounds and general layers and renders only the two cooler layers.

## Food truck

A compact American step van converted into a food truck: short sloped hood narrower than the box, twin square headlights beside an upright grille, a two-pane windscreen raked about 12°, a rounded cap over it with amber marker lights, rounded roof edges and corners, round wheel openings, a rub rail, a stainless diamond-plate lower body, single rear wheels. Cream with a teal stripe. The serving window and its top-hinged awning are on the curb side (Unity +X). It replaces the example car's meshes inside the copied `TurboHonza` prefab (`ModAssets.GenerateTruck`, `TruckAssets`).

Rebuild with `blender -b art/truck/FoodTruck.blend --python art/truck/build_truck.py` from the repo root, then `blender -b art/truck/FoodTruck.blend --python art/truck/review_interior.py`, which places the real equipment and stand-in crew and prints CLEAR or HIT lines. Keep the `.blend` under 20 MB by not packing images. Every visible object has two UV maps: the second is a non-overlapping copy for the example's dirt textures (`SM_HonzaCar_Dirt*`).

| Node | Replaces | Position | Notes |
|---|---|---|---|
| `Truck_Body` | `CarHolder/Body/CheapSaloon` | (0, 0, 0) | Body x ±1.10 (mirrors and shelf to −1.25 / +1.26), y 0.20…3.10 (cab 2.90, compartment 3.10, ceiling 2.96, roof units to 3.25), z −3.15…2.65 |
| `Truck_Wheel_FL`, `_FR` | `Wheels/FrontLeft`, `FrontRight` | (∓0.87, 0.38, 1.80) | 0.76 m diameter, 0.23 m wide. No body within 0.44 m of a hub along Z below y 0.84, x 0.745…0.995 |
| `Truck_Wheel_RL`, `_RR` | `Wheels/RearLeft`, `RearRight` | (∓0.87, 0.38, −1.80) | |
| `Truck_Hatch` | new | (1.10, 2.82, −1.10) | Hinge along local Z. Open at localEulerAngles.z = +80 (`TruckHatch`), which swings it out and up over the curb. Blender: +80° about local Y |
| `Truck_Collision` | `Colliders/BodyCollider` mesh | (0, 0, 0) | Convex hull of body and hatch, y 0.30…3.10, roof units outside. Layer 12. No wheels and nothing below 0.30 since 13 October 2026, as the game's trucks: with wheels it scraped kerbs and played NWH's crash sound. Hull planes need 2.6° between them, or a sliver near the rear bumper breaks strict convexity |

Prefab values (the example car's in brackets): WheelController at hub + (0, 0.164, 0), radius / width 0.38 / 0.23 (0.305 / 0.190); wheel `ShadowCaster` scale (0.279, 0.358, 0.358); NWH `dimensions` and `NavMeshObstacle` size (2.20, 3.10, 5.80), obstacle centre (0, 1.55, −0.25); `RefuelingPosition` (−1.45, 0, −2.30) by the fuel cap on the left; `LoadingPosition` (0, 0, −3.50); `Driverside` / `Passengerside` (∓1.35, 0, 1.15); `Spotlights` (0, 1.78, 2.28); `SmokeBrokenCar` (0, 1.00, 2.20). The spoiler box colliders go.

**Driving** (`TruckAssets`, 10 October 2026, after 0.9.7's truck drove like a sports car). The game's 1.0 vehicle prefabs, read with UnityPy, give every car and truck the same 1,400 kg rigidbody with no drag, the same gears (5.748, 3.786, 2.778, 2.108), a 10,000 rpm limiter, and the same springs and dampers on vans and trucks (5,000 N, 5,000, load rating 7,000). They differ in power, power curve, final drive and wheel size, so top speed is the limiter in top gear. The NWH speed limiter is off on all of them. Nothing in the 1.0 game reads `VehicleType.enginePower` or `brakeForce`; `maxSpeed` feeds the showroom and the bridge skip's fuel.

| | Power (kW) | Turbo | Final drive | Wheel radius | Top (limiter) | Brakes (N m) | Centre of mass |
|---|---|---|---|---|---|---|---|
| HonzaMimic (starter car) | 45 | no | 8.51 | 0.305 | 64 km/h | 2,500 | (0, 0.3, 0) |
| UMCDesert, MersaidiDash (vans) | 55 | no | 11.14 | 0.403 | 65 km/h | 2,500 | (0, 0.6, 0.09 / 0.8) |
| VordV150 (pickup) | 130 | no | 7.62 | 0.403 | 95 km/h | 4,000 | (0, 0.3, 0) |
| DeliveryTruck (box truck) | 46 | x1.2 | 15.25 | 0.43 | 50 km/h | 2,500 | (0, 0.4, 0.68) |
| FreightTruckT1 (semi) | 35 | x1.2 | 15.25 | 0.542 | 64 km/h | 2,500 | (0, 0.4, 0.68) |
| Food truck (1,600 kg) | 40 | no | 13.5 | 0.38 | 50 km/h | 2,500 | (0, 0.4, −0.2) |

The food truck takes the DeliveryTruck's suspension, power curve, brakes and centre-of-mass height, and its top speed through a final drive for our smaller wheels. Top speeds are the limiter converted to wheel speed [code reading, unverified in game]. Mass is 1,600 kg (Peter, 13 October 2026: "slightly heavier"), so it accelerates about 12% slower than at 1,400 kg. Springs and dampers scale by 1,600 / 1,400: unscaled, each wheel's 3,920 N load would sit the truck about 6 cm lower, moving the hatch and crew, and hard braking would bottom out the front. Brakes stay at 2,500 N m per wheel, which already exceeds tyre grip at either mass. Status: pending in game (CHECKS, "Truck driving").

**Serving window**: opening at x 1.10, y 1.80…2.82, z −2.00…−0.20, at least 0.30 m deep, with a stainless shelf outside, top y 1.75. A 1.8 m person on the floor (y 0.94) has eyes at 2.62 and head top at 2.74, inside the opening.

**Interior**: the cooking line is on the back wall, facing the cook, as in real step vans (Peter, 10 October 2026). Inside wall to wall is x −1.02…1.02.

| Part | Unity position | Notes |
|---|---|---|
| Serving counter | x 0.78…1.02, z −2.00…−0.20, top y 1.80 | Meets the sill; nothing in the FBX stands on it. `Cash_Register` at (0.90, 1.80, −0.82), −90° about Y facing the server, scale 2.2, clear of crew, frame, panes and hatch |
| Sliding panes | x 0.955…1.01, z −2.935…−2.00 | Parked in a pocket past the opening's rear end |
| Hand sink | x 0.43…1.02, z −0.12…0.88, top 1.75 | Cab end of the hatch side |
| Aisle | x −0.40…0.78 | Player and crew stand at x 0.59 (`WindowCrew`): three 0.30 m bodies in x 0.44…0.74, z −2.00…−0.20, and waiting places behind them touch nothing |
| Back counter | x −1.00…−0.40, z −2.12…0.88, top 1.75 | Backsplash to y 2.64 |
| Hood | x −1.02…−0.36, z −2.10…−0.04, y 2.64…2.92 | |
| Fridge | x −1.00…−0.55, z −2.92…−2.15, to y 2.70 | By the rear doors |

Four equipment spots on the back counter at scale 1, each +90° about Y so a piece faces the cook and the hatch, 0.45 m apart and filled from the middle out: `EquipmentLine2` (−0.62, 1.75, −0.875) first, `EquipmentLine3` (−1.325) second, `EquipmentLine1` (−0.425) third, `EquipmentLine4` (−1.775) fourth. A piece covers x −0.44…−0.80; the tallest stays under the hood's 2.64.

**Material.** A copy of `M_HonzaCar.mat` (the game's `SH_Vehicle` graph) with our textures. Read/Write on for the meshes, since the damage component deforms them. `T_FoodTruck_N` imports as a normal map, the mask as sRGB. `TruckAssets` caps `_M` and `_N` at 1,024 px in the importer, as the game's vehicles, while the base colour stays 2,048 for its lettering; at 2,048 the bundle was 25 MB.

Slots: `_BaseColorMap` and `_MainTex` take `T_FoodTruck_BC` (alpha is the paint mask); `_MaskMap` takes `T_FoodTruck_M` (R metallic, G AO, B 0, A smoothness); `_NormalMap` takes `T_FoodTruck_N`; `_EmissionTexture`, `_FrontLights`, `_BackLights`, `_BlinkerLeft` and `_BlinkerRight` take `T_FoodTruck_E` (light colours); `_EmissionMask` takes `T_FoodTruck_EM_1` (R headlights plus the marker and work lights, which light together, G tail and brake, B reverse); `_IndicatorsMask` takes `T_FoodTruck_EM_2` (R right blinkers and repeaters, G left). The channel meanings come from mapping the example car's masks onto its mesh [inference; not yet seen lit in game]. `build_truck.py` keeps each light's faces on their own UV island.

**Paint mask.** `SH_Vehicle` sets the base colour to `lerp(texture, Tint, smoothstep(_Min, _Max, alpha))`, blends towards `FresnelColor` by `smoothstep × Fresnel(power) × AO`, and multiplies smoothness by `SmoothnessBoost` (1.1) on the same mask. `CarFeatures.SetColor` sets Tint, rim colour and rim power on every renderer in `bodyMeshes` (body, wheels, hatch). The tint replaces the texture outright, so painted texels lose baked darkening, seams and grime; the mask's AO, the normal map and the game's dirt still apply. `build_truck.py` writes alpha 1 per palette tag and the material keeps the example's `_Min` 0, `_Max` 0.5.

Alpha is 1 on the `paint` and `seam` tags: box, cab, roof, hood, front fenders, grille surround, rear door, generator hatch, both faces of the serving hatch and the side panel joins. It is 0 on paint faces looking into the compartment (`faces_compartment`), so the kitchen stays cream, and 0 on every other tag: teal stripe, diamond plate, steel, rims, tanks, trims, rubber, plastic, glass, cab interior, lights and the menu board.

Alpha 1 covers 18% of the atlas. Island padding keeps its own island's alpha; cells are 16 px aligned, so mips blend only at island edges. Zeroing the paint padding was tried and dropped: bilinear samples at panel corners read 0.25, showing cream flecks on a dark truck. CHECKS C2 looks from about 30 m.

Cream shows only on a truck without a colour name, the debug spawn. There Tint is the graded cream (sRGB 186, 162, 119), the rim × 1.3, rim power 2.04. A bought truck always has a colour: the dealer's picker starts on `GlobalReferences.vehicleColors[0]`. The game's 1.0 `VehicleColor` assets (`sharedassets1.assets`, path IDs 6799 to 6810) are Black, Blue, DarkBlue, DarkRed, Green, Grey, Pink, Purple, Red, Silver, White and Yellow; the order of `vehicleColors` was not in the copied files. None of them is close to the cream: White has its lightness without warmth, Yellow its hue at three times the saturation. Most have a rim darker than the tint and power 1, unlike our default's lighter rim at 2.04.

**Decals off.** The material has `_DISABLE_DECALS` and `_SupportDecals` 0, as the game's vehicle materials. The SDK compiles four-target decal variants (`DECALS_4RT`) while the game renders three; a four-target variant reads a decal buffer the game never fills and left the truck almost black in shade. Every material we ship refuses decals, so the build keeps the SDK setting. Ours keep `_ReceivesSSR` 1, unlike the game's, since matching needs a stencil change too.

## Commissary bay

The loading bay's street furniture, built headless without MCP:

```bash
blender -b --factory-startup --python art/commissary/build_commissary.py
art/tools/apply_look.sh art/commissary/*.spec.json
blender -b --factory-startup --python art/commissary/render_review.py
```

The build script writes the final paint and lettering and `BayCommissary.blend` with the whole set. The sign and each prop then get the surface look like every other model: the script lays each out on its own atlas, bakes the procedural part textures into `T_<Name>_BaseColor.png`, and saves `<Name>.blend` and `<Name>.spec.json`. The spec's `look` entry sets the classes (wood and cardboard as fabric, produce as food, the dumpster's paint as dark). The sign's spec lists one variant per commissary, graded into `T_BaySign_<id>_BC.png`. Atlases are 512 px, the hand truck's 256; the roll cage's spec asks for 30% UV fill. The lettering uses Blender's Inter font (SIL Open Font License), and the sign's names come from `data/commissaries.json` and `en.json`, so a renamed commissary needs a rebuild.

Files: `T_BayPaint_BaseColor.png` (worn road paint, 188 sRGB like the game's decal lines, tiling every 2 m, 512²); `BayLettering.fbx` (LOADING ZONE as flat paint, 1 m cap height, 8.04 m long, origin in the middle, reading along Unity +x with tops to +z); `BaySign.fbx` (a 3.0 m pole with a 0.40 × 0.80 m plate from 1.90 m, facing Unity +z and readable from both sides, origin at the pole's foot); and `BayPalletStack`, `BayHandTruck`, `BayRollCage` and `BayDumpster`, origin at the bottom centre, front Unity +z.

`CommissaryAssets` makes the materials and `<Name>_Display.prefab`s. The sign and props use `SurfaceLook.LitMaterial` (`M_BaySign_<id>` per commissary, which `BayProps` swaps onto the plate); the paint keeps a flat road-line material with decals off. Every prefab gets `SurfaceLook.NoStreetDecals`, and `BayMarkers` sets its outline and copied game meshes to the same layer. Each prop prefab has a box collider and a carving NavMeshObstacle. In game the pallet and hand truck use the game's `DeliveryPallet` and `Vehicles/PlayerVehicles/HandTruck` meshes when those load.

## Cash register

`props/CashRegister/`: a compact register after the game's `ConcessionsStandRegister`: charcoal drawer, lighter deck, a screen on a short post, a card terminal, a receipt slot. 0.150 × 0.133 m, 0.177 m tall, 256 px maps, front −Y (Unity +Z), origin at the bottom centre. Built by `build_cash_register.py` (headless, `--factory-startup`), then `apply_look.sh`. `PropAssets` adds it to both vehicles as a plain mesh child `Cash_Register` with no collider, in the vehicle's `renderers`. The truck scales it to 2.2 (0.330 × 0.293 × 0.390 m), keeping the tall screen's proportions. Positions are in the cart and truck tables.

## Dedicated customer cooler

The first fitted cooler goes on its own `EquipmentCooler` anchor; extra fridges use ordinary `DrinksCooler` in general slots. `build_customer_coolers.py` derives two display-only models from `DrinksCooler`, keeping its door, stock artwork and UVs. They are not purchasable items. The cart variant is the shallow front of a cabinet inside the cart body, not a countertop appliance.

| Vehicle | Anchor | Display | Orientation |
|---|---|---|---|
| Cart | (∓0.443, 0.525, −0.32) | `CartDrinksCooler`, 0.392 × 0.0704 × 0.3836 m | Worker side, opposite the queue; left when closed |
| Truck | (0.875, 1.80, −1.80) | `TruckDrinksCooler`, 0.392 × 0.1584 × 0.548 m | +90° about Y, toward customers |

```bash
blender -b --factory-startup --python art/tools/build_customer_coolers.py
art/tools/apply_look.sh art/equipment/CartDrinksCooler art/equipment/TruckDrinksCooler
```

`review_customer_coolers.py` checks the imported meshes and renders both cart sides and the truck with its register into `build/cooler-slot/`. On the cart, mounting lips may meet the cabinet skin but the liner and drinks stay clear, inside the collider width, above the wheel and fender and below the worktop. On the truck the cooler stands fully on the serving counter, clears the aisle and register, and clears the hatch from 0 to 80° in 10° steps. These are Blender checks, not gameplay evidence.
