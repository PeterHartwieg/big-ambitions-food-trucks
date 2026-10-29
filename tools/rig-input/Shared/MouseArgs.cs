using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AgentRigInput
{
    // A refusal with a stable code prefix ("mouse_busy: ..."), turned into the bridge's ok:false reply.
    public sealed class MouseError : Exception
    {
        public string Code { get; }
        public MouseError(string code, string message) : base(code + ": " + message) { Code = code; }
    }

    // A point argument: raw Unity screen pixels, or a geometry target and a local UV point (default its centre).
    public sealed class PointArg
    {
        public ScreenPoint? Screen;
        public string Target;
        public double U = 0.5, V = 0.5;
        public override string ToString() => Target != null ? $"target {Target} uv {U},{V}" : $"{Screen.Value.X},{Screen.Value.Y}";
    }

    public sealed class AfterRelease
    {
        public const string Escape = "ui_escape", Phase = "after_native_release";
    }

    // Argument rules shared by every mouse operation. They match tools/ftlib/mouse.py and the mock bridge.
    public static class MouseArgs
    {
        public const int Protocol = 1;
        public const int MaxSteps = 240, MaxFrames = 240, MaxSettle = 120;
        public const double MaxDuration = 10, MaxScroll = 100;
        public const double HoldTimeoutSeconds = 30;
        public static readonly string[] Ops = { "status", "move", "down", "up", "scroll", "drag", "release" };
        // Added by the bridge client and the lock layer, not by the caller.
        private static readonly string[] Transport = { "op", "timeout_s", "__bridge_lock_token", "__bridge_lock_permit" };

        public static readonly Dictionary<string, string[]> Fields = new Dictionary<string, string[]>
        {
            ["status"] = new[] { "trace_id" },
            ["move"] = new[] { "to", "frames" },
            ["down"] = new[] { "button", "at" },
            ["up"] = new[] { "button", "settle_frames" },
            ["scroll"] = new[] { "x", "y", "at" },
            ["drag"] = new[] { "from", "to", "button", "steps", "duration_s", "settle_frames", "after_release" },
            ["release"] = new string[0],
        };

        public static readonly string[] GeometryFields = { "component", "path_suffix", "max", "target" };

        public static string Op(JObject args)
        {
            var token = args["op"];
            if (token == null || token.Type != JTokenType.String)
                throw new MouseError("mouse_invalid", "mouse needs \"op\": " + string.Join(", ", Ops));
            var op = (string)token;
            if (!Ops.Contains(op)) throw new MouseError("mouse_invalid", $"unknown mouse op '{op}': {string.Join(", ", Ops)}");
            Allow(args, Fields[op]);
            return op;
        }

        public static void Allow(JObject args, IEnumerable<string> fields)
        {
            var allowed = new HashSet<string>(fields.Concat(Transport));
            foreach (var p in args.Properties())
                if (!allowed.Contains(p.Name)) throw new MouseError("mouse_invalid", $"unsupported field '{p.Name}'");
        }

        public static int Int(JObject args, string key, int fallback, int min, int max)
        {
            var t = args[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Float)
            {
                var d = t.Value<double>();
                if (!double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) && d >= min && d <= max) return (int)d;
            }
            if (t.Type == JTokenType.Integer)
            {
                var v = t.Value<long>();
                if (v >= min && v <= max) return (int)v;
            }
            throw new MouseError("mouse_invalid", $"{key} must be a whole number from {min} to {max}");
        }

        public static double Number(JObject args, string key, double fallback, double min, double max)
        {
            var t = args[key];
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
            {
                var d = t.Value<double>();
                if (!double.IsNaN(d) && !double.IsInfinity(d) && d >= min && d <= max) return d;
            }
            throw new MouseError("mouse_invalid", $"{key} must be a finite number from {min} to {max}");
        }

        public static int Button(JObject args) => Int(args, "button", 0, 0, 2);

        public static PointArg Point(JObject args, string key, bool required, int width, int height)
        {
            var t = args[key];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) throw new MouseError("mouse_invalid", $"{key} is required: {{\"x\":…,\"y\":…}} or {{\"target\":…}}");
                return null;
            }
            if (!(t is JObject o)) throw new MouseError("mouse_invalid", $"{key} must be an object");
            if (o["target"] != null)
            {
                Allow(o, new[] { "target", "uv" });
                if (o["target"].Type != JTokenType.String || ((string)o["target"]).Length == 0)
                    throw new MouseError("mouse_invalid", $"{key}.target must be a target string from ui geometry");
                var point = new PointArg { Target = (string)o["target"] };
                if (o["uv"] != null)
                {
                    if (!(o["uv"] is JObject uv)) throw new MouseError("mouse_invalid", $"{key}.uv must be {{\"x\":0..1,\"y\":0..1}}");
                    Allow(uv, new[] { "x", "y" });
                    point.U = Number(Required(uv, "x", key + ".uv"), "x", 0.5, 0, 1);
                    point.V = Number(Required(uv, "y", key + ".uv"), "y", 0.5, 0, 1);
                }
                return point;
            }
            Allow(o, new[] { "x", "y" });
            var x = Number(Required(o, "x", key), "x", 0, double.MinValue, double.MaxValue);
            var y = Number(Required(o, "y", key), "y", 0, double.MinValue, double.MaxValue);
            if (x < 0 || y < 0 || x >= width || y >= height)
                throw new MouseError("mouse_invalid", $"{key} ({x},{y}) is outside the screen {width}x{height} (bottom-left origin)");
            return new PointArg { Screen = new ScreenPoint(x, y) };
        }

        private static JObject Required(JObject o, string key, string where)
        {
            if (o[key] == null) throw new MouseError("mouse_invalid", $"{where} needs \"{key}\"");
            return o;
        }

        public static bool AfterReleaseEscape(JObject args)
        {
            var t = args["after_release"];
            if (t == null || t.Type == JTokenType.Null) return false;
            if (t is JObject o && o.Count == 2 && o["op"]?.Type == JTokenType.String && (string)o["op"] == AfterRelease.Escape &&
                o["phase"]?.Type == JTokenType.String && (string)o["phase"] == AfterRelease.Phase) return true;
            throw new MouseError("mouse_invalid", "after_release supports only {\"op\":\"ui_escape\",\"phase\":\"after_native_release\"}");
        }
    }
}
