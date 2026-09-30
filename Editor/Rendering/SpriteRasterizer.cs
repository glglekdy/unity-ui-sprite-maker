using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    public sealed class RasterResult
    {
        public int Width;
        public int Height;
        /// <summary>Straight-alpha pixels, row 0 at the bottom (Texture2D order).</summary>
        public Color32[] Pixels;
        public int Scale;
        /// <summary>Shape bounds inside the canvas, in pixels.</summary>
        public RectInt ShapeRect;
        /// <summary>Space around the shape reserved for effects, in pixels (left, bottom, right, top).</summary>
        public Vector4 Padding;
        /// <summary>9-slice sprite border in pixels (left, bottom, right, top). Zero when slicing is off.</summary>
        public Vector4 Border;
        /// <summary>Layers that couldn't be drawn (missing image, unreadable font, ...).</summary>
        public List<string> Warnings = new List<string>();

        public Texture2D ToTexture()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(Pixels);
            tex.Apply(false);
            return tex;
        }
    }

    public sealed class SpriteLayout
    {
        public int Width;
        public int Height;
        public int Scale;
        /// <summary>Shape bounds inside the canvas, in pixels.</summary>
        public RectInt ShapeRect;
        /// <summary>Space around the shape reserved for effects, in pixels (left, bottom, right, top).</summary>
        public Vector4 Padding;
        /// <summary>9-slice sprite border in pixels (left, bottom, right, top). Zero when slicing is off.</summary>
        public Vector4 Border;
        /// <summary>Clamped corner radii in pixels (topLeft, topRight, bottomRight, bottomLeft).</summary>
        public Vector4 Radii;
        public float StrokeWidth;
        public float StrokeOuter;
        /// <summary>True when layers cover every column (row) of the 9-slice stretch area, so stretching distorts them.</summary>
        public bool SliceBlockedX, SliceBlockedY;
    }

    public static class SpriteRasterizer
    {
        public const int MaxSize = 2048;

        // Blur values are treated as CSS blur radii: sigma = blur / 2, visible extent ~ 3 sigma.
        internal const float BlurToSigma = 0.5f;
        internal const float BlurExtent = 1.5f;

        /// <summary>Computes canvas size, padding and 9-slice border without rendering pixels.</summary>
        public static SpriteLayout ComputeLayout(UISpriteStyle style)
        {
            var shape = style.shape;
            int s = Mathf.Clamp(shape.scale, 1, 4);
            int w = Mathf.Clamp(shape.size.x, 1, MaxSize) * s;
            int h = Mathf.Clamp(shape.size.y, 1, MaxSize) * s;

            float maxRadius = Mathf.Min(w, h) * 0.5f;
            Vector4 radii = shape.GetRadii() * s;
            for (int i = 0; i < 4; i++)
                radii[i] = Mathf.Clamp(radii[i], 0f, maxRadius);

            // --- Padding needed around the shape for outer effects and layers ---
            var fx = Fx.Of(style);
            var ext = fx.Extents(s);
            float pl = ext.x, pb = ext.y, pr = ext.z, pt = ext.w;

            if (!style.clip)
            {
                var root = Placement.Axis(w * 0.5f, h * 0.5f, w * 0.5f, h * 0.5f);
                Rect? bounds = null;
                foreach (var layer in style.layers)
                    if (layer != null) bounds = RectUtil.Union(bounds, LayerGeometry.Full(layer, root.Child(layer, s), s));
                if (bounds is Rect b)
                {
                    pl = Mathf.Max(pl, -b.xMin);
                    pb = Mathf.Max(pb, -b.yMin);
                    pr = Mathf.Max(pr, b.xMax - w);
                    pt = Mathf.Max(pt, b.yMax - h);
                }
            }

            // +1 px so anti-aliased edges never touch the texture border.
            int padL = Mathf.CeilToInt(pl) + 1;
            int padR = Mathf.CeilToInt(pr) + 1;
            int padB = Mathf.CeilToInt(pb) + 1;
            int padT = Mathf.CeilToInt(pt) + 1;

            int W = w + padL + padR;
            int H = h + padB + padT;

            float strokeW = fx.StrokeWidth(s), strokeOuter = fx.StrokeOuter(s);
            var layout = new SpriteLayout
            {
                Width = W,
                Height = H,
                Scale = s,
                ShapeRect = new RectInt(padL, padB, w, h),
                Padding = new Vector4(padL, padB, padR, padT),
                Radii = radii,
                StrokeWidth = strokeW,
                StrokeOuter = strokeOuter,
            };
            if (style.nineSlice)
            {
                layout.Border = ComputeBorder(style, radii, strokeW - strokeOuter, s, W, H, padL, padB, padR, padT);
                if (style.layers.Count > 0) FitBorderAroundLayers(style, layout);
            }
            return layout;
        }

        public static RasterResult Render(UISpriteStyle style)
        {
            var layout = ComputeLayout(style);
            int s = layout.Scale;
            int W = layout.Width, H = layout.Height;
            var root = Placement.Root(layout.ShapeRect);
            var ctx = new Context { Scale = s, Width = W, Height = H };

            var acc = new Surface(new RectInt(0, 0, W, H));
            var dist = new RoundedRectDistance(acc.Rect, root, layout.Radii);
            var shapeMask = dist.Coverage(0f, 0f, 0f);
            DrawStack(ctx, acc, dist, shapeMask, Fx.Of(style), root,
                () => CompositeFill(acc, shapeMask, new FillEvaluator(style.fill, root.Hw, root.Hh), root),
                () =>
                {
                    foreach (var layer in style.layers)
                        if (layer != null) RenderInto(ctx, acc, layer, root.Child(layer, s), style.clip ? shapeMask : null, $"layers[{style.layers.IndexOf(layer)}]");
                });

            // --- Resolve to straight alpha ---
            int n = W * H;
            var pixels = new Color32[n];
            var data = acc.Rgba;
            Parallel.For(0, n, i =>
            {
                int j = i * 4;
                float a = data[j + 3];
                if (a <= 1e-5f) return;
                float inv = 1f / a;
                pixels[i] = new Color32(
                    ToByte(data[j] * inv), ToByte(data[j + 1] * inv), ToByte(data[j + 2] * inv), ToByte(a));
            });

            return new RasterResult
            {
                Width = W,
                Height = H,
                Pixels = pixels,
                Scale = s,
                ShapeRect = layout.ShapeRect,
                Padding = layout.Padding,
                Border = layout.Border,
                Warnings = ctx.Warnings,
            };
        }

        /// <summary>True when stretching a 9-sliced sprite would distort the fill (gradients are not uniform).</summary>
        public static bool HasNonUniformFill(UISpriteStyle style) =>
            style.fill.mode != FillMode.Solid || (style.stroke.enabled && style.stroke.fill.mode != FillMode.Solid);

        sealed class Context
        {
            public int Scale, Width, Height;
            public readonly List<string> Warnings = new List<string>();
        }

        // ------------------------------------------------------------------ layers

        static void RenderInto(Context ctx, Surface target, Layer layer, Placement p, float[] clip, string path)
        {
            if (!layer.visible) return;
            int s = ctx.Scale;

            // Groups without opacity, blend mode or effects pass through: their children blend straight into the target.
            if (layer is GroupLayer g && g.opacity >= 1f && g.blendMode == LayerBlendMode.Normal && !Fx.Of(g.effects).Any)
            {
                var childClip = clip;
                if (g.clip)
                {
                    var box = new RoundedRectDistance(target.Rect, p, Vector4.zero).Coverage(0f, 0f, 0f);
                    childClip = clip == null ? box : MultiplyMasks(clip, box);
                }
                for (int i = 0; i < g.children.Count; i++)
                    if (g.children[i] != null) RenderInto(ctx, target, g.children[i], p.Child(g.children[i], s), childClip, $"{path}.layers[{i}]");
                return;
            }

            var full = LayerGeometry.Full(layer, p, s);
            if (full == null) return;
            var rect = RectUtil.ToPixels(full.Value, ctx.Width, ctx.Height);
            if (rect.width <= 0 || rect.height <= 0) return;

            Surface surface;
            try
            {
                surface = RenderLayer(ctx, layer, p, rect, path);
            }
            catch (InvalidOperationException e)
            {
                ctx.Warnings.Add($"{path} ({layer.DisplayName}): {e.Message}");
                return;
            }
            if (surface != null) Blend(target, surface, Mathf.Clamp01(layer.opacity), layer.blendMode, clip);
        }

        static Surface RenderLayer(Context ctx, Layer layer, Placement p, RectInt rect, string path)
        {
            int s = ctx.Scale;
            var fx = Fx.Of(layer.effects);
            switch (layer)
            {
                case ShapeLayer shape:
                {
                    var surface = new Surface(rect);
                    float maxRadius = Mathf.Min(p.Hw, p.Hh);
                    Vector4 radii = shape.radius.GetRadii() * s;
                    for (int i = 0; i < 4; i++) radii[i] = Mathf.Clamp(radii[i], 0f, maxRadius);
                    var dist = new RoundedRectDistance(rect, p, radii);
                    var mask = dist.Coverage(0f, 0f, 0f);
                    DrawStack(ctx, surface, dist, mask, fx, p,
                        () => CompositeFill(surface, mask, new FillEvaluator(shape.fill, p.Hw, p.Hh), p),
                        () =>
                        {
                            for (int i = 0; i < shape.children.Count; i++)
                                if (shape.children[i] != null)
                                    RenderInto(ctx, surface, shape.children[i], p.Child(shape.children[i], s), shape.clip ? mask : null, $"{path}.layers[{i}]");
                        });
                    return surface;
                }
                case ImageLayer image:
                {
                    if (image.source == null) return null;
                    if (!ImageSource.IsSupported(image.source))
                        throw new InvalidOperationException($"source must be a Sprite or Texture2D, not {image.source.GetType().Name}");
                    var src = ImageSource.Load(image.source);
                    int bw = Mathf.Max(1, Mathf.RoundToInt(p.Hw * 2f)), bh = Mathf.Max(1, Mathf.RoundToInt(p.Hh * 2f));
                    var local = ImageSource.Fit(src, bw, bh, image.fit, image.tint, s);
                    var content = SampleImage(rect, p, local, bw, bh);
                    if (!fx.Any) return content;
                    var mask = AlphaOf(content);
                    var surface = new Surface(rect);
                    DrawStack(ctx, surface, new MaskDistance(rect, mask), mask, fx, p, () => Over(surface, content), null);
                    return surface;
                }
                case TextLayer text:
                {
                    if (string.IsNullOrEmpty(text.text)) return null;
                    string missing = TextEngine.MissingCharacters(text.font, text.text);
                    if (missing.Length > 0)
                        ctx.Warnings.Add($"{path} ({layer.DisplayName}): font \"{TextEngine.FontName(text.font)}\" has no glyphs for \"{missing}\"");
                    var layout = TextEngine.Layout(text, s);
                    var coverage = TextEngine.Rasterize(layout, out var area);
                    var mask = SampleCoverage(rect, p, coverage, area);
                    var surface = new Surface(rect);
                    DrawStack(ctx, surface, new MaskDistance(rect, mask), mask, fx, p,
                        () => CompositeFill(surface, mask, new FillEvaluator(text.fill, p.Hw, p.Hh), p), null);
                    return surface;
                }
                case GroupLayer group:
                {
                    var content = new Surface(rect);
                    var clip = group.clip ? new RoundedRectDistance(rect, p, Vector4.zero).Coverage(0f, 0f, 0f) : null;
                    for (int i = 0; i < group.children.Count; i++)
                        if (group.children[i] != null)
                            RenderInto(ctx, content, group.children[i], p.Child(group.children[i], s), clip, $"{path}.layers[{i}]");
                    if (!fx.Any) return content;
                    var mask = AlphaOf(content);
                    var surface = new Surface(rect);
                    DrawStack(ctx, surface, new MaskDistance(rect, mask), mask, fx, p, () => Over(surface, content), null);
                    return surface;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Draws effects and content in paint order: drop shadows, outer glow, content, inner shadow,
        /// inner glow, children, stroke.
        /// </summary>
        static void DrawStack(Context ctx, Surface acc, DistanceSource dist, float[] shapeMask, Fx fx, Placement p,
            Action content, Action children)
        {
            int s = ctx.Scale;
            int W = acc.Rect.width, H = acc.Rect.height;
            int n = W * H;

            // 1. Drop shadows
            foreach (var ds in fx.Shadows)
            {
                if (ds == null || !ds.enabled) continue;
                var m = dist.Coverage(ds.offset.x * s, -ds.offset.y * s, ds.spread * s);
                MaskOps.GaussianBlur(m, W, H, ds.blur * s * BlurToSigma);
                CompositeSolid(acc.Rgba, m, ds.color);
            }

            // 2. Outer glow
            var glow = fx.Glow;
            if (glow.enabled)
            {
                var m = dist.Coverage(0f, 0f, glow.spread * s);
                MaskOps.GaussianBlur(m, W, H, glow.size * s * BlurToSigma);
                MaskOps.Multiply(m, glow.intensity);
                CompositeSolid(acc.Rgba, m, glow.color);
            }

            // 3. Content
            content?.Invoke();

            // 4. Inner shadow / inner glow
            CompositeInner(fx.InnerShadow);
            CompositeInner(fx.InnerGlow);
            void CompositeInner(InnerEffectSettings e)
            {
                if (!e.enabled) return;
                var m = dist.Coverage(e.offset.x * s, -e.offset.y * s, -e.choke * s);
                for (int i = 0; i < n; i++) m[i] = 1f - m[i];
                MaskOps.GaussianBlur(m, W, H, e.blur * s * BlurToSigma);
                for (int i = 0; i < n; i++) m[i] *= shapeMask[i];
                CompositeSolid(acc.Rgba, m, e.color);
            }

            // 5. Children
            children?.Invoke();

            // 6. Stroke
            float strokeW = fx.StrokeWidth(s);
            if (fx.Stroke.enabled && strokeW > 0f)
            {
                float outer = fx.StrokeOuter(s), inner = outer - strokeW;
                var sdf = dist.Base;
                var m = new float[n];
                for (int i = 0; i < n; i++)
                    m[i] = Mathf.Max(0f, ShapeSdf.Coverage(sdf[i] - outer) - ShapeSdf.Coverage(sdf[i] - inner));
                CompositeFill(acc, m, new FillEvaluator(fx.Stroke.fill, p.Hw, p.Hh), p);
            }
        }

        // ------------------------------------------------------------------ sampling

        /// <summary>Samples a box-sized premultiplied bitmap (rows bottom-up) onto the canvas rect.</summary>
        static Surface SampleImage(RectInt rect, Placement p, float[] local, int bw, int bh)
        {
            var surface = new Surface(rect);
            var dst = surface.Rgba;
            float kx = bw / Mathf.Max(1e-4f, p.Hw * 2f), ky = bh / Mathf.Max(1e-4f, p.Hh * 2f);
            Parallel.For(0, rect.height, j =>
            {
                float py = rect.y + j + 0.5f;
                for (int i = 0; i < rect.width; i++)
                {
                    p.ToLocal(rect.x + i + 0.5f, py, out float lx, out float ly);
                    float u = (lx + p.Hw) * kx - 0.5f, v = (ly + p.Hh) * ky - 0.5f;
                    if (u <= -1f || v <= -1f || u >= bw || v >= bh) continue;
                    int o = (j * rect.width + i) * 4;
                    int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
                    float fx = u - x0, fy = v - y0;
                    Accumulate(local, bw, bh, x0, y0, (1 - fx) * (1 - fy), dst, o);
                    Accumulate(local, bw, bh, x0 + 1, y0, fx * (1 - fy), dst, o);
                    Accumulate(local, bw, bh, x0, y0 + 1, (1 - fx) * fy, dst, o);
                    Accumulate(local, bw, bh, x0 + 1, y0 + 1, fx * fy, dst, o);
                }
            });
            return surface;

            static void Accumulate(float[] src, int w, int h, int x, int y, float weight, float[] dst, int o)
            {
                if (weight <= 0f || x < 0 || y < 0 || x >= w || y >= h) return;
                int k = (y * w + x) * 4;
                dst[o] += src[k] * weight;
                dst[o + 1] += src[k + 1] * weight;
                dst[o + 2] += src[k + 2] * weight;
                dst[o + 3] += src[k + 3] * weight;
            }
        }

        /// <summary>Samples a coverage bitmap (rows top-down, placed at <paramref name="area"/> in box pixels, y down) onto the canvas rect.</summary>
        static float[] SampleCoverage(RectInt rect, Placement p, float[] coverage, RectInt area)
        {
            var mask = new float[rect.width * rect.height];
            int aw = area.width, ah = area.height;
            if (aw <= 0 || ah <= 0) return mask;
            Parallel.For(0, rect.height, j =>
            {
                float py = rect.y + j + 0.5f;
                for (int i = 0; i < rect.width; i++)
                {
                    p.ToLocal(rect.x + i + 0.5f, py, out float lx, out float ly);
                    float u = lx + p.Hw - area.x - 0.5f, v = p.Hh - ly - area.y - 0.5f;
                    if (u <= -1f || v <= -1f || u >= aw || v >= ah) continue;
                    int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
                    float fx = u - x0, fy = v - y0;
                    mask[j * rect.width + i] =
                        At(x0, y0) * (1 - fx) * (1 - fy) + At(x0 + 1, y0) * fx * (1 - fy) +
                        At(x0, y0 + 1) * (1 - fx) * fy + At(x0 + 1, y0 + 1) * fx * fy;
                }
            });
            return mask;

            float At(int x, int y) => x < 0 || y < 0 || x >= aw || y >= ah ? 0f : coverage[y * aw + x];
        }

        static float[] AlphaOf(Surface s)
        {
            var mask = new float[s.Count];
            for (int i = 0; i < mask.Length; i++) mask[i] = s.Rgba[i * 4 + 3];
            return mask;
        }

        static float[] MultiplyMasks(float[] a, float[] b)
        {
            var r = new float[a.Length];
            for (int i = 0; i < r.Length; i++) r[i] = a[i] * b[i];
            return r;
        }

        // ------------------------------------------------------------------ compositing

        static void Over(Surface dst, Surface src)
        {
            var d = dst.Rgba;
            var s = src.Rgba;
            Parallel.For(0, dst.Count, i =>
            {
                int j = i * 4;
                float k = 1f - s[j + 3];
                d[j] = s[j] + d[j] * k;
                d[j + 1] = s[j + 1] + d[j + 1] * k;
                d[j + 2] = s[j + 2] + d[j + 2] * k;
                d[j + 3] = s[j + 3] + d[j + 3] * k;
            });
        }

        /// <summary>
        /// Composites <paramref name="src"/> onto <paramref name="dst"/> (both premultiplied) with W3C separable
        /// blend modes. <paramref name="clip"/> (in dst's rect, optional) scales the source's coverage.
        /// </summary>
        static void Blend(Surface dst, Surface src, float opacity, LayerBlendMode mode, float[] clip)
        {
            var dr = dst.Rect;
            var sr = src.Rect;
            int x0 = Mathf.Max(dr.xMin, sr.xMin), x1 = Mathf.Min(dr.xMax, sr.xMax);
            int y0 = Mathf.Max(dr.yMin, sr.yMin), y1 = Mathf.Min(dr.yMax, sr.yMax);
            if (x1 <= x0 || y1 <= y0 || opacity <= 0f) return;

            var d = dst.Rgba;
            var s = src.Rgba;
            Parallel.For(y0, y1, y =>
            {
                for (int x = x0; x < x1; x++)
                {
                    int di = (y - dr.y) * dr.width + (x - dr.x);
                    int si = ((y - sr.y) * sr.width + (x - sr.x)) * 4;
                    float k = opacity * (clip != null ? clip[di] : 1f);
                    float sa = s[si + 3] * k;
                    if (sa <= 0f) continue;
                    float sr0 = s[si] * k, sg = s[si + 1] * k, sb = s[si + 2] * k;
                    int j = di * 4;
                    float da = d[j + 3];

                    if (mode == LayerBlendMode.Normal || da <= 0f)
                    {
                        float inv = 1f - sa;
                        d[j] = sr0 + d[j] * inv;
                        d[j + 1] = sg + d[j + 1] * inv;
                        d[j + 2] = sb + d[j + 2] * inv;
                        d[j + 3] = sa + da * inv;
                        continue;
                    }

                    // co = cs (1 - ab) + cb (1 - as) + as ab B(Cb, Cs)
                    float dr0 = d[j], dg = d[j + 1], db = d[j + 2];
                    float both = sa * da;
                    d[j] = sr0 * (1f - da) + dr0 * (1f - sa) + both * BlendChannel(mode, dr0 / da, sr0 / sa);
                    d[j + 1] = sg * (1f - da) + dg * (1f - sa) + both * BlendChannel(mode, dg / da, sg / sa);
                    d[j + 2] = sb * (1f - da) + db * (1f - sa) + both * BlendChannel(mode, db / da, sb / sa);
                    d[j + 3] = sa + da * (1f - sa);
                }
            });
        }

        static float BlendChannel(LayerBlendMode mode, float cb, float cs)
        {
            switch (mode)
            {
                case LayerBlendMode.Multiply: return cb * cs;
                case LayerBlendMode.Screen: return cb + cs - cb * cs;
                case LayerBlendMode.Overlay: return cb <= 0.5f ? 2f * cb * cs : 1f - 2f * (1f - cb) * (1f - cs);
                case LayerBlendMode.Darken: return Mathf.Min(cb, cs);
                case LayerBlendMode.Lighten: return Mathf.Max(cb, cs);
                case LayerBlendMode.Add: return Mathf.Min(1f, cb + cs);
                default: return cs;
            }
        }

        static void CompositeSolid(float[] acc, float[] mask, Color c)
        {
            Parallel.For(0, mask.Length, i =>
            {
                float a = c.a * mask[i];
                if (a <= 0f) return;
                float k = 1f - a;
                int j = i * 4;
                acc[j] = c.r * a + acc[j] * k;
                acc[j + 1] = c.g * a + acc[j + 1] * k;
                acc[j + 2] = c.b * a + acc[j + 2] * k;
                acc[j + 3] = a + acc[j + 3] * k;
            });
        }

        static void CompositeFill(Surface surface, float[] mask, FillEvaluator fill, Placement p)
        {
            var acc = surface.Rgba;
            var r = surface.Rect;
            int W = r.width;
            Parallel.For(0, r.height, j =>
            {
                int y = r.y + j;
                float ly = y + 0.5f - p.Cy;
                int row = j * W;
                for (int i = 0; i < W; i++)
                {
                    int idx = row + i;
                    if (mask[idx] <= 0f) continue;
                    int x = r.x + i;
                    Color c;
                    if (!p.Rotated)
                    {
                        c = fill.Evaluate(x + 0.5f - p.Cx, ly);
                    }
                    else
                    {
                        p.ToLocal(x + 0.5f, y + 0.5f, out float lx, out float lyr);
                        c = fill.Evaluate(lx, lyr);
                    }
                    float a = c.a * mask[idx];
                    float k = 1f - a;
                    int o = idx * 4;
                    acc[o] = c.r * a + acc[o] * k;
                    acc[o + 1] = c.g * a + acc[o + 1] * k;
                    acc[o + 2] = c.b * a + acc[o + 2] * k;
                    acc[o + 3] = a + acc[o + 3] * k;
                }
            });
        }

        // ------------------------------------------------------------------ 9-slice

        static Vector4 ComputeBorder(UISpriteStyle style, Vector4 radii, float strokeInside, int s,
            int W, int H, int padL, int padB, int padR, int padT)
        {
            // Everything that varies across the edge must live inside the fixed border region.
            float inset = Mathf.Max(0f, strokeInside);
            foreach (var fx in new[] { style.innerShadow, style.innerGlow })
            {
                if (!fx.enabled) continue;
                float off = Mathf.Max(Mathf.Abs(fx.offset.x), Mathf.Abs(fx.offset.y));
                inset = Mathf.Max(inset, (fx.blur * BlurExtent + fx.choke + off) * s);
            }

            float left = padL + Mathf.Max(inset, Mathf.Max(radii.x, radii.w));
            float right = padR + Mathf.Max(inset, Mathf.Max(radii.y, radii.z));
            float top = padT + Mathf.Max(inset, Mathf.Max(radii.x, radii.y));
            float bottom = padB + Mathf.Max(inset, Mathf.Max(radii.z, radii.w));

            var border = new Vector4(
                Mathf.CeilToInt(left) + 1, Mathf.CeilToInt(bottom) + 1,
                Mathf.CeilToInt(right) + 1, Mathf.CeilToInt(top) + 1);

            // Keep at least one stretchable pixel in the middle.
            if (border.x + border.z >= W)
            {
                float half = Mathf.Floor((W - 1) * 0.5f);
                border.x = border.z = half;
            }
            if (border.y + border.w >= H)
            {
                float half = Mathf.Floor((H - 1) * 0.5f);
                border.y = border.w = half;
            }
            return border;
        }

        /// <summary>
        /// Moves the 9-slice stretch band to the widest run of columns/rows no layer covers, so decorations
        /// stay in the fixed border regions. Layers with a Stretch constraint on an axis are expected to
        /// stretch along it and don't block that axis.
        /// </summary>
        static void FitBorderAroundLayers(UISpriteStyle style, SpriteLayout layout)
        {
            var root = Placement.Root(layout.ShapeRect);
            var xs = new List<Vector2>();
            var ys = new List<Vector2>();
            foreach (var layer in style.layers)
            {
                if (layer == null) continue;
                var bounds = LayerGeometry.Full(layer, root.Child(layer, layout.Scale), layout.Scale);
                if (bounds is not Rect b) continue;
                if (style.clip) b = Rect.MinMaxRect(
                    Mathf.Max(b.xMin, layout.ShapeRect.xMin), Mathf.Max(b.yMin, layout.ShapeRect.yMin),
                    Mathf.Min(b.xMax, layout.ShapeRect.xMax), Mathf.Min(b.yMax, layout.ShapeRect.yMax));
                if (layer.horizontal != HorizontalConstraint.Stretch) xs.Add(new Vector2(b.xMin, b.xMax));
                if (layer.vertical != VerticalConstraint.Stretch) ys.Add(new Vector2(b.yMin, b.yMax));
            }

            var border = layout.Border;
            layout.SliceBlockedX = !FitBand(ref border.x, ref border.z, layout.Width, xs);
            layout.SliceBlockedY = !FitBand(ref border.y, ref border.w, layout.Height, ys);
            layout.Border = border;
        }

        /// <returns>False when every pixel of the stretch band is covered.</returns>
        static bool FitBand(ref float low, ref float high, int size, List<Vector2> spans)
        {
            int a = (int)low, b = size - (int)high;
            if (spans.Count == 0 || b <= a) return true;

            var used = new bool[size];
            foreach (var span in spans)
            {
                int from = Mathf.Max(0, Mathf.FloorToInt(span.x)), to = Mathf.Min(size, Mathf.CeilToInt(span.y));
                for (int i = from; i < to; i++) used[i] = true;
            }

            int bestStart = -1, bestLen = 0;
            for (int i = a; i < b;)
            {
                if (used[i]) { i++; continue; }
                int start = i;
                while (i < b && !used[i]) i++;
                if (i - start > bestLen)
                {
                    bestStart = start;
                    bestLen = i - start;
                }
            }
            if (bestLen == 0) return false;
            low = bestStart;
            high = size - (bestStart + bestLen);
            return true;
        }

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
