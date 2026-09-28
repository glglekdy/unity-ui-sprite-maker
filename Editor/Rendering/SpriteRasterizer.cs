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

    public static class SpriteRasterizer
    {
        public const int MaxSize = 2048;

        // Blur values are treated as CSS blur radii: sigma = blur / 2, visible extent ~ 3 sigma.
        const float BlurToSigma = 0.5f;
        const float BlurExtent = 1.5f;

        public static RasterResult Render(UISpriteStyle style)
        {
            var shape = style.shape;
            int s = Mathf.Clamp(shape.scale, 1, 4);
            int w = Mathf.Clamp(shape.size.x, 1, MaxSize) * s;
            int h = Mathf.Clamp(shape.size.y, 1, MaxSize) * s;
            float hw = w * 0.5f, hh = h * 0.5f;

            float maxRadius = Mathf.Min(hw, hh);
            Vector4 radii = shape.GetRadii() * s;
            for (int i = 0; i < 4; i++)
                radii[i] = Mathf.Clamp(radii[i], 0f, maxRadius);

            // --- Padding needed around the shape for outer effects ---
            float pl = 0f, pr = 0f, pb = 0f, pt = 0f;
            void Grow(float extent, float ox, float oyUp)
            {
                pl = Mathf.Max(pl, extent - ox);
                pr = Mathf.Max(pr, extent + ox);
                pb = Mathf.Max(pb, extent - oyUp);
                pt = Mathf.Max(pt, extent + oyUp);
            }

            var stroke = style.stroke;
            float strokeW = stroke.enabled ? stroke.width * s : 0f;
            float strokeOuter = stroke.position switch
            {
                StrokePosition.Outside => strokeW,
                StrokePosition.Center => strokeW * 0.5f,
                _ => 0f,
            };
            Grow(strokeOuter, 0f, 0f);

            foreach (var ds in style.dropShadows)
            {
                if (ds == null || !ds.enabled) continue;
                Grow((ds.spread + ds.blur * BlurExtent) * s, ds.offset.x * s, -ds.offset.y * s);
            }

            var glow = style.outerGlow;
            if (glow.enabled)
                Grow((glow.spread + glow.size * BlurExtent) * s, 0f, 0f);

            // +1 px so anti-aliased edges never touch the texture border.
            int padL = Mathf.CeilToInt(pl) + 1;
            int padR = Mathf.CeilToInt(pr) + 1;
            int padB = Mathf.CeilToInt(pb) + 1;
            int padT = Mathf.CeilToInt(pt) + 1;

            int W = w + padL + padR;
            int H = h + padB + padT;
            int n = W * H;
            float cx = padL + hw, cy = padB + hh;

            // --- Base signed distance field ---
            var sdf = new float[n];
            Parallel.For(0, H, y =>
            {
                float py = y + 0.5f - cy;
                int row = y * W;
                for (int x = 0; x < W; x++)
                    sdf[row + x] = ShapeSdf.RoundedRect(x + 0.5f - cx, py, hw, hh, radii);
            });

            float[] Coverage(float ox, float oyUp, float grow)
            {
                var mask = new float[n];
                bool shifted = ox != 0f || oyUp != 0f;
                Parallel.For(0, H, y =>
                {
                    float py = y + 0.5f - cy - oyUp;
                    int row = y * W;
                    for (int x = 0; x < W; x++)
                    {
                        float d = shifted
                            ? ShapeSdf.RoundedRect(x + 0.5f - cx - ox, py, hw, hh, radii)
                            : sdf[row + x];
                        mask[row + x] = ShapeSdf.Coverage(d - grow);
                    }
                });
                return mask;
            }

            var shapeMask = Coverage(0f, 0f, 0f);
            var acc = new float[n * 4]; // premultiplied RGBA

            // 1. Drop shadows
            foreach (var ds in style.dropShadows)
            {
                if (ds == null || !ds.enabled) continue;
                var m = Coverage(ds.offset.x * s, -ds.offset.y * s, ds.spread * s);
                MaskOps.GaussianBlur(m, W, H, ds.blur * s * BlurToSigma);
                CompositeSolid(acc, m, ds.color);
            }

            // 2. Outer glow
            if (glow.enabled)
            {
                var m = Coverage(0f, 0f, glow.spread * s);
                MaskOps.GaussianBlur(m, W, H, glow.size * s * BlurToSigma);
                MaskOps.Multiply(m, glow.intensity);
                CompositeSolid(acc, m, glow.color);
            }

            // 3. Fill
            CompositeFill(acc, shapeMask, new FillEvaluator(style.fill, hw, hh), W, H, cx, cy);

            // 4. Inner shadow / inner glow
            CompositeInner(style.innerShadow);
            CompositeInner(style.innerGlow);
            void CompositeInner(InnerEffectSettings fx)
            {
                if (!fx.enabled) return;
                var m = Coverage(fx.offset.x * s, -fx.offset.y * s, -fx.choke * s);
                for (int i = 0; i < n; i++) m[i] = 1f - m[i];
                MaskOps.GaussianBlur(m, W, H, fx.blur * s * BlurToSigma);
                for (int i = 0; i < n; i++) m[i] *= shapeMask[i];
                CompositeSolid(acc, m, fx.color);
            }

            // 5. Stroke
            if (stroke.enabled && strokeW > 0f)
            {
                float outer = strokeOuter, inner = strokeOuter - strokeW;
                var m = new float[n];
                for (int i = 0; i < n; i++)
                    m[i] = Mathf.Max(0f, ShapeSdf.Coverage(sdf[i] - outer) - ShapeSdf.Coverage(sdf[i] - inner));
                CompositeFill(acc, m, new FillEvaluator(stroke.fill, hw, hh), W, H, cx, cy);
            }

            // --- Resolve to straight alpha ---
            var pixels = new Color32[n];
            Parallel.For(0, n, i =>
            {
                int j = i * 4;
                float a = acc[j + 3];
                if (a <= 1e-5f) return;
                float inv = 1f / a;
                pixels[i] = new Color32(
                    ToByte(acc[j] * inv), ToByte(acc[j + 1] * inv), ToByte(acc[j + 2] * inv), ToByte(a));
            });

            return new RasterResult
            {
                Width = W,
                Height = H,
                Pixels = pixels,
                Scale = s,
                ShapeRect = new RectInt(padL, padB, w, h),
                Padding = new Vector4(padL, padB, padR, padT),
                Border = style.nineSlice
                    ? ComputeBorder(style, radii, strokeW - strokeOuter, s, W, H, padL, padB, padR, padT)
                    : Vector4.zero,
            };
        }

        /// <summary>True when stretching a 9-sliced sprite would distort the fill (gradients are not uniform).</summary>
        public static bool HasNonUniformFill(UISpriteStyle style) =>
            style.fill.mode != FillMode.Solid || (style.stroke.enabled && style.stroke.fill.mode != FillMode.Solid);

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

        static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

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

        static void CompositeFill(float[] acc, float[] mask, FillEvaluator fill, int W, int H, float cx, float cy)
        {
            Parallel.For(0, H, y =>
            {
                float ly = y + 0.5f - cy;
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    int i = row + x;
                    if (mask[i] <= 0f) continue;
                    var c = fill.Evaluate(x + 0.5f - cx, ly);
                    float a = c.a * mask[i];
                    float k = 1f - a;
                    int j = i * 4;
                    acc[j] = c.r * a + acc[j] * k;
                    acc[j + 1] = c.g * a + acc[j + 1] * k;
                    acc[j + 2] = c.b * a + acc[j + 2] * k;
                    acc[j + 3] = a + acc[j + 3] * k;
                }
            });
        }

        /// <summary>Thread-safe fill sampler. Gradients are baked to a lookup table on the main thread.</summary>
        readonly struct FillEvaluator
        {
            const int LutSize = 512;

            readonly FillMode _mode;
            readonly Color _solid;
            readonly Color[] _lut;
            readonly float _dirX, _dirY, _extent;
            readonly float _centerX, _centerY, _radius;

            public FillEvaluator(FillSettings f, float hw, float hh)
            {
                _mode = f.mode;
                _solid = f.color;
                _lut = null;
                _dirX = _dirY = _extent = _centerX = _centerY = _radius = 0f;

                if (_mode == FillMode.Solid) return;

                var gradient = f.gradient ?? new Gradient();
                _lut = new Color[LutSize];
                for (int i = 0; i < LutSize; i++)
                    _lut[i] = gradient.Evaluate(i / (LutSize - 1f));

                float rad = f.angle * Mathf.Deg2Rad;
                _dirX = Mathf.Cos(rad);
                _dirY = Mathf.Sin(rad);
                _extent = Mathf.Max(1e-4f, Mathf.Abs(hw * _dirX) + Mathf.Abs(hh * _dirY));

                _centerX = (f.radialCenter.x - 0.5f) * hw * 2f;
                _centerY = (f.radialCenter.y - 0.5f) * hh * 2f;
                _radius = Mathf.Max(1e-4f, f.radialRadius * Mathf.Sqrt(hw * hw + hh * hh));
            }

            public Color Evaluate(float lx, float ly)
            {
                float t;
                switch (_mode)
                {
                    case FillMode.LinearGradient:
                        t = (lx * _dirX + ly * _dirY) / _extent * 0.5f + 0.5f;
                        break;
                    case FillMode.RadialGradient:
                        float dx = lx - _centerX, dy = ly - _centerY;
                        t = Mathf.Sqrt(dx * dx + dy * dy) / _radius;
                        break;
                    default:
                        return _solid;
                }
                return _lut[(int)(Mathf.Clamp01(t) * (LutSize - 1) + 0.5f)];
            }
        }
    }
}
