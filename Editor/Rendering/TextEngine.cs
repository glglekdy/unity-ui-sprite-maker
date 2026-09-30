using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace UISpriteMaker.Editor
{
    /// <summary>One placed glyph, in pixels relative to the text box's top-left corner (y down).</summary>
    internal struct PlacedGlyph
    {
        public uint Index;
        /// <summary>Pen position on the baseline.</summary>
        public float X, Baseline;
    }

    internal sealed class TextLayoutResult
    {
        public object Face;
        public int PointSize;
        public readonly List<PlacedGlyph> Glyphs = new List<PlacedGlyph>();
        /// <summary>Approximate ink bounds relative to the box's top-left corner, y down (pixels).</summary>
        public Rect Ink;
        /// <summary>Size of the laid-out text block (widest line × total line height), in pixels.</summary>
        public Vector2 Size;
    }

    /// <summary>
    /// Lays out and rasterizes text with Unity's FreeType-based FontEngine. Works in batch mode
    /// with -nographics (no TextMeshPro assets or shaders are needed).
    /// </summary>
    internal static class TextEngine
    {
        const GlyphRenderMode RenderMode = GlyphRenderMode.SMOOTH_HINTED;
        const int GlyphPadding = 1;
        const int CacheLimit = 4096;

        sealed class GlyphBitmap
        {
            public int Width, Height;
            public float BearingX, BearingY;
            /// <summary>Coverage 0..1, rows top-down.</summary>
            public float[] Alpha;
        }

        // Faces are a Font asset, or the path of the default font file. Both work as dictionary keys.
        static readonly Dictionary<(object face, int size, uint glyph), GlyphBitmap> Bitmaps = new Dictionary<(object, int, uint), GlyphBitmap>();
        static readonly Dictionary<(object, string), TextLayoutResult> Layouts = new Dictionary<(object, string), TextLayoutResult>();
        static MethodInfo s_TryAddGlyphToTexture;

        /// <summary>
        /// The font used when a text layer has none: Inter, which ships with the Unity editor (Latin, Greek, Cyrillic).
        /// Unity's built-in runtime font has no font data FontEngine can read.
        /// </summary>
        public static string DefaultFontPath
        {
            get
            {
                string dir = Path.Combine(EditorApplication.applicationContentsPath, "Resources", "Fonts");
                string inter = Path.Combine(dir, "Inter-Regular.ttf");
                if (File.Exists(inter)) return inter;
                if (Directory.Exists(dir))
                    foreach (var file in Directory.GetFiles(dir, "*.ttf"))
                        return file;
                return null;
            }
        }

        public const string DefaultFontName = "Inter (Unity editor font)";

        static object Face(Font font) => font != null
            ? font
            : (object)DefaultFontPath ?? throw new InvalidOperationException("no default font found; set \"font\" to a .ttf/.otf file");

        public static string FontName(Font font) => font != null ? font.name : DefaultFontName;

        /// <summary>Pixel size used to rasterize <paramref name="fontSize"/> UI units at <paramref name="scale"/>.</summary>
        public static int PointSize(float fontSize, int scale) => Mathf.Clamp(Mathf.RoundToInt(fontSize * scale), 1, 4096);

        static void LoadFace(object face, int pointSize)
        {
            var error = face is Font font ? FontEngine.LoadFontFace(font, pointSize) : FontEngine.LoadFontFace((string)face, pointSize);
            if (error != FontEngineError.Success)
                throw new InvalidOperationException(face is Font f
                    ? $"can't load font \"{f.name}\" ({error}). Make sure \"Include Font Data\" is enabled in its import settings"
                    : $"can't load the default font \"{face}\" ({error}); set \"font\" to a .ttf/.otf file");
        }

        /// <summary>The font's natural line height in ems.</summary>
        public static float LineHeightEm(Font font)
        {
            try
            {
                LoadFace(Face(font), 100);
                var face = FontEngine.GetFaceInfo();
                return face.pointSize > 0 ? face.lineHeight / face.pointSize : 1.2f;
            }
            catch (InvalidOperationException)
            {
                return 1.2f;
            }
        }

        /// <summary>Characters of <paramref name="text"/> the font has no glyph for (control characters are ignored).</summary>
        public static string MissingCharacters(Font font, string text)
        {
            LoadFace(Face(font), 32);
            var missing = new StringBuilder();
            foreach (uint cp in CodePoints(text))
            {
                if (cp < 0x20) continue;
                if (!FontEngine.TryGetGlyphIndex(cp, out uint index) || index == 0)
                {
                    string s = char.ConvertFromUtf32((int)cp);
                    if (missing.ToString().IndexOf(s, StringComparison.Ordinal) < 0) missing.Append(s);
                }
            }
            return missing.ToString();
        }

        static IEnumerable<uint> CodePoints(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    yield return (uint)char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                else
                {
                    yield return text[i];
                }
            }
        }

        /// <summary>Lays out a text layer's text inside its box at the given output scale.</summary>
        public static TextLayoutResult Layout(TextLayer t, int scale)
        {
            var font = Face(t.font);
            int pt = PointSize(t.fontSize, scale);
            float boxW = Mathf.Max(0f, t.size.x) * scale, boxH = Mathf.Max(0f, t.size.y) * scale;
            var key = (font, $"{pt}|{boxW}|{boxH}|{t.wrap}|{t.align}|{t.verticalAlign}|{t.lineHeight}|{t.letterSpacing * scale}|{t.text}");
            if (Layouts.TryGetValue(key, out var cached)) return cached;

            var result = DoLayout(t.text ?? "", font, pt, t.wrap ? boxW : float.PositiveInfinity, boxW, boxH,
                t.align, t.verticalAlign, t.lineHeight, t.letterSpacing * scale);
            if (Layouts.Count >= 256) Layouts.Clear();
            Layouts[key] = result;
            return result;
        }

        /// <summary>Natural size of a text in UI units (no wrapping), e.g. to size a text layer to fit.</summary>
        public static Vector2 Measure(TextLayer t)
        {
            var font = Face(t.font);
            int pt = PointSize(t.fontSize, 1);
            var r = DoLayout(t.text ?? "", font, pt, float.PositiveInfinity, 0f, 0f, TextAlign.Left, TextVerticalAlign.Top, t.lineHeight, t.letterSpacing);
            return new Vector2(Mathf.Ceil(r.Size.x), Mathf.Ceil(r.Size.y));
        }

        struct Item
        {
            public uint Index;
            public float Advance;
            public bool Space;
            public bool BreakBefore; // a line may start with this character
        }

        static TextLayoutResult DoLayout(string text, object font, int pt, float maxWidth, float boxW, float boxH,
            TextAlign align, TextVerticalAlign valign, float lineHeightMul, float spacing)
        {
            LoadFace(font, pt);
            var face = FontEngine.GetFaceInfo();
            float ascent = face.ascentLine, descent = face.descentLine; // descent is negative
            float lineStep = face.lineHeight * Mathf.Max(0.1f, lineHeightMul);

            // --- Break into lines ---
            var lines = new List<List<Item>>();
            foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = new List<Item>();
                float width = 0f;
                uint prev = 0;
                foreach (uint cp in CodePoints(paragraph))
                {
                    uint cpv = cp == '\t' ? ' ' : cp;
                    if (cpv < 0x20) continue;
                    if (!FontEngine.TryGetGlyphIndex(cpv, out uint gi)) gi = 0;
                    float adv = 0f;
                    if (FontEngine.TryGetGlyphWithIndexValue(gi, GlyphLoadFlags.LOAD_DEFAULT, out Glyph g))
                        adv = g.metrics.horizontalAdvance;
                    var item = new Item
                    {
                        Index = gi,
                        Advance = adv + spacing,
                        Space = cpv == ' ' || cpv == 0x3000,
                        BreakBefore = prev == ' ' || prev == 0x3000 || IsCjk(cpv) || IsCjk(prev),
                    };
                    prev = cpv;

                    if (line.Count > 0 && !item.Space && width + item.Advance - spacing > maxWidth)
                    {
                        int breakAt = line.FindLastIndex(i => i.BreakBefore);
                        if (item.BreakBefore) breakAt = line.Count;
                        if (breakAt > 0)
                        {
                            var rest = line.GetRange(breakAt, line.Count - breakAt);
                            line.RemoveRange(breakAt, line.Count - breakAt);
                            lines.Add(line);
                            while (rest.Count > 0 && rest[0].Space) rest.RemoveAt(0);
                            line = rest;
                            width = 0f;
                            foreach (var i in line) width += i.Advance;
                        }
                    }
                    line.Add(item);
                    width += item.Advance;
                }
                lines.Add(line);
            }

            // --- Place ---
            var result = new TextLayoutResult { Face = font, PointSize = pt };
            float blockH = ascent - descent + (lines.Count - 1) * lineStep;
            float top = valign switch
            {
                TextVerticalAlign.Middle => (boxH - blockH) * 0.5f,
                TextVerticalAlign.Bottom => boxH - blockH,
                _ => 0f,
            };

            float maxLine = 0f;
            float inkL = float.MaxValue, inkT = float.MaxValue, inkR = float.MinValue, inkB = float.MinValue;
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                int end = line.Count;
                while (end > 0 && line[end - 1].Space) end--; // trailing spaces don't count for alignment
                float lineW = 0f;
                for (int i = 0; i < end; i++) lineW += line[i].Advance;
                if (end > 0) lineW -= spacing;
                maxLine = Mathf.Max(maxLine, lineW);

                float pen = align switch
                {
                    TextAlign.Center => (boxW - lineW) * 0.5f,
                    TextAlign.Right => boxW - lineW,
                    _ => 0f,
                };
                float baseline = top + ascent + li * lineStep;
                for (int i = 0; i < end; i++)
                {
                    var it = line[i];
                    if (!it.Space && FontEngine.TryGetGlyphWithIndexValue(it.Index, GlyphLoadFlags.LOAD_DEFAULT, out Glyph g))
                    {
                        var m = g.metrics;
                        result.Glyphs.Add(new PlacedGlyph { Index = it.Index, X = pen, Baseline = baseline });
                        if (m.width > 0 && m.height > 0)
                        {
                            inkL = Mathf.Min(inkL, pen + m.horizontalBearingX);
                            inkR = Mathf.Max(inkR, pen + m.horizontalBearingX + m.width);
                            inkT = Mathf.Min(inkT, baseline - m.horizontalBearingY);
                            inkB = Mathf.Max(inkB, baseline - m.horizontalBearingY + m.height);
                        }
                    }
                    pen += it.Advance;
                }
            }

            result.Size = new Vector2(maxLine, blockH);
            result.Ink = inkL <= inkR
                ? Rect.MinMaxRect(Mathf.Floor(inkL) - 2f, Mathf.Floor(inkT) - 2f, Mathf.Ceil(inkR) + 2f, Mathf.Ceil(inkB) + 2f)
                : Rect.zero;
            return result;
        }

        static bool IsCjk(uint cp) =>
            (cp >= 0x1100 && cp <= 0x11FF) || (cp >= 0x2E80 && cp <= 0x9FFF) || (cp >= 0xA960 && cp <= 0xA97F) ||
            (cp >= 0xAC00 && cp <= 0xD7FF) || (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0xFF00 && cp <= 0xFFEF) ||
            (cp >= 0x20000 && cp <= 0x2FA1F);

        /// <summary>
        /// Rasterizes a layout into a coverage bitmap covering <see cref="TextLayoutResult.Ink"/>
        /// (rows top-down, origin at the ink rect's top-left).
        /// </summary>
        public static float[] Rasterize(TextLayoutResult layout, out RectInt area)
        {
            var ink = layout.Ink;
            area = new RectInt(Mathf.FloorToInt(ink.x), Mathf.FloorToInt(ink.y), Mathf.CeilToInt(ink.width), Mathf.CeilToInt(ink.height));
            var coverage = new float[Math.Max(0, area.width * area.height)];
            if (coverage.Length == 0) return coverage;

            EnsureGlyphs(layout);
            var fid = layout.Face;
            foreach (var pg in layout.Glyphs)
            {
                if (!Bitmaps.TryGetValue((fid, layout.PointSize, pg.Index), out var bmp) || bmp.Alpha == null) continue;
                // Whole-pixel placement keeps hinted glyphs crisp.
                int gx = Mathf.RoundToInt(pg.X + bmp.BearingX) - area.x;
                int gy = Mathf.RoundToInt(pg.Baseline - bmp.BearingY) - area.y;
                for (int y = 0; y < bmp.Height; y++)
                {
                    int ty = gy + y;
                    if (ty < 0 || ty >= area.height) continue;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int tx = gx + x;
                        if (tx < 0 || tx >= area.width) continue;
                        float a = bmp.Alpha[y * bmp.Width + x];
                        if (a <= 0f) continue;
                        int i = ty * area.width + tx;
                        coverage[i] = coverage[i] + a - coverage[i] * a; // overlapping glyphs: union
                    }
                }
            }
            return coverage;
        }

        static void EnsureGlyphs(TextLayoutResult layout)
        {
            var fid = layout.Face;
            var todo = new List<uint>();
            foreach (var g in layout.Glyphs)
                if (!Bitmaps.ContainsKey((fid, layout.PointSize, g.Index)) && !todo.Contains(g.Index))
                    todo.Add(g.Index);
            if (todo.Count == 0) return;

            if (Bitmaps.Count + todo.Count > CacheLimit) Bitmaps.Clear();
            LoadFace(layout.Face, layout.PointSize);

            s_TryAddGlyphToTexture ??= typeof(FontEngine).GetMethod("TryAddGlyphToTexture",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null,
                new[]
                {
                    typeof(uint), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>), typeof(List<GlyphRect>),
                    typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph).MakeByRefType(),
                }, null);
            if (s_TryAddGlyphToTexture == null)
                throw new InvalidOperationException("This Unity version doesn't expose FontEngine.TryAddGlyphToTexture; text layers can't be rendered");

            int size = Mathf.NextPowerOfTwo(Mathf.Clamp(layout.PointSize * 3, 256, 4096));
            int next = 0;
            while (next < todo.Count)
            {
                var tex = new Texture2D(size, size, TextureFormat.Alpha8, false);
                try
                {
                    tex.SetPixelData(new byte[size * size], 0);
                    tex.Apply(false, false);
                    var free = new List<GlyphRect> { new GlyphRect(0, 0, size, size) };
                    var used = new List<GlyphRect>();
                    var added = new List<(uint index, Glyph glyph)>();
                    while (next < todo.Count)
                    {
                        var args = new object[] { todo[next], GlyphPadding, GlyphPackingMode.BestShortSideFit, free, used, RenderMode, tex, null };
                        if (!(bool)s_TryAddGlyphToTexture.Invoke(null, args))
                        {
                            if (added.Count == 0)
                            {
                                // Doesn't fit even in an empty atlas, or has no outline (e.g. a space): no bitmap.
                                Bitmaps[(fid, layout.PointSize, todo[next])] = new GlyphBitmap();
                                next++;
                                continue;
                            }
                            break; // atlas full: read it back and start a new one
                        }
                        added.Add((todo[next], (Glyph)args[7]));
                        next++;
                    }

                    var data = tex.GetPixelData<byte>(0);
                    foreach (var (index, glyph) in added)
                    {
                        var r = glyph.glyphRect;
                        var m = glyph.metrics;
                        var bmp = new GlyphBitmap
                        {
                            Width = r.width,
                            Height = r.height,
                            BearingX = m.horizontalBearingX,
                            BearingY = m.horizontalBearingY,
                            Alpha = new float[r.width * r.height],
                        };
                        for (int y = 0; y < r.height; y++)
                        {
                            int srcRow = (r.y + r.height - 1 - y) * size; // atlas rows are bottom-up
                            for (int x = 0; x < r.width; x++)
                                bmp.Alpha[y * r.width + x] = data[srcRow + r.x + x] / 255f;
                        }
                        Bitmaps[(fid, layout.PointSize, index)] = bmp;
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }
    }
}
