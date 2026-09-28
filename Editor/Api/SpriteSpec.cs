using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>Thrown when a sprite spec is invalid. The message names the offending JSON path.</summary>
    public sealed class SpriteSpecException : Exception
    {
        public SpriteSpecException(string message) : base(message) { }
    }

    /// <summary>
    /// Converts between the compact JSON sprite spec (see AGENTS.md) and <see cref="UISpriteStyle"/>.
    /// Effects that are not mentioned in the spec are disabled.
    /// </summary>
    public static class SpriteSpec
    {
        const int MaxGradientKeys = 8; // Unity Gradient limit

        internal static readonly string[] RootKeys =
            { "size", "radius", "scale", "nineSlice", "fill", "stroke", "shadows", "glow", "innerShadow", "innerGlow", "output", "comment" };

        // ------------------------------------------------------------------ parse

        public static UISpriteStyle Parse(string json)
        {
            object root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (FormatException e)
            {
                throw new SpriteSpecException(e.Message);
            }
            return FromObject(root, "$");
        }

        internal static UISpriteStyle FromObject(object value, string path)
        {
            var root = AsObject(value, path, RootKeys);
            var style = ScriptableObject.CreateInstance<UISpriteStyle>();
            try
            {
                Apply(root, style, path);
                return style;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(style);
                throw;
            }
        }

        static void Apply(Dictionary<string, object> root, UISpriteStyle style, string path)
        {
            // Start from a blank slate: only what the spec mentions is enabled.
            style.dropShadows.Clear();
            style.outerGlow.enabled = false;
            style.innerShadow.enabled = false;
            style.innerGlow.enabled = false;
            style.stroke.enabled = false;
            style.fill = new FillSettings { mode = FillMode.Solid, color = Color.white };

            if (!root.TryGetValue("size", out var size))
                throw new SpriteSpecException($"{path}.size: required, e.g. \"size\": [240, 80]");
            var sz = AsVector2(size, $"{path}.size");
            style.shape.size = new Vector2Int(
                ToInt(sz.x, 1, SpriteRasterizer.MaxSize, $"{path}.size[0]"),
                ToInt(sz.y, 1, SpriteRasterizer.MaxSize, $"{path}.size[1]"));

            if (root.TryGetValue("radius", out var radius))
            {
                if (radius is List<object> list)
                {
                    if (list.Count != 4)
                        throw new SpriteSpecException($"{path}.radius: expected a number or [topLeft, topRight, bottomRight, bottomLeft]");
                    style.shape.linkCorners = false;
                    style.shape.topLeft = AsFloat(list[0], $"{path}.radius[0]", 0f);
                    style.shape.topRight = AsFloat(list[1], $"{path}.radius[1]", 0f);
                    style.shape.bottomRight = AsFloat(list[2], $"{path}.radius[2]", 0f);
                    style.shape.bottomLeft = AsFloat(list[3], $"{path}.radius[3]", 0f);
                }
                else
                {
                    style.shape.linkCorners = true;
                    style.shape.radius = AsFloat(radius, $"{path}.radius", 0f);
                }
            }
            else
            {
                style.shape.linkCorners = true;
                style.shape.radius = 0f;
            }

            style.shape.scale = root.TryGetValue("scale", out var scale) ? ToInt(AsFloat(scale, $"{path}.scale"), 1, 4, $"{path}.scale") : 1;
            style.nineSlice = !root.TryGetValue("nineSlice", out var nine) || AsBool(nine, $"{path}.nineSlice");

            if (root.TryGetValue("fill", out var fill))
                style.fill = ParseFill(fill, $"{path}.fill");

            if (root.TryGetValue("stroke", out var strokeValue) && strokeValue != null)
            {
                var o = AsObject(strokeValue, $"{path}.stroke", "width", "position", "color", "fill");
                style.stroke.enabled = true;
                style.stroke.width = o.TryGetValue("width", out var w) ? AsFloat(w, $"{path}.stroke.width", 0f) : 2f;
                style.stroke.position = o.TryGetValue("position", out var pos)
                    ? ParseEnum<StrokePosition>(pos, $"{path}.stroke.position")
                    : StrokePosition.Inside;
                if (o.ContainsKey("color") && o.ContainsKey("fill"))
                    throw new SpriteSpecException($"{path}.stroke: use either \"color\" or \"fill\", not both");
                style.stroke.fill = o.TryGetValue("fill", out var sf) ? ParseFill(sf, $"{path}.stroke.fill")
                    : new FillSettings { mode = FillMode.Solid, color = o.TryGetValue("color", out var sc) ? ParseColor(sc, $"{path}.stroke.color") : Color.white };
            }

            if (root.TryGetValue("shadows", out var shadows) && shadows != null)
            {
                var items = shadows as List<object> ?? new List<object> { shadows };
                for (int i = 0; i < items.Count; i++)
                {
                    string p = $"{path}.shadows[{i}]";
                    var o = AsObject(items[i], p, "color", "offset", "blur", "spread");
                    style.dropShadows.Add(new ShadowSettings
                    {
                        enabled = true,
                        color = o.TryGetValue("color", out var c) ? ParseColor(c, $"{p}.color") : new Color(0f, 0f, 0f, 0.35f),
                        offset = o.TryGetValue("offset", out var off) ? AsVector2(off, $"{p}.offset") : new Vector2(0f, 4f),
                        blur = o.TryGetValue("blur", out var b) ? AsFloat(b, $"{p}.blur", 0f) : 12f,
                        spread = o.TryGetValue("spread", out var s) ? AsFloat(s, $"{p}.spread") : 0f,
                    });
                }
            }

            if (root.TryGetValue("glow", out var glowValue) && glowValue != null)
            {
                const string p = "glow";
                var o = AsObject(glowValue, $"{path}.{p}", "color", "size", "spread", "intensity");
                var g = style.outerGlow;
                g.enabled = true;
                g.color = o.TryGetValue("color", out var c) ? ParseColor(c, $"{path}.{p}.color") : new Color(0.35f, 0.75f, 1f, 1f);
                g.size = o.TryGetValue("size", out var sz2) ? AsFloat(sz2, $"{path}.{p}.size", 0f) : 16f;
                g.spread = o.TryGetValue("spread", out var s) ? AsFloat(s, $"{path}.{p}.spread") : 0f;
                g.intensity = o.TryGetValue("intensity", out var it) ? Mathf.Clamp(AsFloat(it, $"{path}.{p}.intensity", 0f), 0f, 4f) : 1f;
            }

            ParseInner(root, "innerShadow", style.innerShadow, new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, 2f), 4f, path);
            ParseInner(root, "innerGlow", style.innerGlow, new Color(1f, 1f, 1f, 0.5f), Vector2.zero, 10f, path);
        }

        static void ParseInner(Dictionary<string, object> root, string key, InnerEffectSettings fx,
            Color defColor, Vector2 defOffset, float defBlur, string path)
        {
            if (!root.TryGetValue(key, out var value) || value == null) return;
            string p = $"{path}.{key}";
            var o = AsObject(value, p, "color", "offset", "blur", "choke");
            fx.enabled = true;
            fx.color = o.TryGetValue("color", out var c) ? ParseColor(c, $"{p}.color") : defColor;
            fx.offset = o.TryGetValue("offset", out var off) ? AsVector2(off, $"{p}.offset") : defOffset;
            fx.blur = o.TryGetValue("blur", out var b) ? AsFloat(b, $"{p}.blur", 0f) : defBlur;
            fx.choke = o.TryGetValue("choke", out var ch) ? AsFloat(ch, $"{p}.choke", 0f) : 0f;
        }

        static FillSettings ParseFill(object value, string path)
        {
            if (value is string)
                return new FillSettings { mode = FillMode.Solid, color = ParseColor(value, path) };

            var o = AsObject(value, path, "type", "color", "colors", "angle", "direction", "center", "radius");
            string type = o.TryGetValue("type", out var t)
                ? AsString(t, $"{path}.type").ToLowerInvariant()
                : o.ContainsKey("colors") ? "linear" : "solid";

            var fill = new FillSettings();
            switch (type)
            {
                case "solid":
                    fill.mode = FillMode.Solid;
                    if (!o.TryGetValue("color", out var c))
                        throw new SpriteSpecException($"{path}.color: required for a solid fill");
                    fill.color = ParseColor(c, $"{path}.color");
                    return fill;
                case "linear":
                    fill.mode = FillMode.LinearGradient;
                    if (o.ContainsKey("angle") && o.ContainsKey("direction"))
                        throw new SpriteSpecException($"{path}: use either \"angle\" or \"direction\", not both");
                    fill.angle = o.TryGetValue("direction", out var dir) ? ParseDirection(dir, $"{path}.direction")
                        : o.TryGetValue("angle", out var a) ? Mathf.Repeat(AsFloat(a, $"{path}.angle"), 360f)
                        : 90f;
                    break;
                case "radial":
                    fill.mode = FillMode.RadialGradient;
                    fill.radialCenter = o.TryGetValue("center", out var ctr) ? AsVector2(ctr, $"{path}.center") : new Vector2(0.5f, 0.5f);
                    fill.radialRadius = o.TryGetValue("radius", out var r) ? Mathf.Max(0.01f, AsFloat(r, $"{path}.radius", 0f)) : 1f;
                    break;
                default:
                    throw new SpriteSpecException($"{path}.type: expected \"solid\", \"linear\" or \"radial\" but got \"{type}\"");
            }

            if (!o.TryGetValue("colors", out var colors))
                throw new SpriteSpecException($"{path}.colors: required for a {type} gradient, e.g. [\"#2F6BFF\", \"#6FA8FF\"]");
            fill.gradient = ParseGradient(colors, $"{path}.colors");
            return fill;
        }

        static Gradient ParseGradient(object value, string path)
        {
            if (value is not List<object> list || list.Count < 2)
                throw new SpriteSpecException($"{path}: expected an array of at least 2 colors");
            if (list.Count > MaxGradientKeys)
                throw new SpriteSpecException($"{path}: at most {MaxGradientKeys} colors are supported");

            var colorKeys = new GradientColorKey[list.Count];
            var alphaKeys = new GradientAlphaKey[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                string p = $"{path}[{i}]";
                Color color;
                float at = i / (list.Count - 1f);
                if (list[i] is string)
                {
                    color = ParseColor(list[i], p);
                }
                else
                {
                    var o = AsObject(list[i], p, "color", "at");
                    if (!o.TryGetValue("color", out var c)) throw new SpriteSpecException($"{p}.color: required");
                    color = ParseColor(c, $"{p}.color");
                    if (o.TryGetValue("at", out var atValue)) at = Mathf.Clamp01(AsFloat(atValue, $"{p}.at"));
                }
                colorKeys[i] = new GradientColorKey(color, at);
                alphaKeys[i] = new GradientAlphaKey(color.a, at);
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        static float ParseDirection(object value, string path)
        {
            string d = AsString(value, path).Trim().ToLowerInvariant();
            return d switch
            {
                "to right" => 0f,
                "to top right" or "to right top" => 45f,
                "to top" => 90f,
                "to top left" or "to left top" => 135f,
                "to left" => 180f,
                "to bottom left" or "to left bottom" => 225f,
                "to bottom" => 270f,
                "to bottom right" or "to right bottom" => 315f,
                _ => throw new SpriteSpecException(
                    $"{path}: expected \"to right\", \"to top\", \"to left\", \"to bottom\" or a diagonal like \"to top right\" but got \"{d}\""),
            };
        }

        /// <summary>Parses "#RGB", "#RRGGBB", "#RRGGBBAA", "rgb(r,g,b)", "rgba(r,g,b,a)" or a color name.</summary>
        public static Color ParseColor(object value, string path)
        {
            string s = AsString(value, path).Trim();
            string lower = s.ToLowerInvariant();
            if (lower.StartsWith("rgb"))
            {
                int open = s.IndexOf('('), close = s.LastIndexOf(')');
                if (open > 0 && close > open)
                {
                    var parts = s.Substring(open + 1, close - open - 1).Split(',');
                    if ((parts.Length == 3 || parts.Length == 4) && parts.All(p => float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                    {
                        float P(int i) => float.Parse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
                        return new Color(P(0) / 255f, P(1) / 255f, P(2) / 255f, parts.Length == 4 ? Mathf.Clamp01(P(3)) : 1f);
                    }
                }
            }
            else if (ColorUtility.TryParseHtmlString(s, out var c))
            {
                return c;
            }
            throw new SpriteSpecException($"{path}: invalid color \"{s}\". Use \"#RRGGBB\", \"#RRGGBBAA\" or \"rgba(r, g, b, a)\" (a = 0..1)");
        }

        // ------------------------------------------------------------------ serialize

        /// <summary>Converts a style to a spec JSON string. Disabled effects are omitted.</summary>
        public static string ToJson(UISpriteStyle style) => MiniJson.Serialize(ToObject(style));

        internal static Dictionary<string, object> ToObject(UISpriteStyle style)
        {
            var shape = style.shape;
            var root = new Dictionary<string, object>
            {
                ["size"] = new List<object> { shape.size.x, shape.size.y },
                ["radius"] = shape.linkCorners
                    ? shape.radius
                    : new List<object> { shape.topLeft, shape.topRight, shape.bottomRight, shape.bottomLeft },
            };
            if (shape.scale != 1) root["scale"] = shape.scale;
            if (!style.nineSlice) root["nineSlice"] = false;
            root["fill"] = FillToObject(style.fill);

            if (style.stroke.enabled)
            {
                var stroke = new Dictionary<string, object>
                {
                    ["width"] = style.stroke.width,
                    ["position"] = style.stroke.position.ToString().ToLowerInvariant(),
                };
                if (style.stroke.fill.mode == FillMode.Solid) stroke["color"] = ColorToString(style.stroke.fill.color);
                else stroke["fill"] = FillToObject(style.stroke.fill);
                root["stroke"] = stroke;
            }

            var shadows = style.dropShadows.Where(s => s != null && s.enabled).Select(s => (object)new Dictionary<string, object>
            {
                ["color"] = ColorToString(s.color),
                ["offset"] = new List<object> { s.offset.x, s.offset.y },
                ["blur"] = s.blur,
                ["spread"] = s.spread,
            }).ToList();
            if (shadows.Count > 0) root["shadows"] = shadows;

            var g = style.outerGlow;
            if (g.enabled)
            {
                root["glow"] = new Dictionary<string, object>
                {
                    ["color"] = ColorToString(g.color), ["size"] = g.size, ["spread"] = g.spread, ["intensity"] = g.intensity,
                };
            }

            if (style.innerShadow.enabled) root["innerShadow"] = InnerToObject(style.innerShadow);
            if (style.innerGlow.enabled) root["innerGlow"] = InnerToObject(style.innerGlow);
            return root;
        }

        static object FillToObject(FillSettings fill)
        {
            switch (fill.mode)
            {
                case FillMode.LinearGradient:
                    return new Dictionary<string, object>
                    {
                        ["type"] = "linear", ["angle"] = fill.angle, ["colors"] = GradientToList(fill.gradient),
                    };
                case FillMode.RadialGradient:
                    return new Dictionary<string, object>
                    {
                        ["type"] = "radial",
                        ["center"] = new List<object> { fill.radialCenter.x, fill.radialCenter.y },
                        ["radius"] = fill.radialRadius,
                        ["colors"] = GradientToList(fill.gradient),
                    };
                default:
                    return ColorToString(fill.color);
            }
        }

        static List<object> GradientToList(Gradient gradient)
        {
            // Sample at every key time so both color and alpha keys are preserved.
            var times = gradient.colorKeys.Select(k => k.time).Concat(gradient.alphaKeys.Select(k => k.time))
                .Select(t => Mathf.Round(t * 10000f) / 10000f).Distinct().OrderBy(t => t).ToList();
            if (times.Count > MaxGradientKeys)
                times = gradient.colorKeys.Select(k => k.time).OrderBy(t => t).ToList();
            if (times.Count == 1) times.Add(times[0] == 0f ? 1f : 0f);
            times.Sort();

            return times.Select(t => (object)new Dictionary<string, object>
            {
                ["color"] = ColorToString(gradient.Evaluate(t)), ["at"] = t,
            }).ToList();
        }

        static Dictionary<string, object> InnerToObject(InnerEffectSettings fx) => new Dictionary<string, object>
        {
            ["color"] = ColorToString(fx.color),
            ["offset"] = new List<object> { fx.offset.x, fx.offset.y },
            ["blur"] = fx.blur,
            ["choke"] = fx.choke,
        };

        static string ColorToString(Color c) =>
            "#" + (c.a >= 0.999f ? ColorUtility.ToHtmlStringRGB(c) : ColorUtility.ToHtmlStringRGBA(c));

        // ------------------------------------------------------------------ helpers

        internal static Dictionary<string, object> AsObject(object value, string path, params string[] allowedKeys)
        {
            if (value is not Dictionary<string, object> obj)
                throw new SpriteSpecException($"{path}: expected an object");
            foreach (var key in obj.Keys)
                if (Array.IndexOf(allowedKeys, key) < 0)
                    throw new SpriteSpecException($"{path}.{key}: unknown property. Allowed: {string.Join(", ", allowedKeys)}");
            return obj;
        }

        static string AsString(object value, string path) =>
            value as string ?? throw new SpriteSpecException($"{path}: expected a string");

        static bool AsBool(object value, string path) =>
            value is bool b ? b : throw new SpriteSpecException($"{path}: expected true or false");

        static float AsFloat(object value, string path, float min = float.NegativeInfinity)
        {
            if (value is not double d)
                throw new SpriteSpecException($"{path}: expected a number");
            if (d < min)
                throw new SpriteSpecException($"{path}: must be >= {min.ToString(CultureInfo.InvariantCulture)}");
            return (float)d;
        }

        static int ToInt(float value, int min, int max, string path)
        {
            if (value < min || value > max || Math.Abs(value - Mathf.Round(value)) > 1e-4f)
                throw new SpriteSpecException($"{path}: expected a whole number between {min} and {max}");
            return Mathf.RoundToInt(value);
        }

        static Vector2 AsVector2(object value, string path)
        {
            if (value is not List<object> list || list.Count != 2)
                throw new SpriteSpecException($"{path}: expected [x, y]");
            return new Vector2(AsFloat(list[0], $"{path}[0]"), AsFloat(list[1], $"{path}[1]"));
        }

        static T ParseEnum<T>(object value, string path) where T : struct, Enum
        {
            string s = AsString(value, path);
            if (Enum.TryParse(s, true, out T result) && Enum.IsDefined(typeof(T), result)) return result;
            throw new SpriteSpecException($"{path}: expected one of {string.Join(", ", Enum.GetNames(typeof(T)).Select(n => $"\"{n.ToLowerInvariant()}\""))}");
        }
    }
}
