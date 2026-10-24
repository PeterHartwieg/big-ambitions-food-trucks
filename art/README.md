# Art: cart and truck models

The final food cart and food truck for Milestone 8, built in Blender 5.2 and exported as FBX for the SDK project. Until Milestone 8 the game uses the placeholders built by `unity/BuildEditor/CartAssets.cs` (cart) and the SDK's example car (truck). Nothing here is referenced by the build yet.

| Folder | Contents |
|---|---|
| `cart/` | `FoodCart.blend`, `FoodCart.fbx`, `T_FoodCart_BaseColor.png` (flat source) and `T_FoodCart_BC/_M/_N.png` (shipped), `look.json`, `spec.json`, `prompt.md`, `build_cart.py`, `score.json`, `renders/`, `run-log.md` |
| `truck/` | `FoodTruck.blend`, `FoodTruck.fbx`, the `T_FoodTruck_*.png` textures, the same support files |
| `equipment/`, `products/` | One folder per compact piece or food, laid out like `cart/`; each equipment folder also has `Icon_<Name>.png` |
| `props/` | Fixed props on the vehicles: `CashRegister/` |
| `commissary/` | The commissary bay's road paint, lettering, sign and props (below) |
| `tools/` | `score.py` (checks a model against its `spec.json`), `game_look.py` and `apply_look.sh` (surface look, below), `render_look.py` (day and night look renders), `render_icon.py` and `render_icons.sh` (item icons), `render_views.py` (four fixed review renders), `render_diagrams.py`, `render_diagrams.sh` and `diagram_metas.py` (the BizMan vehicle page's layered vehicle drawings) |
| `start.blend` | The empty start scene: metres and a 1.8 m wireframe person, `HumanRef_1.8m` |

## Pipeline

The models are built by GPT 6.1 Sol in Codex, driving Blender through the Blender Lab MCP connector. It won a blind benchmark against Opus 5.5 on a grill and a cart (October 2026). Each model has a `prompt.md` with fixed names, origins and budgets, and a `spec.json` that `score.py` checks; the prompt tells the model to run the scorer until it passes.

```bash
blender -b art/cart/FoodCart.blend --python art/tools/score.py -- art/cart/spec.json
blender -b art/cart/FoodCart.blend --python art/tools/render_views.py -- art/cart/renders/review open
```

The scorer checks triangle budgets per object, origins and wheel hubs, hinge lines, applied transforms, clean geometry, a strictly convex collider, one material, UVs (no overlap, inside 0–1, at least 40% fill), texture sizes, attachment points that must lie on the mesh, zones that must stay empty, and a clean FBX re-import with unchanged counts and origins.

After a build script, every model goes through the surface-look step, which also re-exports the FBX and runs the scorer:

```bash
art/tools/apply_look.sh art/equipment/Griddle      # one model; no arguments: every model, the commissary's included
art/tools/apply_look.sh art/commissary/BaySign.spec.json   # one of several models in a folder, named by its spec
art/tools/render_icons.sh                           # equipment icons and the mod's item icons
```

## Surface look

Peter, 11 October 2026, after playing: our models had "a very bright surface with very 'edgy' edges; other objects in the game feel like they have more texture". The game's 1.0 props were read from its `defaultlocalgroup_assets_prefabs` and `sharedtextures` bundles with UnityPy (copies outside the repo; reference renders in the gitignored `build/game-ref/look/`). The comparable items were the hot dog heater, industrial grill, coffee machine, fryer, pizza oven, drinks fridges, cotton candy cart, van and delivery truck.

| | The game's props | Ours before |
|---|---|---|
| Material | HDRP/Lit (or its own shader graphs) with base colour, mask and normal maps on every prop | HDRP/Lit with a base colour map only |
| Metallic | Steel is metallic 1 (mask R) | 0 everywhere |
| Smoothness | Per texel from mask A: steel 0.31 to 0.50, plastics and paint 0.30 to 0.65, vehicle paint up to 0.95 | 0.5 everywhere |
| Ambient occlusion | Baked into mask G: median 0.4 to 0.95, crevices down to 0 to 0.2 | None |
| Normal map | Baked from high-poly: 25 to 50% of texels tilted more than 5 degrees (rounded edges, panel lines) | None (the truck's was flat) |
| Mesh normals | Smooth across shallow angles: no split normals under 60 degrees, 20 to 46% of corners split | Flat-shaded: every corner split, cylinders faceted |
| Chamfers | 6 to 23% of edge length in short bevel edges | 1 to 4% |
| Base colour | Steel albedo 0.65 to 0.85 (sRGB) with fine noise or brushing; colours muted (saturation 0.02 to 0.25 on machines) | Flat fills, colours at saturation 0.25 to 0.6 |
| Texture size | 256 px for props up to 1 m, 512 for carts and fryers, 1,024 for vehicles | 512 for 0.4 m pieces, 1,024 cart, 2,048 truck |
| Triangles | Hot dog heater 3,400 (0.9 m), industrial grill 1,060, cotton candy cart 3,850, van 4,600, delivery truck 5,800 | In range |

**Mask maps are imported as sRGB**, against HDRP's advice to import them linear, because the game does. Read again from the 1.0 `defaultlocalgroup_assets_prefabs` bundle with UnityPy on 11 October 2026: a built `Texture2D` has no sRGB texture format (BC7 and DXT5 are the same format either way); its `m_ColorSpace` carries the importer's sRGB flag. The game's base colour maps have 1 (394 of 396), its normal maps 0 (356 of 358), so 1 means sRGB, and 349 of its 352 mask maps (named `_M`, `_Mask`, `MaskMap` or `Mask Map`, dirt and emission masks left out) have 1, among them `T_HotdogHeater_Mask Map`, `T_IndustrialGrill_Mask Map`, `IndustrialFryerMachine_M`, `PizzaOven_M`, `T_CottonCandyCart_M`, `Van_Mask` and `T_VanBig_M` (the exceptions are `Wheel_MaskMap`, `T_SwimmingPoolProps_M` and `Fries_Fries_MaskMap`). The values in the table above are the game's stored bytes, and `game_look.py` writes ours in the same range, so the same sRGB decode renders them alike. Only G (AO) is decoded: R is 0 or 1 and alpha is never sRGB-decoded, so metallic and smoothness are as stored. `render_look.py` and the preview material decode the mask the same way. Our AO medians (stored 0.40 to 0.70 on the equipment) sit at the low end of the game's 0.40 to 1.0, so ours are a little darker in indirect light, not brighter; no change to the AO formula was needed for the colour space. The truck's G is 0.12 (AO 0) on large parts of its atlas that don't show dark from outside, probably faces inside the body; worth a look if its inside renders too dark in game. Its vehicles render with `renderingLayerMask` 1 and its props in buildings with 257 (bit 8 is HDRP's default decal layer); see "Street dirt" below.

**What `game_look.py` does.** It works on the saved `.blend` and the build script's flat `T_<Name>_BaseColor.png` (the truck: `T_FoodTruck_D.png` and `_MaskMap.png`), which it leaves alone, so it can run again.

1. Normals: edges sharper than 60 degrees stay hard; shallower ones (cylinder facets, the box chamfers the build scripts already make) are smoothed, and a face-area weighted-normal pass keeps big faces flat.
2. Bakes on the model's own UVs with Cycles: ambient occlusion (distance a quarter of the model's size, 0.06 to 0.6 m), a tangent-space normal map of rounded edges (Bevel node, radius 1.8 texels, 2.5 to 15 mm), and an edge mask and two noise layers in object space, so nothing breaks at UV seams.
3. Sorts each texel into a class by its flat colour: steel (saturation under 0.16, value 0.40 to 0.92), dark plastic and rubber (value under 0.22), white, coloured paint, food (the products), glass and fabric. A `look.json` beside the model overrides a palette colour, e.g. the cart's umbrella cloth is fabric and its blue-grey trims are steel.
4. Writes `T_<Name>_BC.png` (colours graded into the game's range: paint value × 0.86 + 0.03 and saturation × 0.8, steel 0.52 + 0.40 × value at saturation × 0.35; occlusion darkens crevices by up to 30%; ±3.5% coarse and ±2.5% fine variation; edges 22% lighter), `T_<Name>_M.png` (R metallic; G 0.12 + 0.88 × AO; B 0; A the class smoothness ±12% noise, less in crevices and on edges) and `T_<Name>_N.png`, all at the source's size.
5. Re-exports the FBX with the prompts' export settings, saves the `.blend` with a preview material using the three maps, and `apply_look.sh` runs the scorer.

| Class | Metallic | Smoothness (before noise) |
|---|---|---|
| steel | 1 | 0.42 |
| dark | 0 | 0.30 |
| white | 0 | 0.45 |
| paint | 0 | 0.40 |
| food | 0 | 0.35 |
| glass | 0 | 0.80 |
| fabric | 0 | 0.20 |

The truck keeps its own mask's classes: metallic where it was, chrome down to 0.50, paint at most 0.72, glass as it was.

**Rules for new models.** Build with flat part colours and non-overlapping UVs as before (every face its own atlas cell works best for baking), then run `apply_look.sh`. Keep textures at 256 px for foods and small props, 512 for counter pieces and the larger street props, 1,024 for the cart and 2,048 for the truck's base colour; Unity imports the truck's mask and normal map at 1,024 (below). Give boxes a one-segment chamfer of 2 to 6 mm and cylinders 8 or more sides; the look step rounds the rest. Steel is a light, low-saturation grey in the palette; anything else that should shine or stay matte goes in `look.json`. Unity side: `SurfaceLook.LitMaterial` makes the HDRP/Lit material from the three maps (keywords `_MASKMAP`, `_NORMALMAP`, remaps at 0 to 1, decals off), and the model importers import the FBX normals and calculate Mikk tangents, which the normal maps were baked against.

**Renders.** `tools/render_look.py` renders models on a pavement plane under a sky and sun (day) or two warm street lamps (night). Ours-only renders are in each folder's `renders/look-*.png`; the side-by-side comparisons with the game's items stay outside the repo.

**Street dirt.** The game's street dirt and litter are HDRP decal projectors on decal layer 0. `SurfaceLook.NoStreetDecals` sets every renderer of the cart, the truck, the display pieces and the register to rendering layer mask 1, as the game's vehicles have, on top of the materials' `_DISABLE_DECALS`. The machine items (`ItemAssets`) keep Unity's default 257, as the game's own furniture does.

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
| `Cart_Body` | (0, 0, 0) | Box, counter, handle, fenders, feet, umbrella pole. 2,180 tris. Extents x −0.50…0.50, y 0…2.30, z −0.77…0.70 |
| `Cart_Canopy` | (0, 0, 0) | The open umbrella canopy and its ribs, 768 tris, x −0.80…0.80, y 2.02…2.29, z −0.20…1.40. Shown only while the cart is open for business |
| `Cart_Canopy_Furled` | (0, 0, 0) | The canopy closed round the pole, strapped, 288 tris, within 0.075 m of the pole, y 1.44…2.27. Shown while the cart is closed, pushed, parked or loaded. The scorer keeps it out of the pusher zone and 0.30 m or more ahead of the push bar |
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
| `Cash_Register` | (0.29, 0.95, 0.615), turned +90° about Y | The cash register (`props/CashRegister`), facing the cart's right (+X, the door side). While the cart is open with its queue on the left, `VehicleDisplays` mirrors it to (−0.29, 0.95, 0.615), turned −90°. The server stands across the counter from it at (∓0.85, 0, 0.615) and the first customer at (±1.05, 0, 0.615) (Core `CartStance`). Footprint x 0.22…0.36, z 0.54…0.69, top 1.13: 0.33 m from `EquipmentFront` (a piece reaches 0.27 m), 0.19 m from the pole, 1.3 m from the hands. Always shown |

**Umbrella.** `VehicleDisplays` shows `Cart_Canopy` while the cart is open (`StreetSales` `IsOpen`) and `Cart_Canopy_Furled` otherwise, checked every half second, so opening, closing and loading a save all switch it. It logs `Cart umbrella: cart <id> open, selling` or `furled, not selling` once per cart and state per session. The prefab starts furled.

**Swap steps:**

1. Import `FoodCart.fbx` with Read/Write off, scale 1, normals imported and Mikk tangents.
2. One HDRP/Lit material from `T_FoodCart_BC`, `_M` and `_N` (`SurfaceLook.LitMaterial`) for all visible meshes.
3. In `BuildCart`, instead of the seven `Box(...)` calls, instantiate the visible mesh nodes under the root on layer `Vehicles` and pass their renderers, and the register's, to `renderers`. Leave out `Cart_Collision`, or keep it disabled for later.
4. Widen the root `BoxCollider` and the `NavMeshObstacle` to cover the wheels: centre (0, 0.6, 0), size (0.96, 1.2, 1.5). The umbrella stays outside the collider, so people can walk under it.
5. Optional: spin the wheels about local X with the rolling speed (done, `CartParts`). The lid no longer opens: equipment stands over it.

## Equipment

Sixteen compact pieces for the cart's counter spots, one folder each in `equipment/` (`<Name>.fbx` with `<Name>_Body` and `<Name>_Collision`, the flat source `T_<Name>_BaseColor.png`, the shipped maps `T_<Name>_BC/_M/_N.png` and the icon `Icon_<Name>.png`). Each fits 0.40 × 0.36 m, origin at the bottom centre, front (Blender −Y) facing the customer, which is +Z in Unity. Heights 0.40 to 0.70 m.

`tools/build-mod.sh` copies each FBX (as `<Name>_Model.fbx`) and its three maps into `unity/FoodTrucks/Vehicles/EquipmentArt/` and its icon into `EquipmentArt/Icons/`, and `EquipmentAssets` makes an HDRP/Lit material, a `<Name>_Display.prefab` (body mesh only, no collider) and an icon sprite per model there. Only the `.meta` files, materials and prefabs are committed. The suffixes keep these files from sharing a name with the mod's machine items (`griddle`, `corngrill`, `dumplingsteamer`): the game finds an item's prefab by file name in any mod's bundle. The dumpling steamer, griddle and corn grill are also game items; `ItemAssets` builds their item prefabs in `unity/FoodTrucks/Items/Prefabs` from the same models. `art/tools/render_icons.sh` renders the icons in the game's item icon style (512 × 512, transparent, the front and the left side seen from 26° to the left and 24° above, the model filling 86% of the frame, measured on the game's `HotdogGrill` and `IndustrialGrill` icons), with the surface maps. It writes `Icon_<Name>.png` into every equipment folder (the order lists' pictures, loaded from the bundle as `Assets/Mods/FoodTrucks/Vehicles/EquipmentArt/Icons/Icon_<Name>.png`; the prefix keeps them apart from every prefab and item name) and the mod's eight item icons into `unity/FoodTrucks/Items/Icons`, which are committed. `data/economy.json`'s `display` field picks a piece's model by name: the coffee machine shows `CoffeeStation`, the cooler `DrinksCooler`, the hot dog grill `HotDogRoller`, the burger station `BurgerStation`, the ice cream counter `IceCreamCabinet`, the pizza oven `PizzaOven` and the display case `PastryCase`. The new foods' pieces show `PopcornMachine`, `SlushieMachine`, `DumplingSteamer`, `Griddle` and `CornGrill`; `CottonCandyMachine`, `KabobGrill` and `MerchRack` have no piece. The last five of the first fifteen were modelled with the game's own item icons attached as references (kept outside the repo), so they match the game's look at cart size. `StorageCabinet`, a steel cabinet with two doors and an open shelf of cartons, is the Extra Storage piece and also a game item (`foodtrucks:itemname_storagecabinet`, plan "Storage per piece"). A piece without a `display` shows nothing.

**Vehicle drawings.** `art/tools/render_diagrams.sh` renders the BizMan vehicle page's drawings in layers, all from one perspective camera per vehicle: `unity/FoodTrucks/UI/VehicleDiagram_Truck.png` (2400 × 800, framed as 1200 × 700 and then widened at the same vertical view to fill the BizMan Menu box: the open hatch seen from the pavement nearly square on, 85 mm lens, framed on the window with the menu sign, awning and roofline around it so a piece is about 140 to 155 px wide; the cash register is left out because in game it stands in front of the middle places) and `VehicleDiagram_Cart.png` (2400 × 800, framed as 900 × 700 and widened the same way, so the cart stands centred with transparent sides: the whole cart from its right side and a little from the front, umbrella furled), with every place empty; then one `VehicleLayer_<Kind>_<place>_<Model>.png` per place and display model (`economy.json`'s `display`), rendered with the vehicle (and the cart's register) as holdouts so the window frame and walls hide what they hide in the full render, and cropped to its pixels. `data/vehicle-diagrams.json` (schema 2) gives per kind the background, the places from back to front (`drawOrder`) and per place (in the order `VehicleDisplays` fills them) a hotspot and, per game item, its layer file with its offset in the full image. `art/tools/diagram_metas.py` gives each new PNG a Sprite meta (4096 for the 2400 px backgrounds, 2048 for the layers, bundle `foodtrucks`) and keeps existing GUIDs. The script also writes `build/diagram-check.png` with a few places filled. Run it again after moving a place, adding a display model or changing a model; the places' positions and the cameras are in the script.

On the cart, `VehicleDisplays` puts the first fitted piece on `EquipmentFront` and the second on `EquipmentRear` (each keeps its spot while fitted), turned 90° to face the side the queue forms on while the cart is open, and the cart's right side otherwise.

## Food truck

Replaces the example car's meshes inside the copied `TurboHonza` prefab (`ModAssets.GenerateTruck`). This is version 5: version 3's step-van shape (detail and shaping passes, modelled after the reference photos), version 4's taller food compartment and raised serving window, and since 10 October 2026 the cooking line on the back wall (below). `truck/v1/` and `truck/v2/` keep the earlier versions.

**Meshes** (`truck/FoodTruck.fbx`, all nodes at rotation 0, scale 1, two UV channels):

| Node | Replaces | Position | Notes |
|---|---|---|---|
| `Truck_Body` | `CarHolder/Body/CheapSaloon` | (0, 0, 0) | 6,328 tris. Body x ±1.10 (mirrors and shelf to −1.25 / +1.26), y 0.20…3.10 (cab roof 2.90, compartment roof 3.10, ceiling inside 2.96) with roof units to 3.25, z −3.15…2.65 (5.80 m) |
| `Truck_Wheel_FL` | `Wheels/FrontLeft` | (−0.87, 0.38, 1.80) | 0.76 m diameter (radius 0.38), 0.23 m wide, 400 tris |
| `Truck_Wheel_FR` | `Wheels/FrontRight` | (0.87, 0.38, 1.80) | |
| `Truck_Wheel_RL` | `Wheels/RearLeft` | (−0.87, 0.38, −1.80) | |
| `Truck_Wheel_RR` | `Wheels/RearRight` | (0.87, 0.38, −1.80) | |
| `Truck_Hatch` | new | (1.10, 2.82, −1.10) | Serving hatch with its struts, hinge along local Z; open at localEulerAngles.z = +80 (checked in Unity). 164 tris |
| `Truck_Collision` | `Colliders/BodyCollider` mesh | (0, 0, 0) | Convex hull of the body and hatch, 124 tris, x −1.231…1.26, y 0.30…3.10 (roof units left outside), z −3.15…2.65. Convex MeshCollider, layer 12 as before. No wheels and nothing below 0.30 since 13 October 2026, as the game's trucks (delivery truck 0.33, freight truck 0.30): with the wheels in it the hull reached the road, sat about 2 cm into it at the rear at rest and 5 to 14 cm in a hard turn, and scraped kerbs and street objects, which played NWH's crash sound |

Wheelbase 3.60 m (the example car: 2.70 m), track 1.74 m (1.50 m).

**Values to set on the prefab** (the example car's in brackets):

| Field | Value |
|---|---|
| Visual wheel transforms | the four hub positions above |
| `*_WheelController` positions | hub + (0, 0.164, 0), so y = 0.544 (0.470), keeping the example's suspension offset [inference] |
| WheelController radius / width | 0.38 / 0.23 (0.305 / 0.190) |
| Wheel `ShadowCaster` scale | (0.279, 0.358, 0.358), the example's ratio to wheel width and radius ((0.231, 0.287, 0.287)) |
| NWH mass module `dimensions` | (2.20, 3.10, 5.80) (1.99, 1.41, 4.58) |
| `NavMeshObstacle` | centre (0, 1.55, −0.25), extents (1.10, 1.55, 2.90) |
| `RefuelingPosition` | (−1.45, 0, −2.30): fuel cap on the left at (−1.10, 0.90, −2.30) ((−1.2, 0, −1.5)) |
| `LoadingPosition` | (0, 0, −3.50), 0.35 m behind the rear door, as on the car ((0, 0, −2.633)) |
| `Driverside` / `Passengerside` | (∓1.35, 0, 1.15), beside the cab doors [suggested; nudge in the editor] ((−1.243 / 1.007, 0, 0.117)) |
| `Spotlights` | (0, 1.78, 2.28): the car's height, 0.375 m behind the front as on the car [inference] ((0, 1.78, 1.93)) |
| `SmokeBrokenCar` | (0, 1.00, 2.20), under the hood [suggested] ((0, 0.85, 1.64)) |
| Spoiler box colliders | remove; the truck has no spoiler |
| Rigidbody mass and `centerOfMass` | not art values; see "Driving" below |

**Driving** (`TruckAssets`, 10 October 2026, after Peter found 0.9.7's truck as quick as a sports car). The game's 1.0 vehicle prefabs (`defaultlocalgroup_assets_prefabs` and `_vehicletypes` bundles, read with UnityPy on a copy outside the repo) give every car and truck the same 1,400 kg rigidbody with no drag, the same gears (5.748, 3.786, 2.778, 2.108), the same 10,000 rpm limiter and the same springs and dampers on the vans and trucks (5,000 N, 5,000, load rating 7,000). They differ in engine power, power curve, final drive and wheel size, so top speed is the limiter in top gear. The NWH speed limiter is off on every one; its limit mostly repeats the type's `maxSpeed`. `VehicleType.enginePower` and `brakeForce` mostly repeat the prefab's engine power and brake torque (not on the Mersaidi Dash or the Limo), but nothing in the 1.0 game reads them; `maxSpeed` feeds the showroom and the bridge skip's fuel. Before this, the truck was the example car scaled to 3,500 kg with its engine left at 120 kW, final drive 8.51 (80 km/h at the limiter) and brakes at 2,500 N m.

| | Power (kW) | Turbo | Final drive | Wheel radius | Top (limiter) | Brakes (N m) | Centre of mass | Type speed / power / brakes |
|---|---|---|---|---|---|---|---|---|
| HonzaMimic (starter car) | 45 | no | 8.51 | 0.305 | 64 km/h | 2,500 | (0, 0.3, 0) | 60 / 45 / 2,500 |
| UMCDesert, MersaidiDash (vans) | 55 | no | 11.14 | 0.403 | 65 km/h | 2,500 | (0, 0.6, 0.09 / 0.8) | 50, 65 / 55, 70 / 2,500 |
| VordV150 (pickup) | 130 | no | 7.62 | 0.403 | 95 km/h | 4,000 | (0, 0.3, 0) | 80 / 130 / 4,000 |
| DeliveryTruck (box truck) | 46 | x1.2 | 15.25 | 0.43 | 50 km/h | 2,500 | (0, 0.4, 0.68) | 55 / 46 / 2,500 |
| FreightTruckT1 (semi) | 35 | x1.2 | 15.25 | 0.542 | 64 km/h | 2,500 | (0, 0.4, 0.68) | 50 / 35 / 2,500 |
| Food truck, 0.9.7 as played | 120 | no | 8.51 | 0.38 | 80 km/h | 2,500 at 3,500 kg | (0, 0.6, −0.2) | 50 / 112.5 / 6,250 |
| Food truck now (1,600 kg) | 40 | no | 13.5 | 0.38 | 50 km/h | 2,500 | (0, 0.4, −0.2) | 50 / 40 / 2,500 |

The DeliveryTruck is the nearest match for a step van. The food truck takes its suspension, power curve (less power at low revs than the example car's), brakes and centre-of-mass height, its top speed through a final drive suited to our smaller wheels, and a little less power than both trucks (40 kW without the turbo against their 55 and 42 kW with it), so it pulls like the semi and tops out like the delivery truck. Turbo "x1.2" is NWH's forced induction `powerGainMultiplier` [inference: full boost multiplies power by it]. Top speeds are 10,000 rpm in top gear converted to wheel speed [code reading, unverified in game]; the game's own type speeds are a little higher than that for the trucks. The springs, dampers and load rating go back to the example car's, which are the vans' and trucks'. Status: pending in game (CHECKS, "Truck driving").

**Mass** (Peter, 13 October 2026: "slightly heavier", not a different-feeling vehicle). The truck weighs 1,600 kg against the game's 1,400 kg for every vehicle. What changes with it:

- *Pulling away.* The engine stays at 40 kW, so the truck accelerates about 12% slower (1,400 / 1,600) and takes about 14% longer to reach speed. That is the heavier feel. Top speed is still the limiter's 50 km/h: at 50 km/h 40 kW is still about 2,900 N of drive, and the rigidbody has no drag [inference].
- *Springs and dampers scale with the mass* (`SpringMaxForce` and `DamperRate`, 5,000 × 1,600 / 1,400 = 5,714). NWH's spring is linear (force curve 0 to 1) over 0.6 m, so on the unscaled 5,000 N spring each wheel's 3,920 N static load would compress it 78% instead of 69%: the truck would sit about 6 cm lower, which moves the hatch and the crew, and under hard braking the front wheels (about 30% more load) would pass the spring's 5,000 N and bottom out. Scaled, the truck sits as before. The damping ratio goes with damper rate over the square root of spring rate times mass, so scaling the dampers by the same factor keeps it.
- *Brakes stay at 2,500 N m.* NWH applies `brakes.maxTorque` to each wheel (`Brakes.AddBrakeTorqueAllWheels`) [code, 1.0 ExternalPlugins]. On a 0.38 m wheel that is about 6,600 N per wheel against about 3,900 N of load, more than the tyres grip at either mass, so the stop is limited by grip, which grows with the load, and doesn't lengthen [inference: tyre friction about proportional to load].
- *Unchanged:* the tyres' load rating (7,000 N, well above 3,920 N), the centre of mass, the gearing and the power curve.

Status: pending in game (CHECKS, "Bays on the door's side and a heavier truck", row B6: the truck next to the game's delivery truck).

**Serving window**: opening at x = 1.10, y 1.80…2.82, z −2.00…−0.20, centre (1.10, 2.31, −1.10), at least 0.30 m deep. Stainless shelf on the outside, top at y 1.75, x 1.10…1.26. A 1.8 m person on the compartment floor (y 0.94) has eyes at 2.62 and the top of the head at 2.74, both inside the opening.

**Interior** (version 5, Peter, 10 October 2026: "cooking line on the back wall, like real step vans"). In 0.9.7 a griddle station with its shelf and hood filled the cab half of the window and two pieces stood scaled down on a ledge in the hatch, facing the street; from the pavement the player looked half hidden and the grill small and backwards. Now, inside wall to inside wall (x 1.02 to −1.02):

| Part | Unity position | Notes |
|---|---|---|
| Serving counter | x 0.78…1.02, z −2.00…−0.20, top y 1.80 | A shallow steel cabinet under the window whose top meets the sill. Nothing in the FBX stands on it or above it (`spec.json` "serving window opening"). `TruckAssets` puts `Cash_Register` at (0.90, 1.80, −0.82), turned −90° about Y so the screen and drawer face the server inside (−X), at uniform scale 2.2. Footprint 0.293 × 0.330 m: x 0.750…1.043, z −0.985…−0.655, top 2.190. Offset 0.28 m towards the cab from the player's and main crew place (0.59, 0.94, −1.10), leaving the middle clear. The drawer has 84% of its depth supported and about 2 cm overhang each side; clear of crew (x ≤ 0.74), frame, sliding panes and hatch. Pending in game (13 October 2026) |
| Sliding panes | x 0.955…1.01, z −2.935…−2.00 | Parked in a pocket against the wall past the opening's rear end, on tracks along the counter and over the pocket |
| Hand sink | x 0.43…1.02, z −0.12…0.88, top 1.75 | Hatch side, cab end, before the opening starts |
| Aisle | x −0.40…0.78 | The player and crew stand at x 0.59 (`WindowCrew`): three 0.30 m bodies side by side (x 0.44…0.74, z −2.00…−0.20, y 0.95…2.74) and the waiting places behind them (z −2.95…−2.00) touch nothing |
| Back counter | x −1.00…−0.40, z −2.12…0.88, top 1.75 | Cabinet to the floor with door seams, toe kick and handles; steel backsplash to y 2.64 |
| Hood | x −1.02…−0.36, z −2.10…−0.04, y 2.64…2.92 | Slanted front, filters underneath |
| Fridge | x −1.00…−0.55, z −2.92…−2.15, to y 2.70 | Reach-in, beside the rear doors |
| Shelf | x −1.00…−0.62, z 0.10…0.88, y 2.30 | Dry goods over the cab end of the line, clear of the hood |

From the pavement the player and crew fill the window chest up, with the hood and the top of the line behind them (`truck/renders/interior-pavement-open.png`); the pieces themselves show from the game's higher camera and between people.

**Equipment spots** (built by `TruckAssets`, not in the FBX). Four spots on the back counter, full size (scale 1), each turned +90° about Y so a piece's front (+Z) faces the cook and the hatch. They are 0.45 m apart along the truck and centred on the window, so a 0.40 m piece leaves 5 cm between neighbours, and they fill from the middle out. A 0.40 × 0.36 m piece covers x −0.44…−0.80, between the counter's front edge and the backsplash (−0.985); the tallest model (MerchRack, 0.65 m) reaches y 2.40, under the hood's lowest edge (2.64). `truck/review_interior.py` places the real models and checks them against `Truck_Body` and each other; all sixteen are clear, the Extra Storage cabinet (`StorageCabinet`, 0.60 m) included.

| Spot | Position | Fills |
|---|---|---|
| `EquipmentLine1` | (−0.62, 1.75, −0.425) | third |
| `EquipmentLine2` | (−0.62, 1.75, −0.875) | first |
| `EquipmentLine3` | (−0.62, 1.75, −1.325) | second |
| `EquipmentLine4` | (−0.62, 1.75, −1.775) | fourth |

The 0.9.3 to 0.9.7 layout (two spots at the hatch's ends, scaled 0.72 on a steel ledge, `Truck_Ledge`, and two on the old worktop) is gone, ledge included.

**Material:** duplicate `M_HonzaCar.mat` (the game's `SH_Vehicle` graph) and swap each texture for its counterpart. Import settings as for the example: Read/Write on for the meshes (the damage component deforms them [inference, as in the tooling research]), `T_FoodTruck_N` as a normal map (not sRGB), the others as default textures (the mask sRGB, as in "Surface look"). `TruckAssets` caps `T_FoodTruck_M` and `T_FoodTruck_N` at 1,024 px in the importer, as the game's vehicles have (`Van_N`, `T_VanBig_M`, `T_VanBig_N`: 1,024); the base colour stays 2,048 for its lettering and panel lines. The 2,048 px maps had made the truck's bundle 25 MB. The cap is in the importer, not in `game_look.py`, so the bakes keep their full-size sources and the step stays the same for every model. A Blender render with both maps box-filtered to 1,024 (normals renormalised) looked the same as at 2,048 from the street view.

| Slot | Texture | Contents |
|---|---|---|
| `_BaseColorMap`, `_MainTex` | `T_FoodTruck_BC` | Base colour from `game_look.py` (source `T_FoodTruck_D`); alpha is the paint mask (below) |
| `_MaskMap` | `T_FoodTruck_M` | R metallic, G AO, B 0, A smoothness, from `game_look.py` (source `T_FoodTruck_MaskMap`) |
| `_NormalMap` | `T_FoodTruck_N` | Rounded edges baked by `game_look.py` |
| `_EmissionTexture`, `_FrontLights`, `_BackLights`, `_BlinkerLeft`, `_BlinkerRight` | `T_FoodTruck_E` | Light colours |
| `_EmissionMask` | `T_FoodTruck_EM_1` | R headlights plus the amber marker and work lights, G tail/brake, B reverse |
| `_IndicatorsMask` | `T_FoodTruck_EM_2` | R right blinkers and side repeaters, G left ones |

**Paint mask.** The vehicle shader (`SH_Vehicle`, read from the SDK's shader graph) sets the base colour to `lerp(texture, Tint, smoothstep(_Min, _Max, alpha))`, then blends towards `FresnelColor` by `smoothstep × Fresnel(power) × AO`, and multiplies smoothness by `SmoothnessBoost` (1.1) on the same mask. `CarFeatures.SetColor` sets Tint, the rim colour and the rim power on every renderer in `bodyMeshes` (the body, the four wheels and the hatch) from the vehicle's colour name. The tint replaces the texture outright, so a mask texel at 1 shows one flat colour: the baked road-side darkening, the seam lines and rivets drawn into the paint and `game_look.py`'s grime are lost there, as on the game's own cars; the occlusion in the mask's G, the normal map and the game's dirt still apply.

Until 14 October 2026 every cream and teal panel had alpha 1, so the whole body took one tint, and the mask was switched off (`_Min` 1.5, `_Max` 2). Now `build_truck.py` writes alpha 1 only on the body paint, per palette tag, and the material keeps the example car's `_Min` 0 and `_Max` 0.5 (the alpha is exactly 0 or 1):

| Tag | Alpha | Parts |
|---|---|---|
| `paint` | 1 | Box, cab, roof, hood, front fenders, grille surround, rear door, generator hatch panel, the serving hatch on both faces (its underside is the awning). 0 where a paint face looks into the food compartment (inner walls, ceiling, bulkhead; `faces_compartment`), so the kitchen stays cream |
| `seam` | 1 | The panel joins on the sides: in the body colour they show by their shape, not as a cream line on a red truck |
| `stripe` | 0 | Teal skirt and rocker band, the bands under the awning: the livery keeps its teal |
| `diamond`, `steel`, `rim`, `tank`, `trim` | 0 | Diamond plate, rear fenders, frames, rails, ladder, wheel rims, tanks, trims |
| `rubber`, `plastic`, `glass`, `interior` | 0 | Tyres, gaskets, bumpers, mirrors, glass, cab interior and floor |
| `head`, `tail`, `reverse`, `left`, `right`, `marker`, `worklight` | 0 | Lights |
| `warm`, `chalk` | 0 | The menu board on the side |

Alpha 1 covers 18% of the atlas (was 33%). Each island's padding (2 px, 1 px on 16 px cells) keeps its own island's alpha, so at mip 0 the mask is exact on both sides of every island edge. At far mips neighbouring islands blend a little both ways: a trim or livery island next to paint can take a trace of the tint, and a paint edge can show a trace of its neighbour. The atlas cells are 16 px aligned, so box-filtered mips stay inside a cell up to mip 4 and the blending is limited to bilinear sampling at island edges. Zeroing the paint islands' padding was tried and dropped: bilinear samples at a panel's corners then read alpha 0.25 at mip 0, half a tint, which shows cream flecks on a dark truck. CHECKS C2 looks from about 30 m for both.

The cream is only the default for a truck without a colour name: the debug spawn (`DebugActions` makes a `VehicleInstance` without one, and the prefab has no `RandomVehicleColor`). There the material's defaults show: Tint is the body's cream as `game_look.py` grades it (median sRGB 186, 162, 119), the rim a lighter cream (× 1.3), the rim power the example's 2.04. A bought truck always has a colour: `ContractVehicleForSale` starts the picker on `GlobalReferences.vehicleColors[0]`, so buying without touching the picker gives that first swatch, not cream.

The game's 1.0 vehicle colours (`VehicleColor` assets in `sharedassets1.assets`, read with UnityPy from a copy outside the repo on 14 October 2026; sRGB bytes, path IDs 6799 to 6810 in this order). The order of `GlobalReferences.vehicleColors`, and so the dealer's first swatch, wasn't in the copied files; if it follows the assets, the first is Black.

| Name | Random weight | Tint | Fresnel colour | Fresnel power |
|---|---|---|---|---|
| Black | 1 | 13, 13, 13 | 19, 19, 19 | 7 |
| Blue | 0.3 | 49, 89, 156 | 16, 39, 84 | 1 |
| DarkBlue | 1 | 15, 34, 72 | 41, 44, 87 | 2 |
| DarkRed | 1 | 60, 0, 0 | 106, 0, 0 | 2 |
| Green | 0.1 | 47, 87, 45 | 12, 41, 8 | 1 |
| Grey | 1 | 96, 96, 96 | 59, 65, 67 | 1 |
| Pink | 0.1 | 184, 135, 167 | 182, 97, 192 | 1 |
| Purple | 0.1 | 38, 9, 60 | 76, 41, 96 | 1 |
| Red | 0.5 | 164, 28, 27 | 79, 26, 26 | 1 |
| Silver | 1 | 108, 108, 108 | 152, 152, 152 | 1 |
| White | 1 | 188, 188, 188 | 123, 123, 123 | 1 |
| Yellow | 0.1 | 197, 163, 38 | 115, 79, 26 | 1 |

None is close to the cream (186, 162, 119): White has its lightness but no warmth, Yellow its hue family but three times the saturation. So a cream truck exists only from the debug spawn. Most game colours have a rim darker than the tint and power 1, unlike our default's lighter rim at 2.04. Preview renders of the mask and of a few tints, with the shader's logic rebuilt in Cycles, are in `build/truck-paint/` (not committed).

**Decals off.** The material has `_DISABLE_DECALS` and `_SupportDecals` 0, as the game's own vehicle materials (since 14 October 2026). The SDK's HDRP assets compile decal variants for four decal targets (`DECALS_4RT`), the game renders with three (`DECALS_3RT`); a four-target variant blends metallic and ambient occlusion from a fourth decal buffer the game never fills, which left the truck almost black in shade whenever the game drew its decal buffers (Peter: "much better" with decals off). Every material we ship refuses decals, so the build keeps the SDK's four-target setting. The game's vehicle materials also turn screen-space reflections off; ours keep `_ReceivesSSR` 1, since matching that needs the stencil setup changed with it.

The channel meanings come from mapping the example car's masks back onto its mesh: `EM_1` R lies on the headlights, G and B on the rear lights, `EM_2` R on the right side and G on the left [inference; not yet seen lit in game]. The dirt textures stay the example's (`SM_HonzaCar_Dirt*`); the second UV map is a non-overlapping copy of the first, for them.

## Commissary bay

The loading bay's street furniture (0.9.10, plan "Commissary bay look"), built by `commissary/build_commissary.py` in Blender headless, no MCP:

```bash
blender -b --factory-startup --python art/commissary/build_commissary.py
art/tools/apply_look.sh art/commissary/*.spec.json
blender -b --factory-startup --python art/commissary/render_review.py
```

The build script writes the paint and the lettering, which are final, and `BayCommissary.blend` with the whole set. The sign and each prop then get the surface look like every other model: the script lays each one out on its own atlas (Smart UV Project and a tight repack for the props; by hand for the sign, so the plate gets 1.8 mm a texel and the pole 6 mm), bakes the part textures (wood, crates, produce, cartons, the plates) into a flat `T_<Name>_BaseColor.png`, and saves `<Name>.blend` and `<Name>.spec.json`. `apply_look.sh` then writes `T_<Name>_BC/_M/_N.png` and the FBX, and scores it. The spec's `look` entry sets the classes: wood and cardboard matte (fabric), produce as food, the dumpster's paint at 0.30 (dark), steel and the black lids by colour. The sign's spec also lists one variant per commissary: `game_look.py` grades each `T_BaySign_<id>_BaseColor.png` into `T_BaySign_<id>_BC.png` with the same bakes and mask. The part textures are only bake sources and are packed into `BayCommissary.blend`. Atlases are 512 px, the hand truck's 256. The roll cage's hundred thin bars leave its atlas about a third full, so its spec asks for 30% fill instead of 40%. `render_review.py` places the look versions for the review renders in `commissary/renders/`, and `render_look.py` made `renders/look-day.png` and `look-night.png`; those three are kept. Nothing in it comes from the game: the textures are procedural, the lettering is Blender's own Inter font (SIL Open Font License), and the sign's names come from `data/commissaries.json` and `en.json`, so a renamed commissary needs the script run again.

| File | What | Size, origin |
|---|---|---|
| `T_BayPaint_BaseColor.png` | Worn road paint, 188 sRGB like the game's decal lines, with asphalt showing through scuffs and grit; tiles every 2 m | 512² |
| `BayLettering.fbx` | LOADING ZONE as flat paint, condensed | 1 m cap height, 8.04 m long, origin in the middle; reads along Unity +x with the letters' tops to +z |
| `BaySign.fbx`, `T_BaySign_BC/_M/_N.png`, `T_BaySign_<id>_BC.png` | A galvanised pole, 3.0 m, with a 0.40 × 0.80 m plate from 1.90 m: COMMERCIAL VEHICLES ONLY, LOADING ZONE, a double arrow, FOOD VENDORS and the commissary's name in green. The plate faces Unity +z and reads from both sides | Origin at the pole's foot |
| `BayPalletStack.fbx` | Two pallets, four crates of produce, two cartons | 1.20 × 1.00 × 0.87 m |
| `BayHandTruck.fbx` | A hand truck with two cartons | 0.52 × 0.50 × 1.23 m |
| `BayRollCage.fbx` | A roll cage, part loaded | 0.72 × 0.84 × 1.71 m |
| `BayDumpster.fbx` | A 2-yard dumpster, green with black lids | 1.91 × 1.28 × 1.29 m |

Props have their origin at the bottom centre and the front at Unity +z. `tools/build-mod.sh` copies the FBX files (as `<Name>_Model.fbx`), the paint texture and the look step's `_BC`, `_M` and `_N` maps into `unity/FoodTrucks/Commissary/BayArt/`, and `CommissaryAssets` makes the materials and `<Name>_Display.prefab`s there. The sign and the props use `SurfaceLook.LitMaterial` (`M_<Name>`, and `M_BaySign_<id>` per commissary on the same mask and normal map, which `BayProps` swaps onto the plate) with the FBX's own normals; the paint keeps its flat road-line material, with decals off. Every prefab, the lettering's included, gets `SurfaceLook.NoStreetDecals`, and `BayMarkers` sets the outline it draws and the game meshes it copies to the same rendering layer at run time, so the dirt decals skip all bay pieces; each prop prefab has a box collider and a carving NavMeshObstacle of its bounds. In game the pallet and the hand truck are the game's own `DeliveryPallet` (bottom two layers of boxes) and `Vehicles/PlayerVehicles/HandTruck` meshes when those load; the mod's models stand in otherwise. The game has no dumpster or roll cage.
## Cash register

`props/CashRegister/`: a compact point-of-sale register after the game's `ConcessionsStandRegister` and checkout counters (charcoal drawer, lighter deck, a screen on a short post on the drawer side, a card terminal, a receipt slot). Source size 0.150 × 0.133 m, 0.177 m tall, 424 tris, 256 px maps, front −Y (Unity +Z), origin at the bottom centre. Built by `build_cash_register.py` (headless, `--factory-startup`), then `apply_look.sh`. `tools/build-mod.sh` copies it into `Vehicles/PropArt` as `CashRegister_Model.fbx`, and `PropAssets` adds it to the cart and the truck as a plain mesh child (`Cash_Register`, no collider) in the vehicle's `renderers` list. The truck scales it uniformly to 2.2: 0.330 m wide, 0.293 m deep and 0.390 m tall. This preserves the existing tall screen's proportions; it cannot also match a 0.25–0.30 m height at that width without changing the model. Positions are in the cart and truck tables above.

## Status

| | Cart | Truck |
|---|---|---|
| Scorer | pass | pass |
| Unity 2022.3.62f2 import (empty project) | checked | checked |
| In the SDK project / prefab | pending (Milestone 8) | pending (Milestone 8) |
| In game | pending | pending |
| Look review (Peter) | pending | pending |
