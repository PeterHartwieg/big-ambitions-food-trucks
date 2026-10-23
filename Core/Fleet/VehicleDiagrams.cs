using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using FoodTrucks.Core.Selling;

namespace FoodTrucks.Core.Fleet
{
    // A piece's own picture on the drawing: a file cropped to the piece, and where its top-left corner and size fall on
    // the full image, in the full image's pixels (y down).
    public sealed class DiagramLayer
    {
        public DiagramLayer(string file, int x, int y, int width, int height)
        {
            File = file;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public string File { get; }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
    }

    // One place on a vehicle's drawing: its hotspot as fractions of the image (x from the left, y from the top), the
    // icon size for a piece with no layer (a fraction of the image's height), and the layers of the pieces that can
    // stand there, by game item.
    public sealed class DiagramPlace
    {
        public DiagramPlace(string name, double x, double y, double size, IReadOnlyDictionary<string, DiagramLayer> layers)
        {
            Name = name;
            X = x;
            Y = y;
            Size = size;
            Layers = layers;
        }

        public string Name { get; }
        public double X { get; }
        public double Y { get; }
        public double Size { get; }
        public IReadOnlyDictionary<string, DiagramLayer> Layers { get; }

        public DiagramLayer? LayerFor(string? item) => item != null && Layers.TryGetValue(item, out var layer) ? layer : null;
    }

    public sealed class VehicleDiagram
    {
        public VehicleDiagram(string image, int width, int height, IReadOnlyList<DiagramPlace> places, IReadOnlyList<string> drawOrder)
        {
            Image = image;
            Width = width;
            Height = height;
            Places = places;
            DrawOrder = drawOrder;
        }

        // The background PNG's file name (the vehicle with empty places), under the mod's UI folder, and its size.
        public string Image { get; }
        public int Width { get; }
        public int Height { get; }
        // In fill order: the first fitted piece takes the first place.
        public IReadOnlyList<DiagramPlace> Places { get; }
        // The places' names from back to front: the order their layers are drawn in.
        public IReadOnlyList<string> DrawOrder { get; }
    }

    // data/vehicle-diagrams.json, schema 2 (layered drawings, 16 October 2026): per vehicle kind the background image and
    // its size, a fallback icon size, the draw order and the places in the order VehicleDisplays fills them, each with
    // its hotspot, size and layers by game item:
    //   { "schemaVersion": 2, "truck": { "image": "VehicleDiagram_Truck.png", "width": 1200, "height": 500, "iconSize": 0.1,
    //     "drawOrder": ["EquipmentLine4", …], "places": [ { "place": "EquipmentLine2", "x": 0.51, "y": 0.31, "size": 0.08,
    //     "layers": { "ba:itemname_hotdoggrill": { "file": "…png", "x": 584, "y": 214, "width": 68, "height": 45 } } } ] } }
    // Keys starting with "_" are notes.
    public sealed class VehicleDiagrams
    {
        public const int SchemaVersion = 2;
        private readonly Dictionary<string, VehicleDiagram> kinds;

        private VehicleDiagrams(Dictionary<string, VehicleDiagram> kinds) => this.kinds = kinds;

        public IReadOnlyCollection<string> Kinds => kinds.Keys;

        public VehicleDiagram? For(string? kind) => kind != null && kinds.TryGetValue(kind, out var diagram) ? diagram : null;

