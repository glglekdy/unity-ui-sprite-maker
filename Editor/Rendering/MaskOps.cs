using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    internal static class MaskOps
    {
        /// <summary>Approximates a gaussian blur with three box blur passes. Edges are clamped.</summary>
        public static void GaussianBlur(float[] data, int width, int height, float sigma)
        {
            if (sigma < 0.1f) return;

            var tmp = new float[data.Length];
            foreach (int radius in BoxRadiiForGauss(sigma, 3))
            {
                BoxHorizontal(data, tmp, width, height, radius);
                BoxVertical(tmp, data, width, height, radius);
            }
        }

        public static void Multiply(float[] data, float factor)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] = Mathf.Clamp01(data[i] * factor);
        }

        // http://blog.ivank.net/fastest-gaussian-blur.html
        static int[] BoxRadiiForGauss(float sigma, int n)
        {
            float wIdeal = Mathf.Sqrt(12f * sigma * sigma / n + 1f);
            int wl = Mathf.FloorToInt(wIdeal);
            if (wl % 2 == 0) wl--;
            int wu = wl + 2;

            float mIdeal = (12f * sigma * sigma - n * wl * wl - 4f * n * wl - 3f * n) / (-4f * wl - 4f);
            int m = Mathf.RoundToInt(mIdeal);

            var radii = new int[n];
            for (int i = 0; i < n; i++)
                radii[i] = ((i < m ? wl : wu) - 1) / 2;
            return radii;
        }

        static void BoxHorizontal(float[] src, float[] dst, int w, int h, int r)
        {
            if (r <= 0)
            {
                Array.Copy(src, dst, src.Length);
                return;
            }

            float inv = 1f / (2 * r + 1);
            Parallel.For(0, h, y =>
            {
                int row = y * w;
                float acc = 0f;
                for (int i = -r; i <= r; i++)
                    acc += src[row + Math.Clamp(i, 0, w - 1)];

                for (int x = 0; x < w; x++)
                {
                    dst[row + x] = acc * inv;
                    acc += src[row + Math.Min(x + r + 1, w - 1)] - src[row + Math.Max(x - r, 0)];
                }
            });
        }

        static void BoxVertical(float[] src, float[] dst, int w, int h, int r)
        {
            if (r <= 0)
            {
                Array.Copy(src, dst, src.Length);
                return;
            }

            float inv = 1f / (2 * r + 1);
            Parallel.For(0, w, x =>
            {
                float acc = 0f;
                for (int i = -r; i <= r; i++)
                    acc += src[Math.Clamp(i, 0, h - 1) * w + x];

                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = acc * inv;
                    acc += src[Math.Min(y + r + 1, h - 1) * w + x] - src[Math.Max(y - r, 0) * w + x];
                }
            });
        }
    }
}
