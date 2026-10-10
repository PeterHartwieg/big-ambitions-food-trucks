#nullable enable
using System;
using System.IO;
using System.Linq;
using BigAmbitions.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks.BuildEditor
{
    // The mod's own game items (plan, "New foods"; docs/balance/new-foods.md): four foods and the three machines
    // that make them. Item assets in Items/, one prefab per machine in Items/Prefabs and one icon per item in
    // Items/Icons (rendered by art/tools/render_icon.py). FoodTrucks.ModItems registers them at load.
    //
    // The game finds an item's prefab and icon by the part of its name after "itemname_": Prefabs/<id>.prefab,
    // first in its own Addressables and then by file name in any mod's bundle (PrefabHelper.LoadPrefab), and
    // ItemIcons/<id>.png in its icon cache (ItemHelper.GetIcon). So the prefabs are named <id>.prefab, and no other
    // asset in the bundle may share a machine's id (EquipmentAssets' suffixes). The foods need no prefab: the game
    // shows them as boxes in hands and cargo, like its own foods.
    //
    // Field values follow the game's own items, read from its 1.0 item bundle: the foods like the hot dog and
    // kabob, the machines like the industrial grill and hot dog grill. Prices are the balance proposal's
    // (tools/balance/new-foods-proposal.json, "game"); boxes hold 80 (Peter, 9 October 2026).
    internal static class ItemAssets
    {
        public const string Folder = ModAssets.ModFolder + "/Items";
        private const string PrefabFolder = Folder + "/Prefabs";
        private const string IconFolder = Folder + "/Icons";
        private const string EquipmentArtFolder = ModAssets.ModFolder + "/Vehicles/EquipmentArt";
        private const string Prefix = "foodtrucks:itemname_";
        private const int BoxSize = 80;

        private sealed class Food
        {
            public Food(string id, string asset, float price, float wholesale, float salesRatio, int saturation)
            {
                Id = id;
                Asset = asset;
                Price = price;
                Wholesale = wholesale;
                SalesRatio = salesRatio;
                Saturation = saturation;
            }

            public string Id { get; }
            public string Asset { get; }
            public float Price { get; }
            public float Wholesale { get; }
            // The game's productSalesRatio: hot dog and kabob 0.66, popcorn and slushi 0.85.
            public float SalesRatio { get; }
            // How much a customer's hunger it stills: hot dog 60, kabob 90.
            public int Saturation { get; }
        }

        private sealed class Machine
        {
            public Machine(string id, string model, float price, int customersPerHour, params string[] shows)
            {
                Id = id;
                Model = model;
                Price = price;
                CustomersPerHour = customersPerHour;
                Shows = shows;
            }

            public string Id { get; }
            // The compact model in Vehicles/EquipmentArt, the same one shown on a vehicle's counter.
            public string Model { get; }
            public float Price { get; }
            // The game's addedCustomersPerHour: industrial grill 20, hot dog grill 30. Used only in buildings.
            public int CustomersPerHour { get; }
            public string[] Shows { get; }
        }

        private static readonly Food[] Foods =
        {
            new Food("dumplings", "Dumplings", 13.5f, 5.1f, 0.66f, 70),
            new Food("tacos", "Tacos", 13.0f, 4.9f, 0.66f, 60),
            new Food("choppedcheese", "ChoppedCheese", 12.0f, 4.55f, 0.66f, 80),
            new Food("grilledcorn", "GrilledCorn", 7.0f, 2.5f, 0.85f, 40),
        };

        private static readonly Machine[] Machines =
        {
            new Machine("dumplingsteamer", "DumplingSteamer", 2000f, 20, "dumplings"),
            new Machine("griddle", "Griddle", 2000f, 20, "tacos", "choppedcheese"),
            new Machine("corngrill", "CornGrill", 1200f, 30, "grilledcorn"),
            // Extra Storage (plan, "Storage per piece"): serves no food, doubles every fitted piece's storage. Priced
            // below the cheapest machine (docs/balance/storage-per-piece.md).
            new Machine("storagecabinet", "StorageCabinet", 900f, 0),
        };

        public static void Generate()
        {
            ModAssets.EnsureFolder(PrefabFolder);
            ModAssets.EnsureFolder(IconFolder);
            foreach (var food in Foods)
            {
                var item = LoadOrCreate(food.Asset);
                var s = new SerializedObject(item);
                Common(s, food.Id, ItemType.RetailProduct, food.Price);
                Set(s, "wholesalePrice", food.Wholesale);
                Set(s, "productSalesRatio", food.SalesRatio);
                Set(s, "boxSize", BoxSize);
                Set(s, "isConsumable", true);
                Set(s, "consumeAnimation", 1);
                Set(s, "consumeSoundType", 18);
                Set(s, "saturation", food.Saturation);
                // No market entry: the game would keep a ProductMarketEntry for the item in the save, and once the mod is
                // gone its daily market update throws on the unknown item. Core treats the food as unsaturated instead.
                Set(s, "isADemandedProduct", false);
                SetStrings(s, "limitDemandToNeighbourhoods");
                Set(s, "maxWholesaleOrderAmount", 5000);
                Set(s, "maxOrderAmountPerImporter", 25000);
                Set(s, "providersNeededToStartGeneratingEvents", 6);
                Set(s, "usingAnimation", 34);
                s.ApplyModifiedPropertiesWithoutUndo();
                Icon(food.Id);
                Debug.Log($"[FoodTrucksBuild] Item {Prefix}{food.Id}: ${food.Price} (wholesale ${food.Wholesale}), box {BoxSize}.");
            }

            foreach (var machine in Machines)
            {
                var item = LoadOrCreate(machine.Model);
                var s = new SerializedObject(item);
                // A showcase shelf, as the game's grills: it holds and shows the foods it makes.
                // Extra Storage shows nothing: a plain decoration type, so no game system treats it as a shelf.
                Common(s, machine.Id, machine.Shows.Length == 0 ? ItemType.Decoration : ItemType.ShowcaseShelf, machine.Price);
                Set(s, "boxSize", 1);
                // Street food only in version 1: not furniture, so the game offers no "Place" for it in a building
                // (CargoItemUi, PackageOverlay and ItemPanelUI all require isFurniture). It still rides in cargo as equipment.
                Set(s, "isFurniture", false);
                Set(s, "gridSize", 0.5f);
                Set(s, "degreesPerRotation", 45f);
                Set(s, "addedCustomersPerHour", machine.CustomersPerHour);
                Set(s, "cargoCapacityMultiplier", 0.2f);
                Set(s, "stackType", (int)AttachmentPointType.WorkSurface);
                Set(s, "defaultShelfItemName", machine.Shows.Length == 0 ? string.Empty : Prefix + machine.Shows[0]);
                SetStrings(s, "itemsThatCanShowcase", machine.Shows.Select(f => Prefix + f).ToArray());
                Set(s, "maxWholesaleOrderAmount", 5000);
                Set(s, "maxOrderAmountPerImporter", 5000);
                Set(s, "providersNeededToStartGeneratingEvents", 7);
                Set(s, "usingAnimation", 28);
                s.ApplyModifiedPropertiesWithoutUndo();
                MachinePrefab(machine);
                Icon(machine.Id);
                Debug.Log($"[FoodTrucksBuild] Item {Prefix}{machine.Id}: ${machine.Price}, shows {(machine.Shows.Length == 0 ? "nothing" : string.Join(", ", machine.Shows))}.");
            }
        }

        private static Item LoadOrCreate(string asset)
        {
            var path = $"{Folder}/{asset}.asset";
            var item = AssetDatabase.LoadAssetAtPath<Item>(path);
            if (item != null) return item;
            item = ScriptableObject.CreateInstance<Item>();
            AssetDatabase.CreateAsset(item, path);
            return item;
        }

        // Every field the generator owns, so a rebuilt asset never keeps an old value by accident.
        private static void Common(SerializedObject s, string id, ItemType type, float price)
        {
            SetStrings(s, "tags");
            Set(s, "itemName", Prefix + id);
            Set(s, "type", (int)type);
            Set(s, "wholesalePrice", 0f);
            Set(s, "defaultMarketPrice", price);
            Set(s, "productSalesRatio", 0f);
            Set(s, "canPutInShoppingBasket", false);
            Set(s, "showBoxAsHandItem", true);
            Set(s, "isFurniture", false);
            Set(s, "addedCustomersPerHour", 0);
            Set(s, "cargoCapacity", 0);
            Set(s, "cargoCapacityMultiplier", 0f);
            Set(s, "canBeGrabbed", true);
            Set(s, "stackType", 0);
            Set(s, "isProducer", false);
            Set(s, "defaultShelfItemName", string.Empty);
            SetStrings(s, "itemsThatCanShowcase");
            Set(s, "isConsumable", false);
            Set(s, "consumeAnimation", 0);
            Set(s, "consumeSoundType", 0);
            Set(s, "saturation", 0);
            Set(s, "isADemandedProduct", false);
            Set(s, "placeSoundType", 7);
        }

        // An Item-shaped prefab around the compact model, built like the SDK's GigaCounter: mesh, collider and
        // nav obstacle on the root with the ItemController, and the game's ground indicator, direction indicator
        // and nav mesh target as children (the SDK's ItemControllerAutoSetup wires the same lists).
        private static void MachinePrefab(Machine machine)
        {
            var fbxPath = EquipmentAssets.ModelPath(EquipmentArtFolder, machine.Model);
            var filter = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath)?.GetComponentsInChildren<MeshFilter>(true)
                             .FirstOrDefault(f => f.name == machine.Model + "_Body")
                         ?? throw new InvalidOperationException($"{fbxPath} or its {machine.Model}_Body missing; EquipmentAssets runs first.");
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{EquipmentArtFolder}/M_{machine.Model}.mat")
                           ?? throw new InvalidOperationException($"M_{machine.Model}.mat missing; EquipmentAssets runs first.");
            var bounds = filter.sharedMesh.bounds;

            var root = new GameObject(machine.Id);
            try
            {
                root.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var collider = root.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = bounds.size;
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = bounds.center;
                obstacle.size = bounds.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;

                var ground = Child("Assets/_BaDependencies/Prefabs/PlacementSystem/GroundIndicatorSmall.prefab", root.transform, new Vector3(0f, 0.001f, 0f));
                var direction = Child("Assets/_BaDependencies/Prefabs/PlacementSystem/DirectionIndicator.prefab", root.transform, new Vector3(0f, 0f, bounds.max.z + 0.1f));
                // Where a worker stands to use it: in front.
                var target = Child("Assets/_BaDependencies/Resources/NavMeshTarget.prefab", root.transform, new Vector3(0f, 0f, bounds.max.z + 0.5f));

                var controller = root.AddComponent<ItemController>();
                var s = new SerializedObject(controller);
                Set(s, "itemName", Prefix + machine.Id);
                Set(s, "primaryInteractionEnabled", true);
                Set(s, "simpleOverlayType", 8);
                Set(s, "allowResting", true);
                Set(s, "checkRenderersDepth", -1);
                Set(s, "shadowCasterRenderingLayerMask", 16);
                SetObjects(s, "renderers", renderer);
                SetObjects(s, "colliders", collider);
                SetObjects(s, "navMeshObstacles", obstacle);
                SetObjects(s, "navMeshTargets", target.transform);
                SetObjects(s, "directionIndicators", direction);
                SetObjects(s, "attachmentPoints");
                var grounds = s.FindProperty("groundIndicators") ?? throw new InvalidOperationException("ItemController.groundIndicators not found.");
                grounds.arraySize = 1;
                grounds.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = ground.transform;
                grounds.GetArrayElementAtIndex(0).FindPropertyRelative("renderer").objectReferenceValue = ground.GetComponent<Renderer>();
                s.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{machine.Id}.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject Child(string prefabPath, Transform parent, Vector3 position)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) ?? throw new InvalidOperationException($"{prefabPath} not found in the SDK.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.transform.localPosition = position;
            return instance;
        }

        // The icon PNG, committed, imported as a single sprite for bundle.LoadAsset<Sprite>.
        private static void Icon(string id)
        {
            var path = $"{IconFolder}/{id}.png";
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, path)))
                throw new InvalidOperationException($"{path} missing; render it with art/tools/render_icon.py.");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        private static SerializedProperty Find(SerializedObject s, string field) =>
            s.FindProperty(field) ?? throw new InvalidOperationException($"Field {field} not found on {s.targetObject.GetType().Name}.");

        private static void Set(SerializedObject s, string field, string value) => Find(s, field).stringValue = value;
        private static void Set(SerializedObject s, string field, int value) => Find(s, field).intValue = value;
        private static void Set(SerializedObject s, string field, float value) => Find(s, field).floatValue = value;
        private static void Set(SerializedObject s, string field, bool value) => Find(s, field).boolValue = value;

        private static void SetStrings(SerializedObject s, string field, params string[] values)
        {
            var property = Find(s, field);
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        private static void SetObjects(SerializedObject s, string field, params UnityEngine.Object[] values)
        {
            var property = Find(s, field);
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
