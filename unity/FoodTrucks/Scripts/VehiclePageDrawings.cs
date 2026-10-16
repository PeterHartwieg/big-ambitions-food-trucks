#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
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
        private const float DiagramHeight = 250f;
        private const float MapHeight = 240f;
        private static readonly Dictionary<string, Texture2D?> textures = new Dictionary<string, Texture2D?>(StringComparer.Ordinal);

        internal static void Reset() => textures.Clear();

        // ---- The vehicle and its equipment ----

        // The vehicle's drawing (data/vehicle-diagrams.json) with each fitted piece's icon on its place, in fitting order:
        // a fill bar under it (what it holds of what it can, red when low or empty) and its name and load as a tooltip;
        // a place with no piece is a faint outline. False when there is no drawing for this vehicle (the Menu box then
        // lists the equipment as text).
        internal static bool Equipment(BizManParts.Box box, VehicleInstance instance, StorageView view, DemandTables tables)
        {
            var kind = FoodTrucksMod.KindOf(instance.vehicleTypeName);
            var diagram = DemandData.Diagrams?.For(kind);
            var texture = diagram == null ? null : Texture(diagram.Image);
            if (diagram == null || texture == null) return false;

            var root = Rect("FoodTrucks.Diagram", box.Root);
            box.Add(root);
            var size = root.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = DiagramHeight;
            var frame = Rect("Frame", root);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            var aspect = texture.height > 0 ? texture.width / (float)texture.height : 2f;
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
            var filled = VehicleDiagrams.Fill(diagram, pieces);
            for (var i = 0; i < diagram.Places.Count; i++)
            {
                var place = diagram.Places[i];
                var halfWidth = (float)(place.Size / aspect / 2);
                var halfHeight = (float)(place.Size / 2);
                var x = (float)place.X;
                var y = 1f - (float)place.Y;
                var slot = Rect("FoodTrucks.Place", frame);
                slot.anchorMin = new Vector2(x - halfWidth, y - halfHeight);
                slot.anchorMax = new Vector2(x + halfWidth, y + halfHeight);
                slot.offsetMin = slot.offsetMax = Vector2.zero;
                if (i >= filled.Count)
                {
                    // An empty place: a faint outline.
                    var empty = slot.gameObject.AddComponent<Image>();
                    empty.color = new Color(1f, 1f, 1f, 0f);
                    empty.raycastTarget = false;
                    var outline = slot.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.25f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                    outline.useGraphicAlpha = false;
                    continue;
                }
                var (item, name, pool) = filled[i].Piece;
                var icon = slot.gameObject.AddComponent<Image>();
                icon.sprite = CommissaryOrderForm.Icon(item);
                icon.preserveAspect = true;
                var tip = slot.gameObject.AddComponent<BasicTooltip>();
                tip.titleKey = "foodtrucks:bizman_value";
                tip.localizationArguments = new
                {
                    value = pool == null ? name : $"{name}: {pool.LoadUnits} / {pool.Capacity}"
                };
                if (pool == null || pool.Capacity <= 0) continue;
                // The fill bar under the icon.
                var bar = Rect("FoodTrucks.Fill", frame);
                bar.anchorMin = new Vector2(x - halfWidth, y - halfHeight - 0.07f);
                bar.anchorMax = new Vector2(x + halfWidth, y - halfHeight - 0.03f);
                bar.offsetMin = bar.offsetMax = Vector2.zero;
                var back = bar.gameObject.AddComponent<Image>();
                back.color = new Color(0f, 0f, 0f, 0.15f);
                back.raycastTarget = false;
                var share = Mathf.Clamp01((float)(pool.Load / pool.Capacity));
                var level = Rect("Level", bar);
                level.anchorMin = Vector2.zero;
                level.anchorMax = new Vector2(share, 1f);
                level.offsetMin = level.offsetMax = Vector2.zero;
                var fill = level.gameObject.AddComponent<Image>();
                fill.color = StockLevels.Level(pool.LoadUnits, pool.Capacity) == StockLevel.Ok ? (Color)colors.green : (Color)colors.red;
                fill.raycastTarget = false;
            }
            return true;
        }

        private static Texture2D? Texture(string file)
        {
            if (textures.TryGetValue(file, out var cached)) return cached;
            var texture = FoodTrucksMod.Bundle?.LoadAsset<Texture2D>(ImageFolder + file);
            textures[file] = texture;
            Log.Info(texture != null
                ? $"BizMan: vehicle drawing {file} loaded, {texture.width}x{texture.height}."
                : $"BizMan: no vehicle drawing {ImageFolder}{file} in the mod's bundle; the Menu box lists the equipment as text.");
            return texture;
        }

        // ---- The route ----

        // The route on a light panel, north up (the city map's own view has no picture to crop: it is the 3D city seen by
        // a camera, CityMap/CityMapCam). Numbered pins at the stops in order with their hours, straight lines joining them
        // (back to the first when the route runs on consecutive days, as the crew starts each day where it ended), and the
        // vehicle where it stands.
        internal static void Route(BizManParts.Box box, HireSchedule schedule, VehicleInstance instance, VehicleState? vehicle, Color panel)
        {
            if (schedule.Stops.Count == 0) return;
            var colors = InstanceBehavior<GlobalReferences>.Instance.colors;
            var root = Rect("FoodTrucks.RouteMap", box.Root);
            box.Add(root);
            var size = root.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = MapHeight;
            var background = root.gameObject.AddComponent<Image>();
            background.color = panel;
            background.raycastTarget = false;
            var width = box.Root is RectTransform boxRect ? boxRect.rect.width : 0f;
            var aspect = width > 1f ? width / MapHeight : 2f;

            (double, double)? where = null;
            if (vehicle?.Transit == null && SaveGameManager.Current?.VehicleInstances.Any(v => v.id == instance.id) == true)
            {
                var controller = VehicleHelper.GetVehicleController(instance);
                var position = controller != null ? controller.transform.position : (Vector3)instance.position;
                where = (position.x, position.z);
            }
            var map = RouteMap.Layout(schedule.Stops.Select(s => (s.X, s.Z)).ToList(), where, HireSchedules.RunsOnConsecutiveDays(schedule), aspect);

            var lines = Rect("Lines", root);
            lines.anchorMin = Vector2.zero;
            lines.anchorMax = Vector2.one;
            lines.offsetMin = lines.offsetMax = Vector2.zero;
            var drawn = lines.gameObject.AddComponent<RouteLines>();
            Color line = colors.darkBlue;
            line.a = 0.7f;
            drawn.color = line;
            drawn.Set(map.Stops.Select(p => new Vector2((float)p.U, (float)p.V)), map.Loops, 3f);

            if (map.Vehicle is { } at)
            {
                var marker = Pin(root, at, 20f, "FoodTrucks.Vehicle");
                var image = marker.gameObject.AddComponent<Image>();
                image.sprite = InstanceBehavior<GlobalReferences>.Instance.vehiclePOIIcon;
                image.preserveAspect = true;
                image.color = colors.orange;
                image.raycastTarget = false;
            }
            for (var i = 0; i < map.Stops.Count; i++)
            {
                var stop = schedule.Stops[i];
                var pin = Pin(root, map.Stops[i], 22f, "FoodTrucks.Stop");
                var dot = pin.gameObject.AddComponent<Image>();
                dot.color = colors.darkBlue;
                dot.raycastTarget = false;
                var number = BizManParts.SmallLabel(pin, (i + 1).ToString());
                if (number is RectTransform numberRect)
                {
                    Fill(numberRect);
                    BizManParts.Colour(numberRect, Color.white);
                    BizManParts.Centre(numberRect);
                }
                var hours = BizManParts.SmallLabel(pin, $"{StaffDialog.Clock(stop.OpenAt)}–{StaffDialog.Clock(stop.CloseAt)}");
                if (hours is RectTransform hoursRect)
                {
                    hoursRect.anchorMin = hoursRect.anchorMax = new Vector2(1f, 0.5f);
                    hoursRect.pivot = new Vector2(0f, 0.5f);
                    hoursRect.anchoredPosition = new Vector2(4f, 0f);
                    hoursRect.sizeDelta = new Vector2(120f, 22f);
                    BizManParts.Colour(hoursRect, colors.midnight);
                }
            }
        }

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
