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
        // and power instead, so the truck keeps the example car's mass, springs and dampers (the delivery truck's
        // too: 5,000 N, 5,000, load rating 7,000). The delivery truck has 46 kW with forced induction (x1.2), final
        // drive 15.25 on 0.43 m wheels (50 km/h at the 10,000 rpm limiter) and 2,500 N m brakes; the freight truck
        // 35 kW with forced induction. Ours: 40 kW without it, so it pulls a little less than either (it carries a
        // kitchen), the delivery truck's power curve (less low-end than the example car's), and a final drive for
        // the same 50 km/h on our 0.38 m wheels. The 1.0 game reads VehicleType.maxSpeed only for the showroom and
        // the bridge skip's fuel, and enginePower and brakeForce not at all; they mirror the prefab, as most of the game's do.
        private const float Mass = 1400f;
        private const float EnginePower = 40f; // kW, NWH engine maxPower
        private const float FinalGearRatio = 13.5f; // top gear 2.108: 10,000 rpm at 50 km/h on 0.38 m wheels
        private const int TopSpeed = 50; // km/h
        private const float BrakeTorque = 2500f; // N m, the delivery truck's (same mass, same stopping)
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

        // Spot, position and scale, in the order pieces fill them. A 0.40 x 0.36 x 0.70 m piece fits each.
        private static readonly (string Name, Vector3 Position, float Scale)[] EquipmentSpots =
        {
            ("EquipmentHatchRear", new Vector3(0.955f, LedgeTop, -1.83f), HatchScale),
            ("EquipmentHatchFront", new Vector3(0.955f, LedgeTop, -0.37f), HatchScale),
            ("EquipmentBackRear", new Vector3(-0.28f, 1.75f, -1.80f), 1f),
            ("EquipmentBackFront", new Vector3(-0.28f, 1.75f, -1.35f), 1f),
        };
        // 0.26 m of recess over a 0.36 m deep piece.
        private const float HatchScale = 0.72f;
        // Just above the sill (y 1.77 to 1.80); the ledge runs from the panes' track (x 0.815) to the hatch (1.10).
        private const float LedgeTop = 1.805f;

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

                // A steel ledge inside the hatch, under the window's two equipment spots (art/README.md, "Serving window").
                var ledge = new GameObject("Truck_Ledge");
                ledge.layer = body.layer;
                ledge.transform.SetParent(body.transform.parent, false);
                ledge.AddComponent<MeshFilter>().sharedMesh = LedgeMesh(artFolder);
                var ledgeRenderer = ledge.AddComponent<MeshRenderer>();
                ledgeRenderer.sharedMaterial = SteelMaterial(artFolder);
                // Hidden with the truck (EntityController.Hide), but not painted: it isn't body work.
                Append(root, "renderers", ledgeRenderer);

                // The four equipment spots (VehicleDisplays), each turned to face out of the hatch (+X). The window's
                // spots sit at its two ends, between the sliding panes and the hatch, a 0.26 m deep recess, so their
                // pieces are scaled to fit; the worktop's two stand full size behind the crew, on the rear half
                // (the cab half has the workstation). Clearances measured against Truck_Body.
                foreach (var (name, position, scale) in EquipmentSpots)
                {
                    var spot = new GameObject(name).transform;
                    spot.SetParent(root.transform, false);
                    spot.localPosition = position;
                    spot.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    spot.localScale = Vector3.one * scale;
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

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        // Read/Write meshes for the game's damage deformation; the normal map as a normal map.
        private static void ConfigureImports(string artFolder)
        {
            var model = (ModelImporter)AssetImporter.GetAtPath(artFolder + "/FoodTruck_Model.fbx");
            if (!model.isReadable || model.importAnimation || model.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                model.isReadable = true;
                model.importAnimation = false;
                model.materialImportMode = ModelImporterMaterialImportMode.None;
                model.SaveAndReimport();
            }
            var normal = (TextureImporter)AssetImporter.GetAtPath(artFolder + "/T_FoodTruck_N.png");
            if (normal.textureType != TextureImporterType.NormalMap)
            {
                normal.textureType = TextureImporterType.NormalMap;
                normal.SaveAndReimport();
            }
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
            foreach (var suffix in new[] { "D", "E", "EM_1", "EM_2", "MaskMap", "N" })
            {
                var from = AssetDatabase.AssetPathToGUID($"{exampleFolder}/T_HonzaCar_{suffix}.png");
                var to = AssetDatabase.AssetPathToGUID($"{artFolder}/T_FoodTruck_{suffix}.png");
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                    throw new InvalidOperationException($"Texture T_HonzaCar_{suffix} or T_FoodTruck_{suffix} not found.");
                if (!text.Contains(from)) throw new InvalidOperationException($"{ExampleMaterial} doesn't use T_HonzaCar_{suffix}.");
                text = text.Replace(from, to);
            }
            text = text.Replace("m_Name: M_HonzaCar", "m_Name: M_FoodTruck");
            File.WriteAllText(Path.Combine(ProjectRoot, path), text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Material>(path)
                   ?? throw new InvalidOperationException($"{path} didn't import.");
        }

        // A box under the window's equipment: x 0.82 to 1.10, 25 mm thick, along the whole opening (z -1.98 to -0.22).
        // Kept as an asset so the prefab can reference it.
        private static Mesh LedgeMesh(string artFolder)
        {
            var path = artFolder + "/TruckLedge.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "TruckLedge" };
                AssetDatabase.CreateAsset(mesh, path);
            }
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var min = new Vector3(0.82f, LedgeTop - 0.025f, -1.98f);
            var max = new Vector3(1.10f, LedgeTop, -0.22f);
            mesh.Clear();
            mesh.vertices = cube.vertices.Select(v => Vector3.Scale(v + Vector3.one * 0.5f, max - min) + min).ToArray();
            mesh.normals = cube.normals;
            mesh.uv = cube.uv;
            mesh.triangles = cube.triangles;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // Plain brushed steel for the ledge, HDRP/Lit without textures.
        private static Material SteelMaterial(string artFolder)
        {
            var path = artFolder + "/M_TruckLedge.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit shader not found.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", new Color(0.72f, 0.74f, 0.75f));
            material.SetFloat("_Metallic", 0.85f);
            material.SetFloat("_Smoothness", 0.55f);
            material.SetFloat("_SupportDecals", 0f);
            material.EnableKeyword("_DISABLE_DECALS");
            EditorUtility.SetDirty(material);
            return material;
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
