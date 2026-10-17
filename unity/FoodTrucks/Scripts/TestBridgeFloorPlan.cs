#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using BigAmbitions.PlacementSystem;
using Helpers;
using Localizor;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using Placement = BigAmbitions.PlacementSystem.PlacementSystem;

namespace FoodTrucks
{
    internal static partial class TestBridgeCommands
    {
        // The default Item.gridSize, in world metres. Item-specific snapping still belongs to PlacementSystem.
        private const float PlanSquareSize = 0.5f;
        private const int MaxPlanSquares = 30000;

        private sealed class FloorPlanGrid
        {
            internal Transform Root = null!;
            internal MultipleHeightsBuildingController? Heights;
            internal Collider[] Floors = Array.Empty<Collider>();
            internal int Floor, MinX, MinZ, Columns, Rows;
            internal float Y;
            internal Vector3 Point(int column, int row) => new Vector3((MinX + column) * PlanSquareSize, Y, (MinZ + row) * PlanSquareSize);
            internal string? Square(Vector3 p)
            {
                var snapped = Extensions.MathHelper.RoundVectorToNextValue(p, PlanSquareSize);
                var col = Mathf.RoundToInt(snapped.x / PlanSquareSize) - MinX;
                var row = Mathf.RoundToInt(snapped.z / PlanSquareSize) - MinZ;
                return col < 0 || col >= Columns || row < 0 || row >= Rows ? null : ColumnName(col) + (row + 1);
            }
            internal Vector3 Resolve(string square)
            {
                var m = System.Text.RegularExpressions.Regex.Match(square.ToUpperInvariant(), @"^([A-Z]+)([1-9][0-9]*)$");
                if (!m.Success || m.Groups[1].Length > 4 || !int.TryParse(m.Groups[2].Value, out var row))
                    throw new BridgeError("A square is a column and row, for example C4; read floorplan first.");
                var column = 0;
                foreach (var c in m.Groups[1].Value) column = column * 26 + c - 'A' + 1;
                if (column > Columns || row > Rows) throw new BridgeError($"Square {square} is outside this floor plan; read floorplan again.");
                return Point(column - 1, row - 1);
            }
            internal bool Hit(Vector3 p, out RaycastHit hit)
            {
                // Only this floor's actual ground geometry. No bounds-filled floor, lower level, or street fallback.
                hit = default;
                var found = false;
                foreach (var c in Floors)
                    if (c.Raycast(new Ray(new Vector3(p.x, Y + .35f, p.z), Vector3.down), out var h, .7f) &&
                        (!found || h.point.y > hit.point.y)) { hit = h; found = true; }
                return found;
            }
        }

        private static bool PlacementRotationInput()
        {
            foreach (var name in new[] { "RotateInputTriggered", "FreeRotateInputTriggering" })
            {
                var getter = HarmonyLib.AccessTools.PropertyGetter(typeof(Placement), name);
                if (getter == null) throw new BridgeError("The game's placement input guard wasn't found (game update?).");
                if (getter.Invoke(null, null) is bool active && active) return true;
            }
            return false;
        }

        private static JArray CargoSnapshot(IEnumerable<CargoInstance>? cargo) => new JArray(
            (cargo ?? Enumerable.Empty<CargoInstance>()).Select(c => new JObject
            {
                ["key"] = c.itemName, ["amount"] = c.amount, ["paid"] = c.paid, ["price"] = c.pricePerUnit,
                ["colours"] = c.customColors == null ? null : JToken.FromObject(c.customColors),
                ["nested"] = c.nestedCargoInstances == null ? null : JToken.FromObject(c.nestedCargoInstances),
            }));

        // Internal conservation evidence only. No price/balance fields are returned by floorplan or place.
        private static JObject PlacementSnapshot()
        {
            var hands = PlayerHelper.ItemInstanceInHands;
            return new JObject
            {
                ["hands"] = hands == null ? null : new JObject { ["key"] = hands.itemName, ["cargo"] = CargoSnapshot(hands.cargoInstances) },
                ["items"] = new JArray(InstanceBehavior<BuildingManager>.Instance.allItemControllers
                    .Where(c => c != null && c.ItemInstance != null).OrderBy(c => c.ItemInstance.id, StringComparer.Ordinal).Select(c => new JObject
                    {
                        ["id"] = c.ItemInstance.id, ["parent"] = c.ItemInstance.parentId,
                        ["position"] = JToken.FromObject(c.ItemInstance.position), ["rotation"] = JToken.FromObject(c.ItemInstance.rotation),
                        ["yaw"] = c.ItemInstance.yRotation, ["transform"] = JToken.FromObject(new[] { c.transform.position.x, c.transform.position.y, c.transform.position.z, c.transform.eulerAngles.y }),
                        ["children"] = c.ItemInstance.stackedItems == null ? null : JToken.FromObject(c.ItemInstance.stackedItems), ["cargo"] = CargoSnapshot(c.ItemInstance.cargoInstances),
                    })),
            };
        }

