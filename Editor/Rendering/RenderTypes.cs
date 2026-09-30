using System.Collections.Generic;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Where a layer's box sits on the canvas: center in canvas pixels (y up), half size in pixels and
    /// total rotation (clockwise degrees, including all parents).
    /// </summary>
    internal struct Placement
    {
        public float Cx, Cy;
        public float Hw, Hh;
        public float Angle;
        public float Cos, Sin;
        public bool Rotated;

        public static Placement Axis(float cx, float cy, float hw, float hh) => new Placement
        {
            Cx = cx, Cy = cy, Hw = hw, Hh = hh, Cos = 1f,
        };

        /// <summary>Placement of the frame's shape inside a canvas.</summary>
        public static Placement Root(RectInt shapeRect)
        {
            float hw = shapeRect.width * 0.5f, hh = shapeRect.height * 0.5f;
            return Axis(shapeRect.x + hw, shapeRect.y + hh, hw, hh);
        }

        /// <summary>Placement of <paramref name="layer"/>, whose box is relative to this placement's box.</summary>
        public Placement Child(Layer layer, float scale)
        {
            float w = Mathf.Max(0f, layer.size.x) * scale, h = Mathf.Max(0f, layer.size.y) * scale;
            float lx = layer.position.x * scale + w * 0.5f - Hw;
            float ly = Hh - (layer.position.y * scale + h * 0.5f);
            var c = ToCanvas(lx, ly);
            var p = new Placement { Cx = c.x, Cy = c.y, Hw = w * 0.5f, Hh = h * 0.5f };
            p.SetAngle(Angle + layer.rotation);
            return p;
        }

        public void SetAngle(float degrees)
        {
            Angle = degrees;
            float r = Mathf.Repeat(degrees, 360f);
            Rotated = r > 1e-4f && r < 360f - 1e-4f;
            float rad = degrees * Mathf.Deg2Rad;
            Cos = Rotated ? Mathf.Cos(rad) : 1f;
            Sin = Rotated ? Mathf.Sin(rad) : 0f;
        }

        /// <summary>Canvas point → offset from the box center in the box's own (unrotated) frame, y up.</summary>
        public void ToLocal(float px, float py, out float lx, out float ly)
        {
            float dx = px - Cx, dy = py - Cy;
            if (!Rotated)
            {
                lx = dx;
                ly = dy;
                return;
            }
            lx = dx * Cos - dy * Sin;
            ly = dx * Sin + dy * Cos;
        }

        /// <summary>Offset from the box center in the box's frame (y up) → canvas point.</summary>
        public Vector2 ToCanvas(float lx, float ly) => Rotated
            ? new Vector2(Cx + lx * Cos + ly * Sin, Cy - lx * Sin + ly * Cos)
            : new Vector2(Cx + lx, Cy + ly);

        /// <summary>Axis-aligned bounds of a rect given in the box frame (xMin, yMin, xMax, yMax relative to the center, y up).</summary>
        public Rect BoundsOf(float x0, float y0, float x1, float y1)
        {
            if (!Rotated) return Rect.MinMaxRect(Cx + x0, Cy + y0, Cx + x1, Cy + y1);
            var a = ToCanvas(x0, y0);
            var b = ToCanvas(x1, y0);
            var c = ToCanvas(x1, y1);
            var d = ToCanvas(x0, y1);
            return Rect.MinMaxRect(
                Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)),
                Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)), Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
        }

        public Rect Bounds => BoundsOf(-Hw, -Hh, Hw, Hh);

        /// <summary>The four box corners on the canvas: top-left, top-right, bottom-right, bottom-left.</summary>
        public Vector2[] Corners() => new[]
        {
            ToCanvas(-Hw, Hh), ToCanvas(Hw, Hh), ToCanvas(Hw, -Hh), ToCanvas(-Hw, -Hh),
        };
    }

    /// <summary>A premultiplied RGBA float buffer covering <see cref="Rect"/> of the canvas (rows bottom-up).</summary>
    internal sealed class Surface
    {
        public readonly RectInt Rect;
        public readonly float[] Rgba;

        public Surface(RectInt rect)
        {
            Rect = rect;
            Rgba = new float[rect.width * rect.height * 4];
        }

        public int Count => Rect.width * Rect.height;
    }

    /// <summary>The effect settings of the frame or of a layer, viewed the same way.</summary>
    internal readonly struct Fx
    {
        public readonly StrokeSettings Stroke;
        public readonly List<ShadowSettings> Shadows;
        public readonly GlowSettings Glow;
        public readonly InnerEffectSettings InnerShadow;
        public readonly InnerEffectSettings InnerGlow;

        Fx(StrokeSettings stroke, List<ShadowSettings> shadows, GlowSettings glow, InnerEffectSettings innerShadow, InnerEffectSettings innerGlow)
        {
            Stroke = stroke;
            Shadows = shadows ?? new List<ShadowSettings>();
            Glow = glow;
            InnerShadow = innerShadow;
            InnerGlow = innerGlow;
        }

        public static Fx Of(UISpriteStyle s) => new Fx(s.stroke, s.dropShadows, s.outerGlow, s.innerShadow, s.innerGlow);

        public static Fx Of(LayerEffects e) => new Fx(e.stroke, e.dropShadows, e.outerGlow, e.innerShadow, e.innerGlow);

        public bool Any =>
            (Stroke.enabled && Stroke.width > 0f) || Glow.enabled || InnerShadow.enabled || InnerGlow.enabled ||
            Shadows.Exists(s => s != null && s.enabled);

        public float StrokeWidth(int scale) => Stroke.enabled ? Stroke.width * scale : 0f;

        public float StrokeOuter(int scale)
        {
            float w = StrokeWidth(scale);
            return Stroke.position switch
            {
                StrokePosition.Outside => w,
                StrokePosition.Center => w * 0.5f,
                _ => 0f,
            };
        }

        /// <summary>How far outer effects reach past the content, in pixels (left, bottom, right, top).</summary>
        public Vector4 Extents(int s)
        {
            float pl = 0f, pr = 0f, pb = 0f, pt = 0f;
            void Grow(float extent, float ox, float oyUp)
            {
                pl = Mathf.Max(pl, extent - ox);
                pr = Mathf.Max(pr, extent + ox);
                pb = Mathf.Max(pb, extent - oyUp);
                pt = Mathf.Max(pt, extent + oyUp);
            }

            Grow(StrokeOuter(s), 0f, 0f);
            foreach (var ds in Shadows)
            {
                if (ds == null || !ds.enabled) continue;
                Grow((ds.spread + ds.blur * SpriteRasterizer.BlurExtent) * s, ds.offset.x * s, -ds.offset.y * s);
            }
            if (Glow.enabled)
                Grow((Glow.spread + Glow.size * SpriteRasterizer.BlurExtent) * s, 0f, 0f);
            return new Vector4(pl, pb, pr, pt);
        }
    }

    internal static class RectUtil
    {
        public static Rect Expand(Rect r, Vector4 lbrt) =>
            Rect.MinMaxRect(r.xMin - lbrt.x, r.yMin - lbrt.y, r.xMax + lbrt.z, r.yMax + lbrt.w);

        public static Rect? Union(Rect? a, Rect? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            Rect p = a.Value, q = b.Value;
            return Rect.MinMaxRect(Mathf.Min(p.xMin, q.xMin), Mathf.Min(p.yMin, q.yMin), Mathf.Max(p.xMax, q.xMax), Mathf.Max(p.yMax, q.yMax));
        }

        /// <summary>Pixel rect covering <paramref name="r"/> plus one pixel, clamped to the canvas.</summary>
        public static RectInt ToPixels(Rect r, int canvasW, int canvasH)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(r.xMin) - 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(r.yMin) - 1);
            int x1 = Mathf.Min(canvasW, Mathf.CeilToInt(r.xMax) + 1);
            int y1 = Mathf.Min(canvasH, Mathf.CeilToInt(r.yMax) + 1);
            return new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }
    }
}
