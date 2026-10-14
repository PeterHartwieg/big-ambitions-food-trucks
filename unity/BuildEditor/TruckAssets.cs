#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Vehicles.VehicleTypes;
using Object = UnityEngine.Object;

namespace FoodTrucks.BuildEditor
{
    // The food truck: the SDK's example car with the final truck model from art/truck (art/README.md,
    // "Food truck"). tools/build-mod.sh copies the FBX and textures into Vehicles/TruckArt first.
    // Driving: the game's own delivery truck (DeliveryTruck in its 1.0 prefabs bundle), a touch slower. See the
    // Driving block below and art/README.md, "Driving".
    internal static class TruckAssets
    {
        private const string ExampleVehicleType = "Assets/Mods/Example-Vehicle/TurboHonza.asset";
        private const string ExampleVehiclePrefab = "Assets/Mods/Example-Vehicle/TurboHonza.prefab";
        private const string ExampleMaterial = "Assets/Mods/Example-Vehicle/Models/M_HonzaCar.mat";

        private const float TruckPrice = 15000f; // Placeholder until the balance pass near the end.
        private const int CargoSlots = 16;

        // Driving, from the game's 1.0 vehicle prefabs (read with UnityPy, art/README.md "Driving"). The game gives
        // every car and truck the same 1,400 kg rigidbody, its delivery truck included, and sets speed by gearing
        // and power instead; the vans and trucks share the example car's springs and dampers (5,000 N, 5,000, load
        // rating 7,000). The delivery truck has 46 kW with forced induction (x1.2), final drive 15.25 on 0.43 m
        // wheels (50 km/h at the 10,000 rpm limiter) and 2,500 N m brakes; the freight truck 35 kW with forced
        // induction. Ours: 40 kW without it, so it pulls a little less than either (it carries a kitchen), the
        // delivery truck's power curve (less low-end than the example car's), and a final drive for the same
        // 50 km/h on our 0.38 m wheels. The 1.0 game reads VehicleType.maxSpeed only for the showroom and the bridge
        // skip's fuel, and enginePower and brakeForce not at all; they mirror the prefab, as most of the game's do.
        //
        // Mass (Peter, 13 October 2026): 1,600 kg, "slightly heavier" than the game's vehicles but not a different
        // feel. The same 40 kW then pulls away about 12% slower and still reaches the 50 km/h limiter. The springs
        // and dampers scale with the mass, so the truck sits at the same height (each wheel carries 3,920 N, which on
        // the unscaled 5,000 N linear spring would sink 6 cm lower and bottom out under hard braking) and is damped
        // as before (the ratio goes with the square root of spring rate times mass). The brakes stay: 2,500 N m on
        // each wheel already asks more than the tyres grip at either mass, so the stop is the tyres' and doesn't
        // lengthen. The tyres' load rating (7,000 N) stays well above the load.
        private const float Mass = 1600f;
        private const float GameMass = 1400f;
        private const float SpringMaxForce = 5000f * Mass / GameMass; // N, 5,714; NWH spring maxForce, linear curve
        private const float DamperRate = 5000f * Mass / GameMass; // NWH damper bumpRate and reboundRate
        private const float EnginePower = 40f; // kW, NWH engine maxPower
        private const float FinalGearRatio = 13.5f; // top gear 2.108: 10,000 rpm at 50 km/h on 0.38 m wheels
        private const int TopSpeed = 50; // km/h
        private const float BrakeTorque = 2500f; // N m on each wheel, the delivery truck's (grip-limited, see Mass)
        // Steering (Peter, 13 October 2026, after driving 1,600 kg: "the curves you can take are too narrow"). The
        // example car's 35 degrees on our 3.60 m wheelbase turned the truck in about 6.3 m (wheelbase / sin of the
        // angle, to the outer front wheel), car-like. 28 degrees gives about 7.7 m, a step van's 15 to 16 m kerb-to-kerb
        // circle.
        private const float MaxSteerAngle = 28f;
        // The delivery truck's power curve: power fraction over rpm / rev limiter, with its tangents.
        private static readonly Keyframe[] PowerCurve =
        {
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.27655029f, 0.3147766f, 2.8613956f, 2.8613956f),
            new Keyframe(1f, 1f, 0f, 0f),
        };