        public static VehicleDiagrams Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("vehicle-diagrams.json is empty.");
            var root = JObject.Parse(json);
            var version = root["schemaVersion"];
            if (version == null || version.Type != JTokenType.Integer || (int)version != SchemaVersion)
                throw new FormatException($"vehicle-diagrams.json: schemaVersion must be {SchemaVersion}.");
            var kinds = new Dictionary<string, VehicleDiagram>(StringComparer.Ordinal);
            foreach (var property in root.Properties())
            {
                if (property.Name.StartsWith("_", StringComparison.Ordinal) || property.Name == "schemaVersion") continue;
                var name = property.Name;
                if (!(property.Value is JObject kind)) throw Bad($"{name} must be an object");
                var image = (string?)kind["image"];
                if (string.IsNullOrWhiteSpace(image)) throw Bad($"{name} has no image");
                var width = Whole(kind["width"], name + ".width");
                var height = Whole(kind["height"], name + ".height");
                var fallback = Fraction(kind["iconSize"], name + ".iconSize", 0.1);
                if (!(kind["places"] is JArray list)) throw Bad($"{name} has no places");
                var places = new List<DiagramPlace>();
                for (var i = 0; i < list.Count; i++)
                {
                    var where = $"{name}.places[{i}]";
                    if (!(list[i] is JObject place)) throw Bad(where + " must be an object");
                    var placeName = (string?)place["place"];
                    if (string.IsNullOrWhiteSpace(placeName)) throw Bad(where + " has no place name");
                    var layers = new Dictionary<string, DiagramLayer>(StringComparer.Ordinal);
                    if (place["layers"] is JObject layerList)
                        foreach (var layer in layerList.Properties())
                        {
                            var at = $"{where}.layers.{layer.Name}";
                            if (!(layer.Value is JObject l)) throw Bad(at + " must be an object");
                            var file = (string?)l["file"];
                            if (string.IsNullOrWhiteSpace(file)) throw Bad(at + " has no file");
                            var lx = Whole(l["x"], at + ".x", allowZero: true);
                            var ly = Whole(l["y"], at + ".y", allowZero: true);
                            var lw = Whole(l["width"], at + ".width");
                            var lh = Whole(l["height"], at + ".height");
                            if (lx + lw > width || ly + lh > height) throw Bad(at + " runs past the image");
                            layers[layer.Name] = new DiagramLayer(file!, lx, ly, lw, lh);
                        }
                    else if (place["layers"] != null) throw Bad(where + ".layers must be an object");
                    places.Add(new DiagramPlace(placeName!, Fraction(place["x"], where + ".x", null), Fraction(place["y"], where + ".y", null),
                        Fraction(place["size"], where + ".size", fallback), layers));
                }
                var order = new List<string>();
                if (kind["drawOrder"] is JArray drawOrder)
                    foreach (var entry in drawOrder)
                    {
                        var placeName = (string?)entry;
                        if (placeName == null || places.All(p => p.Name != placeName)) throw Bad($"{name}.drawOrder names an unknown place {placeName}");
                        if (!order.Contains(placeName)) order.Add(placeName);
                    }
                else if (kind["drawOrder"] != null) throw Bad(name + ".drawOrder must be a list");
                // Places the order leaves out are drawn last, in fill order.
                order.AddRange(places.Select(p => p.Name).Where(p => !order.Contains(p)));
                kinds[name] = new VehicleDiagram(image!, width, height, places, order);
            }
            return new VehicleDiagrams(kinds);
        }

        private static FormatException Bad(string what) => new FormatException($"vehicle-diagrams.json: {what}.");

        private static int Whole(JToken? token, string where, bool allowZero = false)
        {
            if (token == null || token.Type != JTokenType.Integer) throw Bad(where + " must be a whole number");
            var value = (int)token;
            if (value < 0 || (!allowZero && value == 0)) throw Bad(where + (allowZero ? " must not be negative" : " must be above 0"));
            return value;
        }

        private static double Fraction(JToken? token, string where, double? fallback)
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback ?? throw Bad(where + " is missing");
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw Bad(where + " must be a number");
            var value = (double)token;
            if (double.IsNaN(value) || value < 0 || value > 1) throw Bad(where + " must be between 0 and 1");
            return value;
        }

        public static IReadOnlyList<(DiagramPlace Place, T Piece)> Fill<T>(VehicleDiagram diagram, IEnumerable<T> pieces,
            Func<T, string?> equipmentId, int coolerSlots = 1)
        {
            var general = diagram.Places.Where(p => p.Name != EquipmentLayout.CoolerPlace).ToList();
            var cooler = diagram.Places.FirstOrDefault(p => p.Name == EquipmentLayout.CoolerPlace);
            var places = general.Concat(cooler == null ? Array.Empty<DiagramPlace>() : new[] { cooler }).ToList();
            return EquipmentLayout.Place(pieces, general.Count, cooler == null ? 0 : Math.Min(1, Math.Max(0, coolerSlots)), equipmentId)
                .Select(p => (places[p.Slot], p.Piece)).ToList();
        }

        // The pieces in fitting order on the places in fill order; pieces past the last place aren't drawn.
        public static IReadOnlyList<(DiagramPlace Place, T Piece)> Fill<T>(VehicleDiagram diagram, IEnumerable<T> pieces) =>
            diagram.Places.Zip(pieces, (place, piece) => (place, piece)).ToList();
    }
}
