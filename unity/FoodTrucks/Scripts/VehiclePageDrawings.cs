#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // The two drawings on a vehicle's BizMan page (Peter, 16 October 2026): the vehicle with its fitted equipment at the
    // top of the Menu box, and the route's simplified map in the Crew and route box.
    internal static class VehiclePageDrawings
    {
        // The drawings' PNGs, in the mod's bundle (every asset under the mod folder is packed by the SDK's builder).
        private const string ImageFolder = "Assets/Mods/FoodTrucks/UI/";
        private const float DiagramMaxHeight = 600f;
        private static readonly Dictionary<string, Texture2D?> textures = new Dictionary<string, Texture2D?>(StringComparer.Ordinal);

        internal static void Reset()
        {
            textures.Clear();
            streets = null;
            logged = false;
        }

        // ---- The vehicle and its equipment ----

        // The vehicle's drawing (data/vehicle-diagrams.json, schema 2): the background (the vehicle with empty places), then
        // in its draw order (back to front) each fitted piece's own layer at its crop rectangle, scaled with the image, so
        // it looks like the vehicle with its equipment. Pieces take the places in fitting order. A piece with no layer (the
        // cotton candy machine, say) shows its icon at the place's hotspot; a place with nothing fitted a faint outline.
        // At each fitted place's hotspot, under the piece, a small fill bar (what it holds of what it can, red when low
        // or empty); the piece's name and load as its tooltip. False when there is no drawing for this vehicle (the Menu
        // box then lists the equipment as text).
        internal static bool Equipment(BizManParts.Box box, VehicleInstance instance, StorageView view, DemandTables tables)
        {
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName);
            var diagram = DemandData.Diagrams?.For(kind);
            var texture = diagram == null ? null : Texture(diagram.Image, required: true);
            if (diagram == null || texture == null) return false;

            var root = Rect("FoodTrucks.Diagram", box.Root);
            box.Add(root);
            // The places and crops are in the json's pixels; the image is drawn at its aspect, whatever its file's size.
            var aspect = diagram.Height > 0 ? diagram.Width / (float)diagram.Height : 2f;
            // The box's full inner width, the height following at the drawing's aspect up to DiagramMaxHeight; capped, the
            // drawing is centred (the frame fits inside at its aspect). Layers, bars and tooltips are anchored to the frame.
            var size = root.gameObject.AddComponent<AspectHeight>();
            size.Aspect = aspect;
            size.MaxHeight = DiagramMaxHeight;
            var frame = Rect("Frame", root);
            Fill(frame);
            var fitter = frame.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = aspect;
            var picture = frame.gameObject.AddComponent<RawImage>();
            picture.texture = texture;
            picture.raycastTarget = false;

            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var pieces = new List<(string Item, string Name, StoragePool? Pool)>();
            foreach (var id in CargoRules.FittedEquipment(tables, CargoLimit.Lines(instance)))
            {
                var piece = tables.Equipment(id);
                if (piece == null) continue;
                var item = instance.cargoInstances?.FirstOrDefault(c => c != null && piece.GameItems.Contains(c.itemName))?.itemName ?? piece.GameItems.FirstOrDefault();
                if (item == null) continue;
                pieces.Add((item, StoragePanel.PieceName(instance, piece), view.Pools.FirstOrDefault(p => p.EquipmentId == id)));
            }
            var filled = VehicleDiagrams.Fill(diagram, pieces).ToDictionary(f => f.Place.Name, f => f.Piece, StringComparer.Ordinal);
            var bars = new List<(DiagramPlace Place, float Below, string Name, StoragePool? Pool)>();
            var missing = new List<string>();
            foreach (var name in diagram.DrawOrder)
            {
                var place = diagram.Places.First(p => p.Name == name);
                var x = (float)place.X;
                var y = 1f - (float)place.Y;
                var halfHeight = (float)(place.Size / 2);
                var halfWidth = halfHeight / aspect;
                if (!filled.TryGetValue(name, out var piece))
                {
                    // Nothing fitted here: a faint outline at the hotspot.
                    var slot = Rect("FoodTrucks.EmptyPlace", frame);
                    Anchor(slot, x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight);
                    var empty = slot.gameObject.AddComponent<Image>();
                    empty.color = new Color(1f, 1f, 1f, 0f);
                    empty.raycastTarget = false;
                    var outline = slot.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.25f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                    outline.useGraphicAlpha = false;
                    continue;
                }
                var layer = place.LayerFor(piece.Item);
                var layerTexture = layer == null ? null : Texture(layer.File, required: false);
                Graphic shown;
                float bottom;
                if (layer != null && layerTexture != null)
                {
                    // The piece's own picture at its crop rectangle (pixels of the full image, y down).
                    var cell = Rect("FoodTrucks.Layer", frame);
                    var left = layer.X / (float)diagram.Width;
                    var right = (layer.X + layer.Width) / (float)diagram.Width;
                    var top = 1f - layer.Y / (float)diagram.Height;
                    bottom = 1f - (layer.Y + layer.Height) / (float)diagram.Height;
                    Anchor(cell, left, bottom, right, top);
                    var raw = cell.gameObject.AddComponent<RawImage>();
                    raw.texture = layerTexture;
                    shown = raw;
                }
                else
                {
                    if (layer != null) missing.Add(layer.File);
                    // No layer for this piece: its icon at the hotspot.
                    var slot = Rect("FoodTrucks.Icon", frame);
                    Anchor(slot, x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight);
                    var icon = slot.gameObject.AddComponent<Image>();
                    icon.sprite = CommissaryOrderForm.Icon(piece.Item);
                    icon.preserveAspect = true;
                    shown = icon;
                    bottom = y - halfHeight;
                }
                var tip = shown.gameObject.AddComponent<BasicTooltip>();
                tip.titleKey = "foodtrucks:bizman_value";
                tip.localizationArguments = new
                {
                    value = piece.Pool == null ? piece.Name : $"{piece.Name}: {piece.Pool.LoadUnits} / {piece.Pool.Capacity}"
                };
                bars.Add((place, bottom, piece.Name, piece.Pool));
            }
            // The fill bars over everything, each just under its piece, as wide as the place's icon.
            foreach (var (place, below, _, pool) in bars)
            {
                if (pool == null || pool.Capacity <= 0) continue;
                var x = (float)place.X;
                var halfWidth = (float)(place.Size / 2) / aspect;
                var bar = Rect("FoodTrucks.Fill", frame);
                Anchor(bar, x - halfWidth, below - 0.05f, x + halfWidth, below - 0.02f);
                var back = bar.gameObject.AddComponent<Image>();
                back.color = new Color(0f, 0f, 0f, 0.15f);
                back.raycastTarget = false;
                var share = Mathf.Clamp01((float)(pool.Load / pool.Capacity));
                var level = Rect("Level", bar);
                Anchor(level, 0f, 0f, share, 1f);
                var fill = level.gameObject.AddComponent<Image>();
                fill.color = StockLevels.Level(pool.LoadUnits, pool.Capacity) == StockLevel.Ok ? (Color)colors.green : (Color)colors.red;
                fill.raycastTarget = false;
            }
            if (missing.Count > 0 && missingLogged.Add(string.Join(",", missing)))
                Log.Info($"BizMan: vehicle drawing layers not in the mod's bundle ({string.Join(", ", missing)}); those pieces show their icons.");
            return true;
        }

        private static readonly HashSet<string> missingLogged = new HashSet<string>(StringComparer.Ordinal);

        private static void Anchor(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // A drawing's PNG from the mod's bundle, loaded the first time it is drawn and kept per file for the scene.
        private static Texture2D? Texture(string file, bool required)
        {
            if (textures.TryGetValue(file, out var cached)) return cached;
            var texture = FoodTrucksMod.Bundle?.LoadAsset<Texture2D>(ImageFolder + file);
            textures[file] = texture;
            if (required)
                Log.Info(texture != null
                    ? $"BizMan: vehicle drawing {file} loaded, {texture.width}x{texture.height}."
                    : $"BizMan: no vehicle drawing {ImageFolder}{file} in the mod's bundle; the Menu box lists the equipment as text.");
            return texture;
        }

        // ---- The route ----

        // One weekday's route on a light panel, north up as the game's city map opens (that map is the 3D city under a
        // camera, CityMap/CityMapCam, with no picture to crop). Under it the streets: the traffic system's lanes, light
        // grey. Round numbered pins at the day's stops in order with their hours beside them, the commissary as a square
        // "C" at both ends of the line, lines joining them (without a commissary, back to the first when the next day
        // runs too, as the crew starts each day where it ended), and the vehicle where it stands. Sizes follow the box's
        // own text: the canvas is in game units (a business row is 100 high), so fixed pixel sizes came out as dots
        // without text (16 October 2026).
        internal static void Route(BizManParts.Box box, HireSchedule schedule, DayOfWeek day, VehicleInstance instance, VehicleState? vehicle)
        {
            var indices = WeekPlan.StopsOf(schedule, day);
            if (indices.Count == 0) return;
            var dayStops = indices.Select(i => schedule.Stops[i]).ToList();
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var unit = BizManParts.SmallLabelHeight();
            var mapHeight = unit * 8f;
            var root = Rect("FoodTrucks.RouteMap", box.Root);
            box.Add(root);
            var size = root.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = mapHeight;
            var background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.93f, 0.94f, 0.96f, 1f);
            background.raycastTarget = false;
            root.gameObject.AddComponent<RectMask2D>();
            var width = box.Root is RectTransform boxRect ? boxRect.rect.width : 0f;
            var aspect = width > 1f ? width / mapHeight : 2f;

            (double, double)? where = null;
            if (vehicle?.Transit == null && SaveGameManager.Current?.VehicleInstances.Any(v => v.id == instance.id) == true)
            {
                var controller = VehicleHelper.GetVehicleController(instance);
                var position = controller != null ? controller.transform.position : (Vector3)instance.position;
                where = (position.x, position.z);
            }
            // The bay where it rents its space starts and ends the day: on the map as its own pin, the line from it and back.
            var home = vehicle == null ? null : HireRoutes.HomeOf(vehicle);
            var endsThere = home != null && home.Value.BayStops.Contains(indices[indices.Count - 1]);
            var map = RouteMap.Layout(dayStops.Select(s => (s.X, s.Z)).ToList(), where, home == null && WeekPlan.RunsOn(schedule, WeekPlan.Next(day)), aspect,
                home: HireRoutes.HomePoint(home));

            var streets = Streets();
            if (streets.Count > 0)
            {
                var shown = streets.Where(s => map.Shows(s.Ax, s.Az, s.Bx, s.Bz))
                    .Select(s => (Point(map.Place(s.Ax, s.Az)), Point(map.Place(s.Bx, s.Bz)))).ToList();
                Lines(root, "Streets", new Color(0.78f, 0.80f, 0.84f, 1f)).SetSegments(shown, unit * 0.18f);
            }
            Color line = colors.darkBlue;
            line.a = 0.8f;
            var path = map.Stops.Select(Point).ToList();
            if (map.Home is { } bay)
            {
                path.Insert(0, Point(bay));
                if (!endsThere) path.Add(Point(bay));
            }
            Lines(root, "Route", line).Set(path, map.Loops, unit * 0.12f);
            if (map.Home is { } at0)
            {
                var pin = Pin(root, at0, unit * 1.3f, "FoodTrucks.Commissary");
                // A square, so it reads apart from the round stops.
                var dot = pin.gameObject.AddComponent<Image>();
                dot.color = colors.green;
                dot.raycastTarget = false;
                if (BizManParts.SmallLabel(pin, LocalizorManager.GetLocalization("foodtrucks:bizman_page_map_commissary", null)) is RectTransform letter)
                {
                    Fill(letter);
                    BizManParts.Colour(letter, Color.white);
                    BizManParts.Centre(letter);
                }
            }

            if (map.Vehicle is { } at)
            {
                var marker = Pin(root, at, unit * 1.1f, "FoodTrucks.Vehicle");
                var image = marker.gameObject.AddComponent<Image>();
                image.sprite = InstanceBehavior<GlobalReferences>.Instance.vehiclePOIIcon;
                image.preserveAspect = true;
                image.color = colors.orange;
                image.raycastTarget = false;
            }
            for (var i = 0; i < map.Stops.Count; i++)
            {
                var stop = dayStops[i];
                var pin = Pin(root, map.Stops[i], unit * 1.15f, "FoodTrucks.Stop");
                var dot = pin.gameObject.AddComponent<Image>();
                dot.sprite = Circle();
                dot.color = colors.darkBlue;
                dot.raycastTarget = false;
                if (BizManParts.SmallLabel(pin, (i + 1).ToString()) is RectTransform number)
                {
                    Fill(number);
                    BizManParts.Colour(number, Color.white);
                    BizManParts.Centre(number);
                }
                if (BizManParts.SmallLabel(pin, $"{StaffDialog.Clock(stop.OpenAt)}–{StaffDialog.Clock(stop.CloseAt)}") is RectTransform hours)
                {
                    hours.anchorMin = hours.anchorMax = new Vector2(1f, 0.5f);
                    hours.pivot = new Vector2(0f, 0.5f);
                    hours.anchoredPosition = new Vector2(unit * 0.25f, 0f);
                    hours.sizeDelta = new Vector2(unit * 5f, unit);
                    BizManParts.Colour(hours, colors.midnight);
                }
            }
            if (!logged)
            {
                logged = true;
                Log.Info($"BizMan: route map {map.WidthMetres:F0} x {map.HeightMetres:F0} m, {streets.Count} street segments in the city" +
                         (streets.Count == 0 ? " (traffic lanes not readable: no streets drawn)" : "") + $", text unit {unit:F0}.");
            }
        }

        private static bool logged;
        private static List<(double Ax, double Az, double Bx, double Bz)>? streets;

        // Every lane segment of the traffic system once per city load, both directions as one, at ground level only by
        // x and z. Empty (and tried again next time) while the traffic system isn't running.
        private static List<(double Ax, double Az, double Bx, double Bz)> Streets()
        {
            if (streets != null) return streets;
            var found = new List<(double, double, double, double)>();
            try
            {
                // Instance makes a new manager when there is none, so HasInstance first (as HatchCheck).
                if (!GleyTrafficSystem.TrafficManager.HasInstance || !GleyTrafficSystem.TrafficManager.IsInitialized) return found;
                var manager = GleyTrafficSystem.TrafficManager.Instance;
                var all = new List<GleyTrafficSystem.Waypoint>();
                // The game's search calls the filter for every waypoint within the radius: the whole city.
                manager.GetClosestWaypoint(Vector3.zero, 100000f, w =>
                {
                    if (w != null) all.Add(w);
                    return false;
                });
                var seen = new HashSet<(long, long, long, long)>();
                foreach (var waypoint in all)
                    foreach (var index in waypoint.neighbors ?? new List<int>())
                    {
                        var other = manager.GetWaypoint(index);
                        if (other == null) continue;
                        var a = waypoint.position;
                        var b = other.position;
                        var keyA = (Mathf.RoundToInt(a.x * 2), Mathf.RoundToInt(a.z * 2));
                        var keyB = (Mathf.RoundToInt(b.x * 2), Mathf.RoundToInt(b.z * 2));
                        var key = keyA.CompareTo(keyB) <= 0
                            ? ((long)keyA.Item1, (long)keyA.Item2, (long)keyB.Item1, (long)keyB.Item2)
                            : ((long)keyB.Item1, (long)keyB.Item2, (long)keyA.Item1, (long)keyA.Item2);
                        if (seen.Add(key)) found.Add((a.x, a.z, b.x, b.z));
                    }
                streets = found;
            }
            catch (Exception ex)
            {
                Log.Error("BizMan route map, reading the traffic lanes: " + ex.Message);
            }
            return found;
        }

        private static Sprite? circle;

        // A round marker, drawn once: the game has no plain circle sprite to borrow.
        internal static Sprite Circle()
        {
            if (circle != null) return circle;
            const int side = 64;
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[side * side];
            var centre = (side - 1) / 2f;
            for (var y = 0; y < side; y++)
                for (var x = 0; x < side; x++)
                {
                    var distance = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    var alpha = Mathf.Clamp01(side / 2f - distance);
                    pixels[y * side + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            circle = Sprite.Create(texture, new Rect(0, 0, side, side), new Vector2(0.5f, 0.5f), 100f);
            return circle;
        }

        private static RouteLines Lines(Transform parent, string name, Color colour)
        {
            var rect = Rect(name, parent);
            Fill(rect);
            var lines = rect.gameObject.AddComponent<RouteLines>();
            lines.color = colour;
            return lines;
        }

        private static Vector2 Point((double U, double V) at) => new Vector2((float)at.U, (float)at.V);

        private static RectTransform Pin(Transform parent, (double U, double V) at, float side, string name)
        {
            var pin = Rect(name, parent);
            pin.anchorMin = pin.anchorMax = new Vector2((float)at.U, (float)at.V);
            pin.pivot = new Vector2(0.5f, 0.5f);
            pin.sizeDelta = new Vector2(side, side);
            pin.anchoredPosition = Vector2.zero;
            return pin;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var element = rect.GetComponent<LayoutElement>();
            if (element != null) element.ignoreLayout = true;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