        private static readonly (string Wheel, string Mesh, Vector3 Hub)[] Wheels =
        {
            ("FrontLeft", "Truck_Wheel_FL", new Vector3(-0.87f, 0.38f, 1.80f)),
            ("FrontRight", "Truck_Wheel_FR", new Vector3(0.87f, 0.38f, 1.80f)),
            ("RearLeft", "Truck_Wheel_RL", new Vector3(-0.87f, 0.38f, -1.80f)),
            ("RearRight", "Truck_Wheel_RR", new Vector3(0.87f, 0.38f, -1.80f)),
        };

        private static readonly Vector3 RegisterPosition = new Vector3(0.90f, 1.80f, -0.82f);
        private const float RegisterScale = 2.2f;

        // The cooking line (Peter, 10 October 2026): four spots on the back counter, opposite the hatch, numbered
        // from the cab end and filled from the middle out, so one or two pieces stand right behind the player.
        // Full size, 0.45 m apart along the truck and centred on the window (z -1.10); a 0.40 x 0.36 m piece
        // covers x -0.44 to -0.80, between the counter's front edge (-0.40) and the backsplash (-0.985), and
        // the tallest (0.65 m) stays under the hood (2.64). Checked for all sixteen models with
        // art/truck/review_interior.py (art/README.md, "Equipment spots").
        private const float LineX = -0.62f;
        private const float CounterTop = 1.75f;
        private static readonly (string Name, Vector3 Position)[] EquipmentSpots =
        {
            ("EquipmentLine2", new Vector3(LineX, CounterTop, -0.875f)),
            ("EquipmentLine3", new Vector3(LineX, CounterTop, -1.325f)),
            ("EquipmentLine1", new Vector3(LineX, CounterTop, -0.425f)),
            ("EquipmentLine4", new Vector3(LineX, CounterTop, -1.775f)),
        };