        private static string ColumnName(int column)
        {
            var name = "";
            for (column++; column > 0; column = (column - 1) / 26) name = (char)('A' + (column - 1) % 26) + name;
            return name;
        }

        private static FloorPlanGrid CurrentFloorPlan()
        {
            if (!BuildingManager.CanBuildOnCurrentBuilding)
                throw new BridgeError("floorplan needs the inside of a building you rent or can edit.");
            if (UI.InteriorDesigner.InteriorDesignerUI.IsOpen)
                throw new BridgeError("Close the interior designer first; floorplan follows the floor the player stands on.");
            var manager = InstanceBehavior<BuildingManager>.Instance;
            var grid = Placement.currentBuildingGrid as BuildingGridBase;
            var root = manager.currentBuildingVersion;
            if (grid == null || root == null) throw new BridgeError("The building's floor grid isn't loaded yet.");
            var me = PlayerHelper.GetPosition();
            var heights = manager.multipleHeightsBuildingController;
            var plan = new FloorPlanGrid { Root = root, Heights = heights,
                Floor = heights != null && me.y >= heights.heightToShowSecondFloor ? 1 : 0 };
            var registeredGrids = (grid.GetGroundGrids() ?? Array.Empty<GroundGrid>())
                .Where(g => g != null && g.col != null).Select(g => g.col);
            var colliders = root.GetComponentsInChildren<Collider>(false).Concat(registeredGrids).Distinct()
                .Where(c => c != null && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.GetComponentInParent<ItemController>() == null &&
                    (((1 << c.gameObject.layer) & (int)LayerHelper.groundLayerMask) != 0)).ToArray();
            // Determine physical floor height under the player's feet, before sampling elsewhere.
            var hits = colliders.Select(c => c.Raycast(new Ray(me + Vector3.up * .25f, Vector3.down), out var h, 3f)
                    ? (RaycastHit?)h : null).Where(h => h.HasValue).Select(h => h!.Value).OrderByDescending(h => h.point.y).ToArray();
            if (hits.Length == 0) throw new BridgeError("No building floor under the player; stand on the floor first.");
            plan.Y = hits[0].point.y;
            plan.Floors = colliders.Where(c => c.bounds.min.y <= plan.Y + .35f && c.bounds.max.y >= plan.Y - .35f).ToArray();
            var bounds = plan.Floors[0].bounds;
            foreach (var c in plan.Floors.Skip(1)) bounds.Encapsulate(c.bounds);
            // Include one square of perimeter so walls and exit thresholds have labelled squares too.
            plan.MinX = Mathf.CeilToInt(bounds.min.x / PlanSquareSize) - 1;
            plan.MinZ = Mathf.CeilToInt(bounds.min.z / PlanSquareSize) - 1;
            plan.Columns = Mathf.FloorToInt(bounds.max.x / PlanSquareSize) - plan.MinX + 2;
            plan.Rows = Mathf.FloorToInt(bounds.max.z / PlanSquareSize) - plan.MinZ + 2;
            if ((long)plan.Columns * plan.Rows > MaxPlanSquares) throw new BridgeError("This floor is too large to survey in one call.");
            return plan;
        }

        private static bool OnPlayerFloor(FloorPlanGrid plan, ItemController item)
            => plan.Heights == null || plan.Heights.GetItemHeightIndex(item) == plan.Floor;

        private static JObject PlanPose(FloorPlanGrid plan, Vector3 p) => new JObject
        {
            ["square"] = plan.Square(p), ["x"] = Math.Round(p.x, 3), ["y"] = Math.Round(p.y, 3), ["z"] = Math.Round(p.z, 3),
        };

