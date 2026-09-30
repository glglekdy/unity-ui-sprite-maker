using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>Pixels of an image layer's source, premultiplied, rows bottom-up.</summary>
    internal sealed class SourceImage
    {
        public int Width, Height;
        public float[] Rgba;
        /// <summary>9-slice border in source pixels (left, bottom, right, top).</summary>
        public Vector4 Border;
        public float PixelsPerUnit = 100f;
    }

    /// <summary>Reads the pixels of a Sprite or Texture2D without depending on its import settings.</summary>
    internal static class ImageSource
    {
        const int CacheLimit = 32;
        static readonly Dictionary<(UnityEngine.Object, long, int, int), SourceImage> Cache = new Dictionary<(UnityEngine.Object, long, int, int), SourceImage>();

        public static bool IsSupported(UnityEngine.Object source) => source is Sprite || source is Texture2D;

        /// <exception cref="InvalidOperationException">The pixels can't be read.</exception>
        public static SourceImage Load(UnityEngine.Object source)
        {
            var texture = source switch
            {
                Sprite sp => sp.texture,
                Texture2D t => t,
                _ => throw new InvalidOperationException($"{source.GetType().Name} is not a Sprite or Texture2D"),
            };
            if (texture == null) throw new InvalidOperationException($"\"{source.name}\" has no texture");

            string path = AssetDatabase.GetAssetPath(texture);
            long stamp = !string.IsNullOrEmpty(path) && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
            var key = (source, stamp, texture.width, texture.height);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var full = ReadTexture(texture, path, out float ratio);
            var image = source is Sprite sprite ? Crop(full, sprite, ratio) : full;
            if (Cache.Count >= CacheLimit) Cache.Clear();
            Cache[key] = image;
            return image;
        }

        static SourceImage ReadTexture(Texture2D texture, string path, out float ratio)
        {
            Color32[] pixels;
            int w, h;
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                // Decode the file itself: works for non-readable and compressed textures, and without a GPU.
                var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!tmp.LoadImage(File.ReadAllBytes(path)))
                        throw new InvalidOperationException($"can't decode \"{path}\"");
                    w = tmp.width;
                    h = tmp.height;
                    pixels = tmp.GetPixels32();
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tmp);
                }
            }
            else if (texture.isReadable)
            {
                w = texture.width;
                h = texture.height;
                pixels = texture.GetPixels32();
            }
            else
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException(
                        $"\"{path}\" is not a PNG/JPG and not readable. Enable Read/Write on the texture, or run without -nographics");
                w = texture.width;
                h = texture.height;
                pixels = ReadBack(texture);
            }

            ratio = texture.width > 0 ? (float)w / texture.width : 1f;
            var rgba = new float[w * h * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                float a = c.a / 255f;
                rgba[i * 4] = c.r / 255f * a;
                rgba[i * 4 + 1] = c.g / 255f * a;
                rgba[i * 4 + 2] = c.b / 255f * a;
                rgba[i * 4 + 3] = a;
            }
            return new SourceImage { Width = w, Height = h, Rgba = rgba };
        }

        static Color32[] ReadBack(Texture2D texture)
        {
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply(false);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        /// <param name="ratio">Source file pixels per imported texture pixel (the import may downscale).</param>
        static SourceImage Crop(SourceImage full, Sprite sprite, float ratio)
        {
            var r = sprite.rect;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(r.x * ratio), 0, full.Width - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(r.y * ratio), 0, full.Height - 1);
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width * ratio), 1, full.Width - x0);
            int h = Mathf.Clamp(Mathf.RoundToInt(r.height * ratio), 1, full.Height - y0);

            var rgba = new float[w * h * 4];
            for (int y = 0; y < h; y++)
                Array.Copy(full.Rgba, ((y0 + y) * full.Width + x0) * 4, rgba, y * w * 4, w * 4);
            return new SourceImage
            {
                Width = w,
                Height = h,
                Rgba = rgba,
                Border = sprite.border * ratio,
                PixelsPerUnit = sprite.pixelsPerUnit * ratio,
            };
        }

        /// <summary>
        /// Resamples the image into a box of <paramref name="bw"/>×<paramref name="bh"/> pixels (premultiplied,
        /// rows bottom-up) according to <paramref name="fit"/>, tinted.
        /// </summary>
        public static float[] Fit(SourceImage src, int bw, int bh, ImageFit fit, Color tint, float scale)
        {
            var dst = new float[bw * bh * 4];
            float sw = src.Width, sh = src.Height;

            // Destination pixel → source pixel mapping, per axis.
            float kx = bw / sw, ky = bh / sh, ox = 0f, oy = 0f;
            bool sliced = fit == ImageFit.Sliced && src.Border != Vector4.zero;
            switch (fit)
            {
                case ImageFit.Contain:
                case ImageFit.Cover:
                    float k = fit == ImageFit.Contain ? Mathf.Min(kx, ky) : Mathf.Max(kx, ky);
                    kx = ky = k;
                    ox = (bw - sw * k) * 0.5f;
                    oy = (bh - sh * k) * 0.5f;
                    break;
            }

            // Slice borders in destination pixels (UGUI: border / pixelsPerUnit * 100 units).
            float bl = 0, bb = 0, br = 0, bt = 0;
            if (sliced)
            {
                float unit = 100f / Mathf.Max(0.01f, src.PixelsPerUnit) * scale;
                bl = src.Border.x * unit; bb = src.Border.y * unit; br = src.Border.z * unit; bt = src.Border.w * unit;
                float fx = bl + br > bw ? bw / (bl + br) : 1f, fy = bb + bt > bh ? bh / (bb + bt) : 1f;
                bl *= fx; br *= fx; bb *= fy; bt *= fy;
            }

            // Downscaling a lot with bilinear sampling aliases: pre-shrink with a box filter.
            var img = src;
            float pre = 1f;
            if (!sliced)
            {
                int factor = Mathf.FloorToInt(Mathf.Min(1f / kx, 1f / ky));
                if (factor >= 2)
                {
                    img = BoxShrink(src, factor);
                    pre = (float)img.Width / src.Width;
                }
            }

            float tr = tint.r * tint.a, tg = tint.g * tint.a, tb = tint.b * tint.a, ta = tint.a;
            System.Threading.Tasks.Parallel.For(0, bh, y =>
            {
                float dy = y + 0.5f;
                float sy = sliced ? SliceMap(dy, bb, bh - bt, bh, src.Border.y, sh - src.Border.w, sh) : (dy - oy) / ky;
                if (!sliced && (sy < 0f || sy > sh)) return;
                for (int x = 0; x < bw; x++)
                {
                    float dx = x + 0.5f;
                    float sx = sliced ? SliceMap(dx, bl, bw - br, bw, src.Border.x, sw - src.Border.z, sw) : (dx - ox) / kx;
                    if (!sliced && (sx < 0f || sx > sw)) continue;
                    int j = (y * bw + x) * 4;
                    Bilinear(img, sx * pre - 0.5f, sy * pre - 0.5f, out float r, out float g, out float b, out float a);
                    dst[j] = r * tr;
                    dst[j + 1] = g * tg;
                    dst[j + 2] = b * tb;
                    dst[j + 3] = a * ta;
                }
            });
            return dst;
        }

        /// <summary>Maps a destination coordinate to the source for a 9-sliced axis.</summary>
        static float SliceMap(float d, float d0, float d1, float dLen, float s0, float s1, float sLen)
        {
            if (d < d0) return d0 > 0f ? d / d0 * s0 : 0f;
            if (d > d1) return dLen - d1 > 0f ? s1 + (d - d1) / (dLen - d1) * (sLen - s1) : sLen;
            return d1 - d0 > 0f ? s0 + (d - d0) / (d1 - d0) * (s1 - s0) : (s0 + s1) * 0.5f;
        }

        public static void Bilinear(SourceImage img, float x, float y, out float r, out float g, out float b, out float a)
        {
            int w = img.Width, h = img.Height;
            x = Mathf.Clamp(x, 0f, w - 1);
            y = Mathf.Clamp(y, 0f, h - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float fx = x - x0, fy = y - y0;
            float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            int i00 = (y0 * w + x0) * 4, i10 = (y0 * w + x1) * 4, i01 = (y1 * w + x0) * 4, i11 = (y1 * w + x1) * 4;
            var p = img.Rgba;
            r = p[i00] * w00 + p[i10] * w10 + p[i01] * w01 + p[i11] * w11;
            g = p[i00 + 1] * w00 + p[i10 + 1] * w10 + p[i01 + 1] * w01 + p[i11 + 1] * w11;
            b = p[i00 + 2] * w00 + p[i10 + 2] * w10 + p[i01 + 2] * w01 + p[i11 + 2] * w11;
            a = p[i00 + 3] * w00 + p[i10 + 3] * w10 + p[i01 + 3] * w01 + p[i11 + 3] * w11;
        }

        static SourceImage BoxShrink(SourceImage src, int f)
        {
            int w = Math.Max(1, src.Width / f), h = Math.Max(1, src.Height / f);
            var rgba = new float[w * h * 4];
            float inv = 1f / (f * f);
            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int j = (y * w + x) * 4;
                    for (int yy = 0; yy < f; yy++)
                    {
                        int sy = Math.Min(y * f + yy, src.Height - 1);
                        for (int xx = 0; xx < f; xx++)
                        {
                            int i = (sy * src.Width + Math.Min(x * f + xx, src.Width - 1)) * 4;
                            rgba[j] += src.Rgba[i];
                            rgba[j + 1] += src.Rgba[i + 1];
                            rgba[j + 2] += src.Rgba[i + 2];
                            rgba[j + 3] += src.Rgba[i + 3];
                        }
                    }
                    rgba[j] *= inv;
                    rgba[j + 1] *= inv;
                    rgba[j + 2] *= inv;
                    rgba[j + 3] *= inv;
                }
            });
            return new SourceImage { Width = w, Height = h, Rgba = rgba, Border = src.Border / f, PixelsPerUnit = src.PixelsPerUnit / f };
        }
    }
}