        public static void GenerateTruck(string vehiclesFolder, string typeName)
        {
            var artFolder = vehiclesFolder + "/TruckArt";
            var fbxPath = artFolder + "/FoodTruck_Model.fbx";
            if (!File.Exists(Path.Combine(ProjectRoot, fbxPath)))
                throw new InvalidOperationException($"{fbxPath} missing; tools/build-mod.sh copies it from art/truck.");
            ConfigureImports(artFolder);
            var meshes = Meshes(fbxPath);
            var material = Material(artFolder);

            var typePath = vehiclesFolder + "/FoodTruck.asset";
            var prefabPath = vehiclesFolder + "/FoodTruck.prefab";
            CopyFresh(ExampleVehicleType, typePath);
            CopyFresh(ExampleVehiclePrefab, prefabPath);

            var type = AssetDatabase.LoadAssetAtPath<VehicleType>(typePath);
            type.name = "FoodTruck";
            type.vehicleTypeName = typeName;
            type.price = TruckPrice;
            type.maxCargoCapacity = CargoSlots;
            type.maxFuel = 80f;
            type.maxSpeed = TopSpeed;
            type.enginePower = EnginePower;
            type.brakeForce = BrakeTorque;
            type.taxDeductible = true;
            EditorUtility.SetDirty(type);

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var controller = root.GetComponent<VehicleController>();
                if (controller == null) throw new InvalidOperationException("Example prefab has no VehicleController.");
                controller.vehicleType = type;
                controller.showCargoInItemPanel = true;
                root.name = "FoodTruck";

                var body = Find(root, "CarHolder/Body/CheapSaloon");
                body.GetComponent<MeshFilter>().sharedMesh = meshes["Truck_Body"];
                body.GetComponent<MeshRenderer>().sharedMaterial = material;
                var obstacle = body.GetComponent<NavMeshObstacle>();
                obstacle.center = new Vector3(0f, 1.55f, -0.25f);
                obstacle.size = new Vector3(2.20f, 3.10f, 5.80f);

                // The hatch opens when the truck opens for business (rotated in game, see TruckHatch).
                var hatch = new GameObject("Truck_Hatch");
                hatch.layer = body.layer;
                hatch.transform.SetParent(body.transform.parent, false);
                hatch.transform.localPosition = new Vector3(1.10f, 2.82f, -1.10f);
                hatch.AddComponent<MeshFilter>().sharedMesh = meshes["Truck_Hatch"];
                var hatchRenderer = hatch.AddComponent<MeshRenderer>();
                hatchRenderer.sharedMaterial = material;
                // The game paints, dirties and outlines the renderers in these lists; the hatch is body work.
                Append(root, "renderers", hatchRenderer);
                Append(root, "bodyMeshes", hatchRenderer);

                // The four equipment spots (VehicleDisplays) on the back counter, each turned so the piece's front
                // (+Z) faces the cook and the hatch (+X). The hatch side keeps only its serving counter (in the FBX).
                foreach (var (name, position) in EquipmentSpots)
                {
                    var spot = new GameObject(name).transform;
                    spot.SetParent(root.transform, false);
                    spot.localPosition = position;
                    spot.localRotation = Quaternion.Euler(0f, 90f, 0f);
                }

                var collider = Find(root, "CarHolder/Colliders/BodyCollider").GetComponent<MeshCollider>();
                collider.sharedMesh = meshes["Truck_Collision"];
                collider.convex = true;

                foreach (var (wheel, mesh, hub) in Wheels)
                {
                    var visual = Find(root, "CarHolder/Wheels/" + wheel);
                    visual.transform.localPosition = hub;
                    visual.GetComponent<MeshFilter>().sharedMesh = meshes[mesh];
                    visual.GetComponent<MeshRenderer>().sharedMaterial = material;
                    foreach (Transform child in visual.transform)
                        if (child.name.StartsWith("ShadowCaster", StringComparison.Ordinal)) child.localScale = new Vector3(0.279f, 0.358f, 0.358f);

                    var wheelController = Find(root, wheel + "_WheelController");
                    wheelController.transform.localPosition = hub + new Vector3(0f, 0.164f, 0f);
                    Set(wheelController, "wheel.radius", 0.38f);
                    Set(wheelController, "wheel.width", 0.23f);
                    Set(wheelController, "spring.maxForce", SpringMaxForce);
                    Set(wheelController, "damper.bumpRate", DamperRate);
                    Set(wheelController, "damper.reboundRate", DamperRate);
                }

                root.GetComponent<Rigidbody>().mass = Mass;
                Set(root, "baseMass", Mass);
                Set(root, "combinedMass", Mass);
                Set(root, "dimensions", new Vector3(2.20f, 3.10f, 5.80f));
                // Low, as on the game's delivery truck (0.4 m on its 3.0 m body), so the tall box doesn't roll
                // over; a little behind the middle for the kitchen.
                Set(root, "centerOfMass", new Vector3(0f, 0.4f, -0.2f));
                Set(root, "powertrain.engine.maxPower", EnginePower);
                Apply(root, "powertrain.engine.forcedInduction.useForcedInduction", p => p.boolValue = false);
                Apply(root, "powertrain.engine.powerCurve", p => p.animationCurveValue = new AnimationCurve(PowerCurve));
                Set(root, "powertrain.transmission.finalGearRatio", FinalGearRatio);
                Set(root, "brakes.maxTorque", BrakeTorque);
                Set(root, "steering.maximumSteerAngle", MaxSteerAngle);
                // Off on every game vehicle too; the game's SetMaxSpeed console command writes this value.
                Set(root, "module.speedLimit", TopSpeed);
                Set(root, "module.capacity", type.maxFuel);
                Set(root, "module.amount", type.maxFuel);

                Place(root, "RefuelingPosition", new Vector3(-1.45f, 0f, -2.30f));
                Place(root, "LoadingPosition", new Vector3(0f, 0f, -3.50f));
                Place(root, "Driverside", new Vector3(-1.35f, 0f, 1.15f));
                Place(root, "Passengerside", new Vector3(1.35f, 0f, 1.15f));
                Place(root, "Spotlights", new Vector3(0f, 1.78f, 2.28f));
                Place(root, "SmokeBrokenCar", new Vector3(0f, 1.00f, 2.20f));

                var spoiler = root.transform.Find("Spoiler");
                if (spoiler != null) Object.DestroyImmediate(spoiler.gameObject);

                // The register in front of the server at (0.59, 0.94, -1.10), offset 0.28 m towards the cab to
                // leave the middle open (Peter, 13 October 2026). Uniform scale 2.2 keeps its proportions:
                // 0.293 x 0.330 m footprint, x 0.750 to 1.043, z -0.985 to -0.655, top y 2.190. The drawer
                // rests at y 1.80 with 84% of its depth on the 0.24 m counter and about 2 cm overhang each side;
                // clear of the crew (x <= 0.74), frame and hatch. Front (+Z), screen and drawer face inside (-X).
                // Body work for the game's lists, like the hatch.
                var register = PropAssets.AddRegister(body.transform.parent, RegisterPosition, Quaternion.Euler(0f, -90f, 0f), body.layer, RegisterScale);
                Append(root, "renderers", register);
                // No street dirt on the truck: its renderers leave the decal layer, as the game's vehicles do.
                SurfaceLook.NoStreetDecals(root);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        private const int VehicleMapSize = 1024;

        // Read/Write meshes for the game's damage deformation; the normal map as a normal map. The mask (sRGB, as
        // SurfaceLook.MaskTexture explains) and the normal map are capped at 1,024 px like the game's vehicle maps
        // (Van_N, T_VanBig_M/_N); game_look.py writes them at the 2,048 px source size, which kept the bundle at 25 MB.
        private static void ConfigureImports(string artFolder)
        {
            var model = (ModelImporter)AssetImporter.GetAtPath(artFolder + "/FoodTruck_Model.fbx");
            if (SurfaceLook.NormalsAsBaked(model) || !model.isReadable || model.importAnimation || model.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                model.isReadable = true;
                model.importAnimation = false;
                model.materialImportMode = ModelImporterMaterialImportMode.None;
                model.SaveAndReimport();
            }
            SurfaceLook.Texture(artFolder + "/T_FoodTruck_N.png", TextureImporterType.NormalMap, srgb: false, maxSize: VehicleMapSize);
            SurfaceLook.MaskTexture(artFolder + "/T_FoodTruck_M.png", VehicleMapSize);
        }

        // Meshes by object name in the FBX (the names are fixed by art/truck/spec.json).
        private static Dictionary<string, Mesh> Meshes(string fbxPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var meshes = new Dictionary<string, Mesh>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                meshes[filter.name] = filter.sharedMesh;
            var needed = Wheels.Select(w => w.Mesh).Concat(new[] { "Truck_Body", "Truck_Hatch", "Truck_Collision" });
            var missing = needed.Where(n => !meshes.ContainsKey(n)).ToList();
            if (missing.Count > 0) throw new InvalidOperationException($"{fbxPath} lacks {string.Join(", ", missing)}.");
            return meshes;
        }

        // A copy of the example car's material (the game's vehicle shader, which the SDK project doesn't have,
        // so the material can't be edited through it) with each of the car's textures swapped for the truck's
        // counterpart in the file itself. The dirt textures stay the example's (art/README.md).
        private static Material Material(string artFolder)
        {
            var path = artFolder + "/M_FoodTruck.mat";
            var exampleFolder = Path.GetDirectoryName(ExampleMaterial)!.Replace('\\', '/');
            var text = File.ReadAllText(Path.Combine(ProjectRoot, ExampleMaterial));
            // The base colour and mask are game_look.py's (T_FoodTruck_BC, _M: graded colour with baked grime,
            // occlusion and wear); the normal map is its baked rounded edges. art/README.md, "Surface look".
            foreach (var (suffix, ours) in new[] { ("D", "BC"), ("E", "E"), ("EM_1", "EM_1"), ("EM_2", "EM_2"), ("MaskMap", "M"), ("N", "N") })
            {
                var from = AssetDatabase.AssetPathToGUID($"{exampleFolder}/T_HonzaCar_{suffix}.png");
                var to = AssetDatabase.AssetPathToGUID($"{artFolder}/T_FoodTruck_{ours}.png");
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                    throw new InvalidOperationException($"Texture T_HonzaCar_{suffix} or T_FoodTruck_{ours} not found.");
                if (!text.Contains(from)) throw new InvalidOperationException($"{ExampleMaterial} doesn't use T_HonzaCar_{suffix}.");
                text = text.Replace(from, to);
            }
            text = text.Replace("m_Name: M_HonzaCar", "m_Name: M_FoodTruck");
            // The vehicle shader replaces the base colour with its single Tint (the example's dark red) wherever
            // smoothstep(_Min, _Max, base colour alpha) is above 0, and adds a Fresnel rim there. The truck's livery
            // has two paint colours in its texture, so the mask is switched off: with _Min 1.5 and _Max 2 the
            // smoothstep is 0 for every alpha. Left at 0 and 0.5, the cream and teal panels rendered dark red,
            // almost black in shade.
            // Decals off, as on the game's own vehicle materials (_DISABLE_DECALS) and our other models (SurfaceLook): the
            // SDK's HDRP assets compile the decal variants for four decal targets (DECALS_4RT) while the game renders with
            // three (DECALS_3RT), so a truck that read the decal buffers took its ambient occlusion from a target the game
            // doesn't fill and rendered almost black in shade whenever the game drew its decal buffers (13 and 14 October).
            foreach (var (from, to) in new[]
                     {
                         ("    - _Min: 0\n", "    - _Min: 1.5\n"), ("    - _Max: 0.5\n", "    - _Max: 2\n"),
                         ("    - _SupportDecals: 1\n", "    - _SupportDecals: 0\n"),
                         ("  m_ValidKeywords:\n  - _DISABLE_SSR_TRANSPARENT\n", "  m_ValidKeywords:\n  - _DISABLE_DECALS\n  - _DISABLE_SSR_TRANSPARENT\n"),
                     })
            {
                if (!text.Contains(from)) throw new InvalidOperationException($"{ExampleMaterial} lacks '{from.Trim()}'.");
                text = text.Replace(from, to);
            }
            File.WriteAllText(Path.Combine(ProjectRoot, path), text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Material>(path)
                   ?? throw new InvalidOperationException($"{path} didn't import.");
        }

        private static GameObject Find(GameObject root, string path) =>
            root.transform.Find(path)?.gameObject ?? throw new InvalidOperationException($"Example prefab has no {path}.");

        // Nested prefab instances (Driverside, Spotlights, ...) sit anywhere below the root.
        private static void Place(GameObject root, string name, Vector3 position)
        {
            var target = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)
                         ?? throw new InvalidOperationException($"Example prefab has no {name}.");
            target.localPosition = position;
        }

        // Sets a serialized field on whichever components of the object have it, so the NWH types
        // need no compile-time reference. Fails when none has it, rather than building a wrong truck.
        private static void Set(GameObject owner, string path, float value) =>
            Apply(owner, path, p => p.floatValue = value);

        private static void Set(GameObject owner, string path, Vector3 value) =>
            Apply(owner, path, p => p.vector3Value = value);

        private static void Append(GameObject owner, string path, Object value) =>
            Apply(owner, path, p =>
            {
                if (!p.isArray) throw new InvalidOperationException($"{path} on {owner.name} is not a list.");
                p.arraySize++;
                p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = value;
            });

        private static void Apply(GameObject owner, string path, Action<SerializedProperty> change)
        {
            var hits = 0;
            foreach (var component in owner.GetComponents<MonoBehaviour>())
            {
                if (component == null) continue;
                var serialized = new SerializedObject(component);
                var property = serialized.FindProperty(path);
                if (property == null) continue;
                change(property);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                hits++;
            }
            if (hits == 0) throw new InvalidOperationException($"No component on {owner.name} has {path}.");
        }

        private static void CopyFresh(string from, string to)
        {
            File.Copy(Path.Combine(ProjectRoot, from), Path.Combine(ProjectRoot, to), overwrite: true);
            AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceUpdate);
        }
    }
}
