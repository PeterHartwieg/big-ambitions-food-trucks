#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Vehicles.VehicleTypes;

namespace FoodTrucks.BuildEditor
{
    // The push cart with the final model from art/cart (art/README.md, "Food cart"); tools/build-mod.sh copies
    // the FBX and texture into Vehicles/CartArt first. Its ground-level origin is at the body centre / wheel axle.
    // HandTruck.EnterVehicle parents the cart at the player root, then a negative playerOffsetWhenActive
    // moves that root forward while keeping the player model in place. The cart turns around its centre,
    // with the pusher's feet PusherDistance behind it and the body facing +z.
    internal static class CartAssets
    {
        private const string HandVehicleTag = "ba:vehicletag_ishandvehicle";
        private const int CargoSlots = 6; // The game's cargo slots; food units and equipment are limited by the mod.
        private const float CartPrice = 1200f; // Placeholder until the economy milestone.
        // Grip measured from the vanilla hand truck (logged at city load, Milestone 1 session): hands
        // 1.16 m high, 0.30 m either side, 0.65 m ahead of the player. The handle sits at the body's rear edge.
        private const float GripHeight = 1.16f;
        private const float GripHalfWidth = 0.30f;
        private const float GripReach = 0.65f;
        private const float HandleZ = -0.75f;
        private const float PusherDistance = -HandleZ + GripReach;

        private const float SpotHeight = 0.963f;
        private const float SpotZ = 0.30f;

        // The root collider and nav obstacle, widened to the wheels; the umbrella stays outside it.
        private static readonly Vector3 BodyCenter = new Vector3(0f, 0.6f, 0f);
        private static readonly Vector3 BodySize = new Vector3(0.96f, 1.2f, 1.5f);

        // The FBX nodes the prefab shows, with the positions art/README.md gives (checked against the file).
        private static readonly (string Name, Vector3 Position)[] Parts =
        {
            ("Cart_Body", Vector3.zero),
            ("Cart_Wheel_L", new Vector3(-0.455f, 0.25f, 0f)),
            ("Cart_Wheel_R", new Vector3(0.455f, 0.25f, 0f)),
            ("Cart_Lid", new Vector3(0f, 0.95f, 0.025f)),
            // The umbrella's canopy, open while the cart sells and furled round the pole otherwise (CartParts).
            ("Cart_Canopy", Vector3.zero),
            ("Cart_Canopy_Furled", Vector3.zero),
        };

        // The cash register (art/props/CashRegister) on the counter's front right corner, beside the umbrella
        // pole and facing the cart's right (+X), the side its door is on. Its 0.13 x 0.15 m footprint covers
        // x 0.22 to 0.36, z 0.54 to 0.69: 0.33 m from the front equipment spot (a piece reaches 0.27 m), 0.19 m
        // from the pole, and far from the push bar and the hands at z -0.75 (art/README.md, "Food cart").
        private static readonly Vector3 RegisterPosition = new Vector3(0.29f, 0.95f, 0.615f);
        private static readonly Quaternion RegisterRotation = Quaternion.Euler(0f, 90f, 0f);

