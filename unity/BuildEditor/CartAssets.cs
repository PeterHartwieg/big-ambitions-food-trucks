#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Vehicles.VehicleTypes;

namespace FoodTrucks.BuildEditor
{
    // Spike 2: a placeholder push cart built from primitives and wired like the game's hand truck.
    // Box sizes are in metres: about 1.6 long, 0.8 wide, 1.0 high, with handles at the back.
    internal static class CartAssets
    {
        private const string HandVehicleTag = "ba:vehicletag_ishandvehicle";
        private const int CargoSlots = 6;

        public static void GenerateCart(string folder, string typeName)
        {
            var typePath = folder + "/SpikeCart.asset";
            var prefabPath = folder + "/SpikeCart.prefab";

            var type = AssetDatabase.LoadAssetAtPath<VehicleType>(typePath);
            if (type == null)
            {
                type = ScriptableObject.CreateInstance<VehicleType>();
                AssetDatabase.CreateAsset(type, typePath);
            }
            type.vehicleTypeName = typeName;
            type.price = 1f;
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

            var root = BuildCart(type);
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

        private static GameObject BuildCart(VehicleType type)
        {
            var root = new GameObject("SpikeCart");
            root.tag = "Vehicle";
            root.layer = LayerMask.NameToLayer("Vehicles");

            // Clickable collider on the root: the mouse raycast looks for the controller on the hit object itself.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.55f, 0f);
            collider.size = new Vector3(0.8f, 1.1f, 1.6f);

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

            var renderers = new List<Renderer>
            {
                Box(root.transform, "Body", new Vector3(0f, 0.6f, 0.05f), new Vector3(0.8f, 0.7f, 1.4f), new Color(0.85f, 0.25f, 0.2f)),
                Box(root.transform, "Canopy", new Vector3(0f, 1.25f, 0.05f), new Vector3(0.95f, 0.05f, 1.55f), new Color(0.95f, 0.9f, 0.8f)),
                Box(root.transform, "Handle", new Vector3(0f, 0.95f, -0.8f), new Vector3(0.7f, 0.05f, 0.05f), Color.gray),
                Box(root.transform, "WheelLeft", new Vector3(-0.42f, 0.18f, 0.3f), new Vector3(0.06f, 0.36f, 0.36f), Color.black),
                Box(root.transform, "WheelRight", new Vector3(0.42f, 0.18f, 0.3f), new Vector3(0.06f, 0.36f, 0.36f), Color.black),
            };

            // One child per cargo slot; HandTruck shows the first N children for N cargo entries.
            var boxes = new GameObject("BoxPlaceholder").transform;
            boxes.SetParent(root.transform, false);
            boxes.localPosition = new Vector3(0f, 0.95f, 0.05f);
            for (var i = 0; i < CargoSlots; i++)
            {
                var box = Box(boxes, "Box" + i, new Vector3((i % 2 - 0.5f) * 0.35f, 0.12f, (i / 2 - 1) * 0.4f), new Vector3(0.3f, 0.2f, 0.3f), new Color(0.6f, 0.45f, 0.3f));
                box.gameObject.SetActive(false);
            }

            var leftHand = Point(root.transform, "LeftHandIK", new Vector3(-0.3f, 0.95f, -0.8f));
            var rightHand = Point(root.transform, "RightHandIK", new Vector3(0.3f, 0.95f, -0.8f));
            var loading = Point(root.transform, "LoadingPosition", new Vector3(0f, 0f, -1.3f));
            var navTarget = Point(root.transform, "NavMeshTarget", new Vector3(0f, 0f, -1.1f));

            var truck = root.AddComponent<HandTruck>();
            truck.vehicleType = type;
            truck.boxPlaceholder = boxes;
            truck.LHandIKAttachmentPoint = leftHand;
            truck.RHandIKAttachmentPoint = rightHand;
            truck.playerOffsetWhenActive = 0.9f;
            truck.navMeshObstacle = obstacle;
            truck.vehicleCollider = collider;
            truck.vehicleLoadingPosition = loading;
            truck.showCargoInItemPanel = true;

            var serialized = new SerializedObject(truck);
            SetObject(serialized, "rollingAudioSource", audio);
            SetArray(serialized, "renderers", renderers.ToArray());
            SetArray(serialized, "navMeshTargets", new UnityEngine.Object[] { navTarget });
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static Renderer Box(Transform parent, string name, Vector3 position, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Material(color);
            return renderer;
        }

        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        // HDRP Lit materials saved next to the prefab, one per colour.
        private static Material Material(Color color)
        {
            if (Materials.TryGetValue(color, out var existing) && existing != null) return existing;
            var path = $"{ModAssets.ModFolder}/Vehicles/Materials/Cart_{ColorUtility.ToHtmlStringRGB(color)}.mat";
            ModAssets.EnsureFolder(ModAssets.ModFolder + "/Vehicles/Materials");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit shader not found.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            Materials[color] = material;
            return material;
        }

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

        private static void SetArray(SerializedObject serialized, string field, UnityEngine.Object[] values)
        {
            var property = serialized.FindProperty(field) ?? throw new InvalidOperationException($"Field {field} not found on {serialized.targetObject.GetType().Name}.");
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
