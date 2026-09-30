using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
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

        const int MaxLayerDepth = 16;

        internal static readonly string[] RootKeys =
        {
            "size", "radius", "scale", "nineSlice", "fill", "stroke", "shadows", "glow", "innerShadow", "innerGlow",
            "clip", "layers", "output", "prefab", "comment",
        };

        static readonly string[] EffectKeys = { "stroke", "shadows", "glow", "innerShadow", "innerGlow" };

        static readonly string[] LayerCommonKeys =
        {
            "type", "name", "position", "size", "rotation", "opacity", "blendMode", "visible", "locked",
            "constraints", "export", "comment",
        };

        static readonly Dictionary<string, string[]> LayerTypeKeys = new Dictionary<string, string[]>
        {
            ["shape"] = new[] { "radius", "fill", "clip", "layers" },
            ["image"] = new[] { "source", "tint", "fit" },
            ["text"] = new[] { "text", "font", "fontSize", "fill", "color", "align", "verticalAlign", "lineHeight", "letterSpacing", "wrap" },
            ["group"] = new[] { "clip", "layers" },
        };

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

            var corners = new CornerRadius();
            if (root.TryGetValue("radius", out var radius)) ParseRadius(radius, corners, $"{path}.radius");
            style.shape.linkCorners = corners.linkCorners;
            if (corners.linkCorners)
            {
                style.shape.radius = corners.radius;
            }
            else
            {
                style.shape.topLeft = corners.topLeft;
                style.shape.topRight = corners.topRight;
                style.shape.bottomRight = corners.bottomRight;
                style.shape.bottomLeft = corners.bottomLeft;
            }

            style.shape.scale = root.TryGetValue("scale", out var scale) ? ToInt(AsFloat(scale, $"{path}.scale"), 1, 4, $"{path}.scale") : 1;
            style.nineSlice = !root.TryGetValue("nineSlice", out var nine) || AsBool(nine, $"{path}.nineSlice");

            if (root.TryGetValue("fill", out var fill))
                style.fill = ParseFill(fill, $"{path}.fill");

            ParseEffects(root, style.stroke, style.dropShadows, style.outerGlow, style.innerShadow, style.innerGlow, path);

            style.clip = root.TryGetValue("clip", out var clip) && AsBool(clip, $"{path}.clip");
            style.layers.Clear();
            if (root.TryGetValue("layers", out var layers) && layers != null)
                style.layers.AddRange(ParseLayers(layers, $"{path}.layers", 1));
        }

        static void ParseRadius(object value, CornerRadius target, string path)
        {
            if (value is List<object> list)
            {
                if (list.Count != 4)
                    throw new SpriteSpecException($"{path}: expected a number or [topLeft, topRight, bottomRight, bottomLeft]");
                target.linkCorners = false;
                target.topLeft = AsFloat(list[0], $"{path}[0]", 0f);
                target.topRight = AsFloat(list[1], $"{path}[1]", 0f);
                target.bottomRight = AsFloat(list[2], $"{path}[2]", 0f);
                target.bottomLeft = AsFloat(list[3], $"{path}[3]", 0f);
            }
            else
            {
                target.linkCorners = true;
                target.radius = AsFloat(value, path, 0f);
            }
        }

        /// <summary>Reads stroke/shadows/glow/innerShadow/innerGlow. Effects that aren't mentioned are disabled.</summary>
        static void ParseEffects(Dictionary<string, object> root, StrokeSettings stroke, List<ShadowSettings> shadowList,
            GlowSettings glow, InnerEffectSettings innerShadow, InnerEffectSettings innerGlow, string path)
        {
            stroke.enabled = false;
            glow.enabled = false;
            innerShadow.enabled = false;
            innerGlow.enabled = false;
            shadowList.Clear();

            if (root.TryGetValue("stroke", out var strokeValue) && strokeValue != null)
            {
                var o = AsObject(strokeValue, $"{path}.stroke", "width", "position", "color", "fill");
                stroke.enabled = true;
                stroke.width = o.TryGetValue("width", out var w) ? AsFloat(w, $"{path}.stroke.width", 0f) : 2f;
                stroke.position = o.TryGetValue("position", out var pos)
                    ? ParseEnum<StrokePosition>(pos, $"{path}.stroke.position")
                    : StrokePosition.Inside;
                if (o.ContainsKey("color") && o.ContainsKey("fill"))
                    throw new SpriteSpecException($"{path}.stroke: use either \"color\" or \"fill\", not both");
                stroke.fill = o.TryGetValue("fill", out var sf) ? ParseFill(sf, $"{path}.stroke.fill")
                    : new FillSettings { mode = FillMode.Solid, color = o.TryGetValue("color", out var sc) ? ParseColor(sc, $"{path}.stroke.color") : Color.white };
            }

            if (root.TryGetValue("shadows", out var shadows) && shadows != null)
            {
                var items = shadows as List<object> ?? new List<object> { shadows };
                for (int i = 0; i < items.Count; i++)
                {
                    string p = $"{path}.shadows[{i}]";
                    var o = AsObject(items[i], p, "color", "offset", "blur", "spread");
                    shadowList.Add(new ShadowSettings
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
                glow.enabled = true;
                glow.color = o.TryGetValue("color", out var c) ? ParseColor(c, $"{path}.{p}.color") : new Color(0.35f, 0.75f, 1f, 1f);
                glow.size = o.TryGetValue("size", out var sz2) ? AsFloat(sz2, $"{path}.{p}.size", 0f) : 16f;
                glow.spread = o.TryGetValue("spread", out var s) ? AsFloat(s, $"{path}.{p}.spread") : 0f;
                glow.intensity = o.TryGetValue("intensity", out var it) ? Mathf.Clamp(AsFloat(it, $"{path}.{p}.intensity", 0f), 0f, 4f) : 1f;
            }

            ParseInner(root, "innerShadow", innerShadow, new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, 2f), 4f, path);
            ParseInner(root, "innerGlow", innerGlow, new Color(1f, 1f, 1f, 0.5f), Vector2.zero, 10f, path);
        }

        // ------------------------------------------------------------------ layers

        static List<Layer> ParseLayers(object value, string path, int depth)
        {
            if (value is not List<object> list)
                throw new SpriteSpecException($"{path}: expected an array of layers");
            if (depth > MaxLayerDepth)
                throw new SpriteSpecException($"{path}: layers are nested more than {MaxLayerDepth} levels deep");
            var result = new List<Layer>(list.Count);
            for (int i = 0; i < list.Count; i++)
                result.Add(ParseLayer(list[i], $"{path}[{i}]", depth));
            return result;
        }

        static Layer ParseLayer(object value, string path, int depth)
        {
            if (value is not Dictionary<string, object> raw)
                throw new SpriteSpecException($"{path}: expected an object");
            if (!raw.TryGetValue("type", out var typeValue))
                throw new SpriteSpecException($"{path}.type: required, one of \"shape\", \"image\", \"text\", \"group\"");
            string type = AsString(typeValue, $"{path}.type").Trim().ToLowerInvariant();
            if (!LayerTypeKeys.TryGetValue(type, out var typeKeys))
                throw new SpriteSpecException($"{path}.type: expected \"shape\", \"image\", \"text\" or \"group\" but got \"{type}\"");
            var o = AsObject(value, path, LayerCommonKeys.Concat(EffectKeys).Concat(typeKeys).ToArray());

            Layer layer = type switch
            {
                "shape" => new ShapeLayer(),
                "image" => new ImageLayer(),
                "text" => new TextLayer(),
                _ => new GroupLayer(),
            };

            if (o.TryGetValue("name", out var name)) layer.name = AsString(name, $"{path}.name");
            layer.position = o.TryGetValue("position", out var pos) ? AsVector2(pos, $"{path}.position") : Vector2.zero;
            bool hasSize = o.TryGetValue("size", out var size);
            if (hasSize)
            {
                var sz = AsVector2(size, $"{path}.size");
                if (sz.x < 0f || sz.y < 0f) throw new SpriteSpecException($"{path}.size: must be >= 0");
                layer.size = sz;
            }
            if (o.TryGetValue("rotation", out var rot)) layer.rotation = AsFloat(rot, $"{path}.rotation");
            if (o.TryGetValue("opacity", out var op))
            {
                layer.opacity = AsFloat(op, $"{path}.opacity", 0f);
                if (layer.opacity > 1f) throw new SpriteSpecException($"{path}.opacity: must be between 0 and 1");
            }
            if (o.TryGetValue("blendMode", out var blend)) layer.blendMode = ParseEnum<LayerBlendMode>(blend, $"{path}.blendMode");
            if (o.TryGetValue("visible", out var vis)) layer.visible = AsBool(vis, $"{path}.visible");
            if (o.TryGetValue("locked", out var locked)) layer.locked = AsBool(locked, $"{path}.locked");
            if (o.TryGetValue("export", out var export)) layer.export = ParseEnum<LayerExport>(export, $"{path}.export");
            if (o.TryGetValue("constraints", out var cons) && cons != null)
            {
                var c = AsObject(cons, $"{path}.constraints", "horizontal", "vertical");
                if (c.TryGetValue("horizontal", out var hc)) layer.horizontal = ParseEnum<HorizontalConstraint>(hc, $"{path}.constraints.horizontal");
                if (c.TryGetValue("vertical", out var vc)) layer.vertical = ParseEnum<VerticalConstraint>(vc, $"{path}.constraints.vertical");
            }

            var fx = layer.effects;
            ParseEffects(o, fx.stroke, fx.dropShadows, fx.outerGlow, fx.innerShadow, fx.innerGlow, path);

            switch (layer)
            {
                case ShapeLayer shape:
                    if (!hasSize) throw new SpriteSpecException($"{path}.size: required for a shape, e.g. \"size\": [64, 24]");
                    if (o.TryGetValue("radius", out var radius)) ParseRadius(radius, shape.radius, $"{path}.radius");
                    shape.fill = o.TryGetValue("fill", out var fill) ? ParseFill(fill, $"{path}.fill") : new FillSettings { mode = FillMode.Solid, color = Color.white };
                    shape.clip = o.TryGetValue("clip", out var sc) && AsBool(sc, $"{path}.clip");
                    if (o.TryGetValue("layers", out var sl) && sl != null) shape.children.AddRange(ParseLayers(sl, $"{path}.layers", depth + 1));
                    break;

                case ImageLayer image:
                    if (!o.TryGetValue("source", out var source))
                        throw new SpriteSpecException($"{path}.source: required, e.g. \"source\": \"Assets/UI/Icons/Star.png\"");
                    image.source = ResolveImage(source, $"{path}.source");
                    if (o.TryGetValue("tint", out var tint)) image.tint = ParseColor(tint, $"{path}.tint");
                    if (o.TryGetValue("fit", out var fit)) image.fit = ParseEnum<ImageFit>(fit, $"{path}.fit");
                    if (!hasSize) image.size = NaturalSize(image.source);
                    break;

                case TextLayer text:
                    if (!o.TryGetValue("text", out var content))
                        throw new SpriteSpecException($"{path}.text: required");
                    text.text = AsString(content, $"{path}.text");
                    if (o.TryGetValue("font", out var font)) text.font = ResolveFont(font, $"{path}.font");
                    if (o.TryGetValue("fontSize", out var fs))
                    {
                        text.fontSize = AsFloat(fs, $"{path}.fontSize", 1f);
                        if (text.fontSize > 1024f) throw new SpriteSpecException($"{path}.fontSize: must be <= 1024");
                    }
                    if (o.ContainsKey("color") && o.ContainsKey("fill"))
                        throw new SpriteSpecException($"{path}: use either \"color\" or \"fill\", not both");
                    text.fill = o.TryGetValue("fill", out var tf) ? ParseFill(tf, $"{path}.fill")
                        : new FillSettings { mode = FillMode.Solid, color = o.TryGetValue("color", out var tc) ? ParseColor(tc, $"{path}.color") : Color.black };
                    if (o.TryGetValue("align", out var al)) text.align = ParseEnum<TextAlign>(al, $"{path}.align");
                    if (o.TryGetValue("verticalAlign", out var va))
                        text.verticalAlign = va is string vs && vs.Trim().ToLowerInvariant() == "center"
                            ? TextVerticalAlign.Middle
                            : ParseEnum<TextVerticalAlign>(va, $"{path}.verticalAlign");
                    if (o.TryGetValue("lineHeight", out var lh)) text.lineHeight = Mathf.Max(0.1f, AsFloat(lh, $"{path}.lineHeight", 0f));
                    if (o.TryGetValue("letterSpacing", out var ls)) text.letterSpacing = AsFloat(ls, $"{path}.letterSpacing");
                    text.wrap = o.TryGetValue("wrap", out var wrap) ? AsBool(wrap, $"{path}.wrap") : hasSize;

                    string missing;
                    try
                    {
                        missing = TextEngine.MissingCharacters(text.font, text.text);
                    }
                    catch (InvalidOperationException e)
                    {
                        throw new SpriteSpecException($"{path}.font: {e.Message}");
                    }
                    if (missing.Length > 0)
                        throw new SpriteSpecException(
                            $"{path}.text: font \"{TextEngine.FontName(text.font)}\" has no glyphs for \"{missing}\". Set \"font\" to a font that has them (e.g. a Korean font for Hangul)");
                    if (!hasSize) text.size = TextEngine.Measure(text);
                    break;

                case GroupLayer group:
                    if (!hasSize) throw new SpriteSpecException($"{path}.size: required for a group (the box its children are positioned in)");
                    group.clip = o.TryGetValue("clip", out var gc) && AsBool(gc, $"{path}.clip");
                    if (o.TryGetValue("layers", out var gl) && gl != null) group.children.AddRange(ParseLayers(gl, $"{path}.layers", depth + 1));
                    break;
            }
            return layer;
        }

        static UnityEngine.Object ResolveImage(object value, string path)
        {
            string s = AsString(value, path).Trim();
            int hash = s.IndexOf('#');
            string assetPath = hash >= 0 ? s.Substring(0, hash) : s;
            string spriteName = hash >= 0 ? s.Substring(hash + 1) : null;

            if (spriteName != null)
            {
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                    if (obj is Sprite sp && sp.name == spriteName) return sp;
                throw new SpriteSpecException($"{path}: no sprite named \"{spriteName}\" in \"{assetPath}\"");
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null) return sprite;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture != null) return texture;
            throw new SpriteSpecException($"{path}: no Sprite or Texture2D at \"{assetPath}\"");
        }

        static Font ResolveFont(object value, string path)
        {
            string assetPath = AsString(value, path).Trim();
            var font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
            if (font != null) return font;
            var tmp = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(assetPath);
            if (tmp != null && tmp.sourceFontFile != null) return tmp.sourceFontFile;
            throw new SpriteSpecException($"{path}: no font at \"{assetPath}\". Use a .ttf/.otf file (or a TMP font asset that has a source font)");
        }

        /// <summary>Size of an image in UI units as UGUI shows it (pixels / pixelsPerUnit × 100).</summary>
        internal static Vector2 NaturalSize(UnityEngine.Object source) => source switch
        {
            Sprite sp => sp.rect.size / Mathf.Max(0.01f, sp.pixelsPerUnit) * 100f,
            Texture2D t => new Vector2(t.width, t.height),
            _ => new Vector2(100f, 100f),
        };

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

            WriteEffects(root, style.stroke, style.dropShadows, style.outerGlow, style.innerShadow, style.innerGlow);
            if (style.clip) root["clip"] = true;
            if (style.layers.Count > 0) root["layers"] = LayersToList(style.layers);
            return root;
        }

        static void WriteEffects(Dictionary<string, object> root, StrokeSettings stroke, List<ShadowSettings> shadowList,
            GlowSettings g, InnerEffectSettings innerShadow, InnerEffectSettings innerGlow)
        {
            if (stroke.enabled)
            {
                var o = new Dictionary<string, object>
                {
                    ["width"] = stroke.width,
                    ["position"] = stroke.position.ToString().ToLowerInvariant(),
                };
                if (stroke.fill.mode == FillMode.Solid) o["color"] = ColorToString(stroke.fill.color);
                else o["fill"] = FillToObject(stroke.fill);
                root["stroke"] = o;
            }

            var shadows = shadowList.Where(s => s != null && s.enabled).Select(s => (object)new Dictionary<string, object>
            {
                ["color"] = ColorToString(s.color),
                ["offset"] = new List<object> { s.offset.x, s.offset.y },
                ["blur"] = s.blur,
                ["spread"] = s.spread,
            }).ToList();
            if (shadows.Count > 0) root["shadows"] = shadows;

            if (g.enabled)
            {
                root["glow"] = new Dictionary<string, object>
                {
                    ["color"] = ColorToString(g.color), ["size"] = g.size, ["spread"] = g.spread, ["intensity"] = g.intensity,
                };
            }

            if (innerShadow.enabled) root["innerShadow"] = InnerToObject(innerShadow);
            if (innerGlow.enabled) root["innerGlow"] = InnerToObject(innerGlow);
        }

        static List<object> LayersToList(List<Layer> layers) =>
            layers.Where(l => l != null).Select(l => (object)LayerToObject(l)).ToList();

        internal static Dictionary<string, object> LayerToObject(Layer layer)
        {
            var o = new Dictionary<string, object> { ["type"] = layer.TypeName };
            if (!string.IsNullOrEmpty(layer.name)) o["name"] = layer.name;
            o["position"] = new List<object> { layer.position.x, layer.position.y };
            o["size"] = new List<object> { layer.size.x, layer.size.y };
            if (layer.rotation != 0f) o["rotation"] = layer.rotation;
            if (layer.opacity < 1f) o["opacity"] = layer.opacity;
            if (layer.blendMode != LayerBlendMode.Normal) o["blendMode"] = EnumToString(layer.blendMode);
            if (!layer.visible) o["visible"] = false;
            if (layer.locked) o["locked"] = true;
            if (layer.horizontal != HorizontalConstraint.Left || layer.vertical != VerticalConstraint.Top)
            {
                o["constraints"] = new Dictionary<string, object>
                {
                    ["horizontal"] = EnumToString(layer.horizontal), ["vertical"] = EnumToString(layer.vertical),
                };
            }
            if (layer.export != LayerExport.Auto) o["export"] = EnumToString(layer.export);

            switch (layer)
            {
                case ShapeLayer shape:
                    var r = shape.radius;
                    if (!r.linkCorners) o["radius"] = new List<object> { r.topLeft, r.topRight, r.bottomRight, r.bottomLeft };
                    else if (r.radius != 0f) o["radius"] = r.radius;
                    o["fill"] = FillToObject(shape.fill);
                    if (shape.clip) o["clip"] = true;
                    break;
                case ImageLayer image:
                    if (image.source != null) o["source"] = ImagePath(image.source);
                    if (image.tint != Color.white) o["tint"] = ColorToString(image.tint);
                    if (image.fit != ImageFit.Stretch) o["fit"] = EnumToString(image.fit);
                    break;
                case TextLayer text:
                    o["text"] = text.text ?? "";
                    if (text.font != null) o["font"] = AssetDatabase.GetAssetPath(text.font);
                    o["fontSize"] = text.fontSize;
                    if (text.fill.mode == FillMode.Solid) o["color"] = ColorToString(text.fill.color);
                    else o["fill"] = FillToObject(text.fill);
                    if (text.align != TextAlign.Left) o["align"] = EnumToString(text.align);
                    if (text.verticalAlign != TextVerticalAlign.Top) o["verticalAlign"] = EnumToString(text.verticalAlign);
                    if (text.lineHeight != 1f) o["lineHeight"] = text.lineHeight;
                    if (text.letterSpacing != 0f) o["letterSpacing"] = text.letterSpacing;
                    o["wrap"] = text.wrap;
                    break;
                case GroupLayer group:
                    if (group.clip) o["clip"] = true;
                    break;
            }

            var fx = layer.effects;
            WriteEffects(o, fx.stroke, fx.dropShadows, fx.outerGlow, fx.innerShadow, fx.innerGlow);
            if (layer.Children != null && layer.Children.Count > 0) o["layers"] = LayersToList(layer.Children);
            return o;
        }

        static string ImagePath(UnityEngine.Object source)
        {
            string path = AssetDatabase.GetAssetPath(source);
            if (source is Sprite sprite && AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count() > 1)
                return path + "#" + sprite.name;
            return path;
        }

        static string EnumToString<T>(T value) where T : struct, Enum
        {
            string s = value.ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
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
