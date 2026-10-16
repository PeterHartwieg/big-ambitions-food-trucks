using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    // One place on a vehicle's drawing where a fitted piece's icon goes: its centre as fractions of the image (x from the
    // left, y from the top) and the icon's size as a fraction of the image's height.
    public sealed class DiagramPlace
    {
        public DiagramPlace(double x, double y, double size)
        {
            X = x;
            Y = y;
            Size = size;
        }

        public double X { get; }
        public double Y { get; }
        public double Size { get; }
    }

    public sealed class VehicleDiagram
    {
        public VehicleDiagram(string image, IReadOnlyList<DiagramPlace> places)
        {
            Image = image;
            Places = places;
        }

        // The PNG's file name, under the mod's UI folder.
        public string Image { get; }
        // In fill order: the first fitted piece takes the first place.
        public IReadOnlyList<DiagramPlace> Places { get; }
    }

    // data/vehicle-diagrams.json: per vehicle kind, the drawing on the vehicle page's Menu box and its equipment places,
    // in the order VehicleDisplays fills them. Other keys (width, height, a place's name and baseX/baseY) are read past.
    //   { "truck": { "image": "VehicleDiagram_Truck.png", "iconSize": 0.18,
    //                "places": [ { "x": 0.21, "y": 0.55 }, { "x": 0.40, "y": 0.55, "size": 0.2 } ] }, "cart": { … } }
    // A place's own "size" (or "iconSize") wins over the kind's "iconSize".
    public sealed class VehicleDiagrams
    {
        private readonly Dictionary<string, VehicleDiagram> kinds;

        private VehicleDiagrams(Dictionary<string, VehicleDiagram> kinds) => this.kinds = kinds;

        public IReadOnlyCollection<string> Kinds => kinds.Keys;

        public VehicleDiagram? For(string? kind) => kind != null && kinds.TryGetValue(kind, out var diagram) ? diagram : null;

        public static VehicleDiagrams Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("vehicle-diagrams.json is empty.");
            var root = JObject.Parse(json);
            var kinds = new Dictionary<string, VehicleDiagram>(StringComparer.Ordinal);
            var version = root["schemaVersion"];
            if (version != null && (version.Type != JTokenType.Integer || (int)version != 1))
                throw new FormatException("vehicle-diagrams.json: schemaVersion must be 1.");
            foreach (var property in root.Properties())
            {
                // Notes (keys starting with "_") and the schema version aren't vehicle kinds.
                if (property.Name.StartsWith("_", StringComparison.Ordinal) || property.Name == "schemaVersion") continue;
                if (!(property.Value is JObject kind)) throw new FormatException($"vehicle-diagrams.json: {property.Name} must be an object.");
                var image = (string?)kind["image"];
                if (string.IsNullOrWhiteSpace(image)) throw new FormatException($"vehicle-diagrams.json: {property.Name} has no image.");
                var size = Fraction(kind["iconSize"], property.Name + ".iconSize", 0.2);
                if (!(kind["places"] is JArray list)) throw new FormatException($"vehicle-diagrams.json: {property.Name} has no places.");
                var places = new List<DiagramPlace>();
                for (var i = 0; i < list.Count; i++)
                {
                    if (!(list[i] is JObject place)) throw new FormatException($"vehicle-diagrams.json: {property.Name}.places[{i}] must be an object.");
                    var where = $"{property.Name}.places[{i}]";
                    places.Add(new DiagramPlace(Fraction(place["x"], where + ".x", null), Fraction(place["y"], where + ".y", null),
                        Fraction(place["size"] ?? place["iconSize"], where + ".size", size)));
                }
                kinds[property.Name] = new VehicleDiagram(image!, places);
            }
            return new VehicleDiagrams(kinds);
        }

        private static double Fraction(JToken? token, string where, double? fallback)
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback ?? throw new FormatException($"vehicle-diagrams.json: {where} is missing.");
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
                throw new FormatException($"vehicle-diagrams.json: {where} must be a number.");
            var value = (double)token;
            if (double.IsNaN(value) || value < 0 || value > 1) throw new FormatException($"vehicle-diagrams.json: {where} must be between 0 and 1.");
            return value;
        }

        // The pieces in fitting order on the places in fill order; pieces past the last place aren't drawn.
        public static IReadOnlyList<(DiagramPlace Place, T Piece)> Fill<T>(VehicleDiagram diagram, IEnumerable<T> pieces) =>
            diagram.Places.Zip(pieces, (place, piece) => (place, piece)).ToList();
    }
}