        public static void GenerateCart(string folder, string typeName)
        {
            var typePath = folder + "/FoodCart.asset";
            var prefabPath = folder + "/FoodCart.prefab";

            var type = AssetDatabase.LoadAssetAtPath<VehicleType>(typePath);
            if (type == null)
            {
                type = ScriptableObject.CreateInstance<VehicleType>();
                AssetDatabase.CreateAsset(type, typePath);
            }
            type.name = "FoodCart";
            type.vehicleTypeName = typeName;
            type.price = CartPrice;
            type.itemVersion = string.Empty;
            type.maxFuel = 0f;
            type.maxCargoCapacity = CargoSlots;
            type.maxSpeed = 5;
            type.fitsHandTruck = false;
            type.fitsFlatbed = false;
            type.autoParkSupported = false;
            type.taxDeductible = true;
            type.hasRadio = false;
            type.isLuxuryCar = false;
            type.countsForPersonalGoals = false;
            type.spawnInPlayerObject = true;
            type.usePedestrianCam = true;
            type.autoDestroyAfterMinutes = -1;
            type.enclosed = false;
            type.canGetDirty = false;
            SetTags(type, HandVehicleTag);
            EditorUtility.SetDirty(type);

            var artFolder = folder + "/CartArt";
            var root = BuildCart(type, Meshes(artFolder), ArtMaterial(artFolder));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void SetTags(VehicleType type, params string[] tags)
        {
            var serialized = new SerializedObject(type);
            var list = serialized.FindProperty("tags");
            list.ClearArray();
            for (var i = 0; i < tags.Length; i++)
            {
                list.InsertArrayElementAtIndex(i);
                list.GetArrayElementAtIndex(i).stringValue = tags[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject BuildCart(VehicleType type, Dictionary<string, (Mesh Mesh, Vector3 Position)> meshes, Material material)
        {
            var root = new GameObject("FoodCart");
            root.tag = "Vehicle";
            root.layer = LayerMask.NameToLayer("Vehicles");

            // Clickable collider on the root: the mouse raycast looks for the controller on the hit object itself.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = BodyCenter;
            collider.size = BodySize;

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = collider.center;
            obstacle.size = collider.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.loop = true;
            audio.spatialBlend = 1f;

            var renderers = new List<Renderer>();
            foreach (var (name, _) in Parts)
            {
                var part = new GameObject(name);
                part.layer = root.layer;
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = meshes[name].Position;
                part.AddComponent<MeshFilter>().sharedMesh = meshes[name].Mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderers.Add(renderer);
            }
            // Closed by default; CartParts opens the canopy while the cart is open for business.
            root.transform.Find("Cart_Canopy").gameObject.SetActive(false);
            renderers.Add(PropAssets.AddRegister(root.transform, RegisterPosition, RegisterRotation, root.layer));

            // The parking lanes keep a spot free only for player vehicles with a collider at this path
            // (ParkingLaneGenerator.AddPlayerVehicleObstacles reads it through VehicleHelper.TryGetBodyColliderBounds).
            // A trigger on Ignore Raycast, so it neither blocks anything nor catches the mouse; the root collider
            // stays the one the game clicks and switches off while pushing. CartParts switches this one with it.
            var holder = new GameObject("CarHolder").transform;
            holder.SetParent(root.transform, false);
            var colliders = new GameObject("Colliders").transform;
            colliders.SetParent(holder, false);
            var body = new GameObject("BodyCollider");
            body.layer = LayerMask.NameToLayer("Ignore Raycast");
            body.transform.SetParent(colliders, false);
            var bodyCollider = body.AddComponent<BoxCollider>();
            bodyCollider.isTrigger = true;
            bodyCollider.center = BodyCenter;
            bodyCollider.size = BodySize;

            // No cargo boxes on the cart: stock is inside the cabinet (plan, "Cart counter layout"). HandTruck only
            // switches the placeholder's children on and off by the cargo count, so an empty placeholder inside the
            // body keeps the game's cargo logic and shows nothing.
            var boxes = Point(root.transform, "BoxPlaceholder", new Vector3(0f, 0.5f, 0f));

            // The two equipment spots, centred across the counter, each for a 0.40 x 0.36 m piece that may turn
            // to either side (radius 0.27 m): the front one behind the umbrella pole (z 0.60), the rear one in front
            // of the push bar (z -0.75). Just above the counter and the closed steam-well lid (top 0.962).
            // VehicleDisplays puts the fitted pieces here.
            Point(root.transform, "EquipmentFront", new Vector3(0f, SpotHeight, SpotZ));
            Point(root.transform, "EquipmentRear", new Vector3(0f, SpotHeight, -SpotZ));

            var leftHand = Point(root.transform, "LeftHandIK", new Vector3(-GripHalfWidth, GripHeight, HandleZ));
            var rightHand = Point(root.transform, "RightHandIK", new Vector3(GripHalfWidth, GripHeight, HandleZ));
            // The mod copies vanilla hand truck IK rotations at runtime.
            // Where the player walks to grab the cart and to load it: just behind the handle.
            var loading = Point(root.transform, "LoadingPosition", new Vector3(0f, 0f, -PusherDistance));
            var navTarget = Point(root.transform, "NavMeshTarget", new Vector3(0f, 0f, -PusherDistance));

            var truck = root.AddComponent<HandTruck>();
            truck.vehicleType = type;
            truck.boxPlaceholder = boxes;
            truck.LHandIKAttachmentPoint = leftHand;
            truck.RHandIKAttachmentPoint = rightHand;
            truck.playerOffsetWhenActive = -PusherDistance;
            truck.navMeshObstacle = obstacle;
            truck.vehicleCollider = collider;
            truck.vehicleLoadingPosition = loading;
            truck.showCargoInItemPanel = true;

            var serialized = new SerializedObject(truck);
            // Vanilla vehicle overlays: hover outline and click popup.
            SetInt(serialized, "simpleOverlayType", 5);
            SetInt(serialized, "detailedOverlayType", 16);
            SetObject(serialized, "rollingAudioSource", audio);
            SetArray(serialized, "renderers", renderers.ToArray());
            SetArray(serialized, "navMeshTargets", new UnityEngine.Object[] { navTarget });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SurfaceLook.NoStreetDecals(root);
            return root;
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath)!;

        // Meshes and node positions from the FBX; a node away from art/README.md's table fails the build,
        // since every attachment point above was placed against that table.
        private static Dictionary<string, (Mesh Mesh, Vector3 Position)> Meshes(string artFolder)
        {
            var fbxPath = artFolder + "/FoodCart_Model.fbx";
            if (!File.Exists(Path.Combine(ProjectRoot, fbxPath)))
                throw new InvalidOperationException($"{fbxPath} missing; tools/build-mod.sh copies it from art/cart.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            if (SurfaceLook.NormalsAsBaked(importer) || importer.isReadable || importer.importAnimation || importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.globalScale != 1f)
            {
                importer.isReadable = false;
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.globalScale = 1f;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var found = model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name, f => (f.sharedMesh, f.transform.localPosition, f.transform.localRotation));
            var meshes = new Dictionary<string, (Mesh, Vector3)>();
            foreach (var (name, expected) in Parts)
            {
                if (!found.TryGetValue(name, out var node)) throw new InvalidOperationException($"{fbxPath} lacks {name}.");
                if ((node.localPosition - expected).magnitude > 0.005f || Quaternion.Angle(node.localRotation, Quaternion.identity) > 0.5f)
                    throw new InvalidOperationException($"{name} in {fbxPath} is at {node.localPosition:F3}, rotation {node.localRotation.eulerAngles:F1}; art/README.md has {expected:F3}, rotation 0.");
                meshes[name] = (node.sharedMesh, node.localPosition);
                var b = node.sharedMesh.bounds;
                Debug.Log($"[FoodTrucksBuild] Cart mesh {name}: {node.sharedMesh.triangles.Length / 3} tris, at {node.localPosition:F3}, bounds {(b.min + node.localPosition):F3} to {(b.max + node.localPosition):F3}.");
            }
            return meshes;
        }

        // One HDRP/Lit material with the model's surface maps for every visible mesh (art/README.md, "Surface look").
        private static Material ArtMaterial(string artFolder) =>
            SurfaceLook.LitMaterial(artFolder + "/M_FoodCart.mat", artFolder, "FoodCart");

        private static Transform Point(Transform parent, string name, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        private static void SetObject(SerializedObject serialized, string field, UnityEngine.Object value)
        {
            var property = serialized.FindProperty(field) ?? throw new InvalidOperationException($"Field {field} not found on {serialized.targetObject.GetType().Name}.");
            property.objectReferenceValue = value;
        }

        private static void SetInt(SerializedObject serialized, string field, int value)
        {
            var property = serialized.FindProperty(field) ?? throw new InvalidOperationException($"Field {field} not found on {serialized.targetObject.GetType().Name}.");
            property.intValue = value;
        }

        private static void SetArray(SerializedObject serialized, string field, UnityEngine.Object[] values)
        {
            var property = serialized.FindProperty(field) ?? throw new InvalidOperationException($"Field {field} not found on {serialized.targetObject.GetType().Name}.");
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
