using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    // Optional, independently versioned cosmetic data. Keep the original token so a missing icon,
    // malformed design or newer logo format cannot corrupt a save or be silently overwritten.
    public sealed class VehicleLogo
    {
        public const int CurrentVersion = 1;
        public string Name { get; }
        public string Shape { get; }
        public string Font { get; }
        public string Background { get; }
        public string Ink { get; }
        public string Text { get; }

        public VehicleLogo(string name, string shape, string font, string background, string ink, string text)
        {
            Name = name; Shape = shape; Font = font; Background = background; Ink = ink; Text = text;
            if (!ValidName(name) || !SafeIdentifier(shape) || !SafeIdentifier(font) ||
                !Color(background) || !Color(ink) || !Color(text)) throw new ArgumentException("Invalid truck logo.");
        }

        public static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value!.Length <= 80 &&
            !value.Any(char.IsControl) && !value.Contains("<") && !value.Contains(">");
        private static bool SafeIdentifier(string? value) => !string.IsNullOrWhiteSpace(value) && value!.Length <= 128 &&
            value != "." && value != ".." && !value.Any(c => char.IsControl(c) || "/\\:".Contains(c));
        private static bool Color(string? value) => value != null && value.Length == 8 &&
            value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F');

        public JObject Save() => new JObject { ["version"] = CurrentVersion, ["name"] = Name, ["shape"] = Shape,
            ["font"] = Font, ["background"] = Background, ["ink"] = Ink, ["text"] = Text };

        public static VehicleLogo? Read(JToken? token)
        {
            if (!(token is JObject obj) || obj["version"]?.Type != JTokenType.Integer ||
                obj["version"]!.ToString() != CurrentVersion.ToString()) return null;
            string? Field(string key) => obj[key]?.Type == JTokenType.String ? (string?)obj[key] : null;
            try { return new VehicleLogo(Field("name")!, Field("shape")!, Field("font")!,
                Field("background")!, Field("ink")!, Field("text")!); }
            catch (ArgumentException) { return null; }
        }

        public static bool CanEdit(JToken? token) => token == null || token.Type == JTokenType.Null || Read(token) != null;
    }
}
