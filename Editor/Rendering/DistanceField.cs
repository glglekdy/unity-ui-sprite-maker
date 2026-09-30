using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Signed distance (pixels, negative inside) of a layer's content over a surface rect.
    /// Effects (offset, spread, choke, stroke) are all computed from it, for any kind of layer.
    /// </summary>
    internal abstract class DistanceSource
    {
        protected readonly RectInt Rect;

        protected DistanceSource(RectInt rect) => Rect = rect;

        /// <summary>Distance with the content in place.</summary>
        public abstract float[] Base { get; }

        /// <summary>Distance with the content moved by (<paramref name="ox"/>, <paramref name="oyUp"/>) pixels.</summary>
        protected abstract float[] Shifted(float ox, float oyUp);

        /// <summary>Anti-aliased coverage of the content moved by an offset and grown by <paramref name="grow"/> pixels.</summary>
        public float[] Coverage(float ox, float oyUp, float grow)
        {
            var d = ox != 0f || oyUp != 0f ? Shifted(ox, oyUp) : Base;
            var mask = new float[d.Length];
            Parallel.For(0, Rect.height, y =>
            {
                int row = y * Rect.width;
                for (int x = 0; x < Rect.width; x++)
                    mask[row + x] = ShapeSdf.Coverage(d[row + x] - grow);
            });
            return mask;
        }
    }

    /// <summary>Exact distance to a (possibly rotated) rounded rect.</summary>
    internal sealed class RoundedRectDistance : DistanceSource
    {
        readonly Placement _p;
        readonly Vector4 _radii;
        float[] _base;

        public RoundedRectDistance(RectInt rect, Placement p, Vector4 radii) : base(rect)
        {
            _p = p;
            _radii = radii;
        }

        public override float[] Base => _base ??= Shifted(0f, 0f);

        protected override float[] Shifted(float ox, float oyUp)
        {
            var d = new float[Rect.width * Rect.height];
            bool shifted = ox != 0f || oyUp != 0f;
            var p = _p;
            var radii = _radii;
            int x0 = Rect.x, y0 = Rect.y, w = Rect.width;
            Parallel.For(0, Rect.height, j =>
            {
                int y = y0 + j;
                int row = j * w;
                if (!p.Rotated)
                {
                    // Same arithmetic as the original single-shape renderer, so old sprites render identically.
                    float py = shifted ? y + 0.5f - p.Cy - oyUp : y + 0.5f - p.Cy;
                    for (int i = 0; i < w; i++)
                    {
                        int x = x0 + i;
                        float px = shifted ? x + 0.5f - p.Cx - ox : x + 0.5f - p.Cx;
                        d[row + i] = ShapeSdf.RoundedRect(px, py, p.Hw, p.Hh, radii);
                    }
                }
                else
                {
                    for (int i = 0; i < w; i++)
                    {
                        p.ToLocal(x0 + i + 0.5f - ox, y + 0.5f - oyUp, out float lx, out float ly);
                        d[row + i] = ShapeSdf.RoundedRect(lx, ly, p.Hw, p.Hh, radii);
                    }
                }
            });
            return d;
        }
    }

    /// <summary>Distance derived from an anti-aliased coverage mask (images, text, groups).</summary>
    internal sealed class MaskDistance : DistanceSource
    {
        readonly float[] _mask;
        float[] _base;

        public MaskDistance(RectInt rect, float[] mask) : base(rect) => _mask = mask;

        public override float[] Base => _base ??= DistanceField.FromCoverage(_mask, Rect.width, Rect.height);

        protected override float[] Shifted(float ox, float oyUp)
        {
            var src = Base;
            int w = Rect.width, h = Rect.height;
            var d = new float[src.Length];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                    d[y * w + x] = DistanceField.Sample(src, w, h, x - ox, y - oyUp);
            });
            return d;
        }
    }

    internal static class DistanceField
    {
        const float Inf = 1e20f;
        const float Far = 1e6f;

        /// <summary>
        /// Signed distance field (pixels, negative inside) from a coverage mask, so that
        /// <see cref="ShapeSdf.Coverage"/> of the result gives the mask back.
        /// </summary>
        public static float[] FromCoverage(float[] mask, int w, int h)
        {
            int n = w * h;
            var toInside = new float[n];
            var toOutside = new float[n];
            bool anyInside = false, anyOutside = false;
            for (int i = 0; i < n; i++)
            {
                bool inside = mask[i] >= 0.5f;
                anyInside |= inside;
                anyOutside |= !inside;
                toInside[i] = inside ? 0f : Inf;
                toOutside[i] = inside ? Inf : 0f;
            }

            var sdf = new float[n];
            if (!anyInside)
            {
                for (int i = 0; i < n; i++) sdf[i] = mask[i] > 0f ? 0.5f - mask[i] : Far;
                return sdf;
            }

            SquaredEdt(toInside, w, h);
            if (anyOutside) SquaredEdt(toOutside, w, h);

            for (int i = 0; i < n; i++)
            {
                float m = mask[i];
                if (m > 0f && m < 1f)
                    sdf[i] = 0.5f - m; // edge pixel: keep the exact anti-aliased coverage
                else if (m >= 0.5f)
                    sdf[i] = anyOutside ? -(Mathf.Sqrt(toOutside[i]) - 0.5f) : -Far;
                else
                    sdf[i] = Mathf.Sqrt(toInside[i]) - 0.5f;
            }
            return sdf;
        }

        /// <summary>Bilinear sample; outside the field the distance keeps growing with the distance to its edge.</summary>
        public static float Sample(float[] field, int w, int h, float x, float y)
        {
            float cx = Mathf.Clamp(x, 0f, w - 1), cy = Mathf.Clamp(y, 0f, h - 1);
            float ex = x - cx, ey = y - cy;
            int x0 = (int)cx, y0 = (int)cy;
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float fx = cx - x0, fy = cy - y0;
            float a = field[y0 * w + x0], b = field[y0 * w + x1];
            float c = field[y1 * w + x0], d = field[y1 * w + x1];
            float v = Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
            if (ex != 0f || ey != 0f) v += Mathf.Sqrt(ex * ex + ey * ey);
            return v;
        }

        // Felzenszwalb & Huttenlocher, "Distance Transforms of Sampled Functions".
        static void SquaredEdt(float[] grid, int w, int h)
        {
            Parallel.For(0, w, x =>
            {
                var f = new float[h];
                var d = new float[h];
                var v = new int[h];
                var z = new float[h + 1];
                for (int y = 0; y < h; y++) f[y] = grid[y * w + x];
                Edt1D(f, h, d, v, z);
                for (int y = 0; y < h; y++) grid[y * w + x] = d[y];
            });
            Parallel.For(0, h, y =>
            {
                var f = new float[w];
                var d = new float[w];
                var v = new int[w];
                var z = new float[w + 1];
                Array.Copy(grid, y * w, f, 0, w);
                Edt1D(f, w, d, v, z);
                Array.Copy(d, 0, grid, y * w, w);
            });
        }

        static void Edt1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = Intersect(f, q, v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = Intersect(f, q, v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        static float Intersect(float[] f, int q, int p)
        {
            // Both infinite: treat the parabolas as never crossing before q.
            if (f[q] >= Inf && f[p] >= Inf) return float.PositiveInfinity;
            return (f[q] + (float)q * q - (f[p] + (float)p * p)) / (2f * q - 2f * p);
        }
    }
}
