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
    internal static class CartAssets
    {
        private const string HandVehicleTag = "ba:vehicletag_ishandvehicle";
        private const int CargoSlots = 6;
        private const float CartPrice = 1200f; // Placeholder until the economy milestone.
        private const float GripHeight = 1.16f;
        private const float GripHalfWidth = 0.30f;
        private const float GripReach = 0.65f;
        private const float HandleZ = -0.75f;
        private const float PusherDistance = -HandleZ + GripReach;

        private const float SpotHeight = 0.963f;
        private const float SpotZ = 0.30f;

        private static readonly Vector3 BodyCenter = new Vector3(0f, 0.6f, 0f);
        private static readonly Vector3 BodySize = new Vector3(0.96f, 1.2f, 1.5f);

        private static readonly (string Name, Vector3 Position)[] Parts =
        {
            ("Cart_Body", Vector3.zero),
            ("Cart_Wheel_L", new Vector3(-0.455f, 0.25f, 0f)),
            ("Cart_Wheel_R", new Vector3(0.455f, 0.25f, 0f)),
            ("Cart_Lid", new Vector3(0f, 0.95f, 0.025f)),
            ("Cart_Canopy", Vector3.zero),
            ("Cart_Canopy_Furled", Vector3.zero),
        };

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

            var boxes = Point(root.transform, "BoxPlaceholder", new Vector3(0f, 0.5f, 0f));

            Point(root.transform, "EquipmentFront", new Vector3(0f, SpotHeight, SpotZ));
            Point(root.transform, "EquipmentRear", new Vector3(0f, SpotHeight, -SpotZ));
            Point(root.transform, "EquipmentCooler", new Vector3(-0.443f, 0.525f, -0.32f));

            var leftHand = Point(root.transform, "LeftHandIK", new Vector3(-GripHalfWidth, GripHeight, HandleZ));
            var rightHand = Point(root.transform, "RightHandIK", new Vector3(GripHalfWidth, GripHeight, HandleZ));
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