        // Physical box footprints are projected oriented boxes, not their axis-aligned bounding rectangles.
        // Other collider shapes use conservative bounds, explicitly identified in the observation.
        private static List<Vector2> Footprint(Collider c)
        {
            var points = new List<Vector2>();
            if (c is BoxCollider box)
            {
                for (var x = -1; x <= 1; x += 2)
                    for (var y = -1; y <= 1; y += 2)
                        for (var z = -1; z <= 1; z += 2)
                        {
                            var p = box.transform.TransformPoint(box.center + Vector3.Scale(box.size * .5f, new Vector3(x, y, z)));
                            points.Add(new Vector2(p.x, p.z));
                        }
            }
            else
            {
                var b = c.bounds;
                points.Add(new Vector2(b.min.x, b.min.z)); points.Add(new Vector2(b.max.x, b.min.z));
                points.Add(new Vector2(b.max.x, b.max.z)); points.Add(new Vector2(b.min.x, b.max.z));
            }
            points = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            float Cross(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            var hull = new List<Vector2>();
            foreach (var p in points) { while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            var lower = hull.Count;
            foreach (var p in points.AsEnumerable().Reverse().Skip(1))
            { while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static bool Covers(List<Vector2> polygon, Vector3 point)
        {
            if (polygon.Count < 3) return false;
            var centre = new Vector2(point.x, point.z);
            var axes = new List<Vector2> { Vector2.right, Vector2.up };
            for (var i = 0; i < polygon.Count; i++)
            {
                var edge = polygon[(i + 1) % polygon.Count] - polygon[i];
                axes.Add(new Vector2(-edge.y, edge.x).normalized);
            }
            foreach (var axis in axes)
            {
                var low = polygon.Min(p => Vector2.Dot(p, axis)); var high = polygon.Max(p => Vector2.Dot(p, axis));
                var middle = Vector2.Dot(centre, axis);
                var half = (Math.Abs(axis.x) + Math.Abs(axis.y)) * PlanSquareSize * .5f;
                if (high <= middle - half + .001f || low >= middle + half - .001f) return false;
            }
            return true;
        }

        private static JArray CoveredSquares(FloorPlanGrid plan, IEnumerable<Collider> colliders)
        {
            var covered = new HashSet<int>();
            foreach (var collider in colliders.Where(c => c != null && !c.isTrigger))
            {
                var polygon = Footprint(collider);
                if (polygon.Count < 3) continue;
                var firstCol = Math.Max(0, Mathf.FloorToInt(polygon.Min(p => p.x) / PlanSquareSize - .5f) - plan.MinX);
                var lastCol = Math.Min(plan.Columns - 1, Mathf.CeilToInt(polygon.Max(p => p.x) / PlanSquareSize + .5f) - plan.MinX);
                var firstRow = Math.Max(0, Mathf.FloorToInt(polygon.Min(p => p.y) / PlanSquareSize - .5f) - plan.MinZ);
                var lastRow = Math.Min(plan.Rows - 1, Mathf.CeilToInt(polygon.Max(p => p.y) / PlanSquareSize + .5f) - plan.MinZ);
                for (var row = firstRow; row <= lastRow; row++)
                    for (var col = firstCol; col <= lastCol; col++)
                        if (Covers(polygon, plan.Point(col, row))) covered.Add(row * plan.Columns + col);
            }
            return new JArray(covered.OrderBy(i => i).Select(i => ColumnName(i % plan.Columns) + (i / plan.Columns + 1)));
        }

        private static JArray UseSquares(FloorPlanGrid plan, ItemController item)
        {
            var me = PlayerHelper.GetPosition();
            var targets = new JArray();
            foreach (var target in (NavTargetsField?.GetValue(item) as Transform[]) ?? Array.Empty<Transform>())
            {
                if (target == null) continue;
                var wall = PathHelper.IsThereAWallBetweenTargetAndEntity(target.position, item.transform.position + item.transform.forward * .1f);
                var path = new NavMeshPath();
                NavMesh.CalculatePath(me, target.position, PlayerController.navMeshQueryFilter, path);
                var accepted = path.status != NavMeshPathStatus.PathInvalid && (path.status != NavMeshPathStatus.PathPartial ||
                    (path.corners.Length > 0 && (path.corners[path.corners.Length - 1] - target.position).sqrMagnitude <= .25f));
                var entry = PlanPose(plan, target.position);
                entry["reachable"] = !wall && accepted; entry["wall_between"] = wall; entry["path"] = path.status.ToString();
                targets.Add(entry);
            }
            return targets;
        }

        private static JObject FloorPlan(JObject args)
        {
            if (Placement.IsInPlacementMode) throw new BridgeError("Finish or cancel the active placement before reading floorplan.");
            if (NavTargetsField == null) throw new BridgeError("The game's use targets weren't found (game update?).");
            var plan = CurrentFloorPlan();
            var cells = new JArray();
            var rows = new List<char[]>();
            var items = new JArray();
            var doors = new HashSet<string>();
            foreach (var exit in plan.Root.GetComponentsInChildren<ExitZone>(false))
            {
                var p = exit.door != null ? exit.door.position : exit.transform.position;
                if (Math.Abs(p.y - plan.Y) > 2f) continue;
                var doorColliders = exit.door != null ? exit.door.GetComponentsInChildren<Collider>(false) : Array.Empty<Collider>();
                foreach (var s in CoveredSquares(plan, doorColliders)) doors.Add((string)s!);
                var square = plan.Square(p); if (square != null) doors.Add(square);
            }
            for (var row = 0; row < plan.Rows; row++)
            {
                var line = new char[plan.Columns];
                for (var col = 0; col < plan.Columns; col++)
                {
                    var p = plan.Point(col, row); var square = ColumnName(col) + (row + 1);
                    var floor = plan.Hit(p, out var hit);
                    var element = floor ? hit.collider.GetComponent<InteriorElement>() : null;
                    var wall = Physics.OverlapBox(p + Vector3.up * .8f, new Vector3(.24f, .6f, .24f), Quaternion.identity,
                        (int)LayerHelper.wallsLayerMask, QueryTriggerInteraction.Ignore).Any(c => c.transform.IsChildOf(plan.Root));
                    var blocked = (element != null && element.placementBlocked) ||
                        Physics.OverlapBox(p + Vector3.up * .3f, new Vector3(.24f, .25f, .24f), Quaternion.identity,
                            (int)LayerHelper.groundUnplacableLayerMask, QueryTriggerInteraction.Ignore).Any(c => c.transform.IsChildOf(plan.Root));
                    var kind = doors.Contains(square) ? "opening" : wall ? "wall" : blocked ? "blocked" : floor ? "floor" : "void";
                    line[col] = kind == "opening" ? '=' : kind == "wall" ? '#' : kind == "blocked" ? 'X' : kind == "floor" ? '.' : ' ';
                    cells.Add(new JObject { ["square"] = square, ["kind"] = kind });
                }
                rows.Add(line);
            }
            const string labels = "ABCDEFGHIJKLMNOPQRSTUVWYZabcdefghijklmnopqrstuvwxyz0123456789";
            foreach (var item in InstanceBehavior<BuildingManager>.Instance.allItemControllers.Where(c => c != null && c.ItemInstance != null && OnPlayerFloor(plan, c))
                .OrderBy(c => c.ItemInstance.id, StringComparer.Ordinal))
            {
                var label = items.Count < labels.Length ? labels[items.Count].ToString() : '*'.ToString();
                var colliders = item.GetColliders() ?? Array.Empty<Collider>();
                var squares = CoveredSquares(plan, colliders);
                foreach (var square in squares.Values<string>())
                {
                    var p = plan.Resolve(square!); var col = Mathf.RoundToInt(p.x / PlanSquareSize) - plan.MinX; var row = Mathf.RoundToInt(p.z / PlanSquareSize) - plan.MinZ;
                    var old = rows[row][col]; rows[row][col] = labels.Contains(old) || old == '*' ? '*' : label[0];
                }
                items.Add(new JObject
                {
                    ["label"] = label, ["id"] = item.ItemInstance.id, ["item"] = LocalizorManager.GetLocalization(item.itemName, null),
                    ["key"] = item.itemName, ["anchor"] = PlanPose(plan, item.transform.position), ["squares"] = squares,
                    ["rotation"] = Math.Round(item.transform.eulerAngles.y, 2), ["use"] = UseSquares(plan, item),
                    ["reachable"] = item.GetClosestNavMeshTargetPosition(PlayerHelper.GetPosition()) != Vector3.zero,
                    ["footprint"] = colliders.Any(c => c != null && !(c is BoxCollider)) ? "conservative collider bounds" : "projected collider boxes",
                });
            }
            var player = plan.Square(PlayerHelper.GetPosition());
            if (player != null)
            {
                var p = plan.Resolve(player); rows[Mathf.RoundToInt(p.z / PlanSquareSize) - plan.MinZ][Mathf.RoundToInt(p.x / PlanSquareSize) - plan.MinX] = '@';
            }
            var building = InstanceBehavior<BuildingManager>.Instance.building;
            return new JObject
            {
                ["address"] = InsideAddress(), ["session"] = TestBridge.Session, ["floor"] = plan.Floor + 1,
                ["building_size"] = building.BuildingSize, ["building_version"] = building.BuildingVersion,
                ["square_size"] = PlanSquareSize, ["origin"] = PlanPose(plan, plan.Point(0, 0)),
                ["columns"] = new JArray(Enumerable.Range(0, plan.Columns).Select(ColumnName)), ["rows"] = plan.Rows,
                ["map"] = new JArray(rows.Select(r => new string(r))), ["cells"] = cells, ["items"] = items,
                ["player"] = PlanPose(plan, PlayerHelper.GetPosition()),
                ["geometry"] = "sampled at native 0.5 m world-grid anchors; walls/blocked volumes sampled per square; exit doors only; non-box footprints conservative; use targets read as currently cached",
            };
        }
    }
}
