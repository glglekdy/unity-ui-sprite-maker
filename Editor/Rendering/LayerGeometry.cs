using System.Collections.Generic;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>Where layers end up on the canvas and how much room they (and their effects) need.</summary>
    internal static class LayerGeometry
    {
        /// <summary>Canvas bounds of everything a layer draws, including its children and outer effects. Null if nothing.</summary>
        public static Rect? Full(Layer layer, Placement p, int s)
        {
            if (layer == null || !layer.visible) return null;
            var content = Content(layer, p, s);
            return content == null ? null : RectUtil.Expand(content.Value, Fx.Of(layer.effects).Extents(s));
        }

        /// <summary>Canvas bounds of a layer's own pixels plus its children, before outer effects.</summary>
        public static Rect? Content(Layer layer, Placement p, int s)
        {
            Rect? own = layer switch
            {
                TextLayer t => TextInk(t, p, s),
                GroupLayer _ => null,
                ImageLayer im => im.source != null ? p.Bounds : (Rect?)null,
                _ => p.Bounds,
            };

            var children = layer.Children;
            if (children == null || children.Count == 0) return own;

            Rect? kids = null;
            foreach (var child in children)
                if (child != null) kids = RectUtil.Union(kids, Full(child, p.Child(child, s), s));
            if (kids != null && layer.Clips)
                kids = Intersect(kids.Value, p.Bounds);
            return RectUtil.Union(own, kids);
        }

        /// <summary>Canvas bounds of a text layer's glyphs (text may overflow its box).</summary>
        public static Rect? TextInk(TextLayer t, Placement p, int s)
        {
            if (string.IsNullOrEmpty(t.text)) return null;
            TextLayoutResult layout;
            try
            {
                layout = TextEngine.Layout(t, s);
            }
            catch (System.InvalidOperationException)
            {
                return null;
            }
            var ink = layout.Ink;
            if (ink.width <= 0f || ink.height <= 0f) return null;
            // Ink is y-down from the box's top-left corner; placements are y-up from the box center.
            return p.BoundsOf(ink.xMin - p.Hw, p.Hh - ink.yMax, ink.xMax - p.Hw, p.Hh - ink.yMin);
        }

        static Rect? Intersect(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin), y0 = Mathf.Max(a.yMin, b.yMin);
            float x1 = Mathf.Min(a.xMax, b.xMax), y1 = Mathf.Min(a.yMax, b.yMax);
            return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : (Rect?)null;
        }

        /// <summary>Placement of every layer (visible or not), keyed by layer id.</summary>
        public static Dictionary<string, Placement> Placements(UISpriteStyle style, RectInt shapeRect, int scale)
        {
            var result = new Dictionary<string, Placement>();
            Collect(style.layers, Placement.Root(shapeRect), scale, result);
            return result;
        }

        static void Collect(List<Layer> layers, Placement parent, int s, Dictionary<string, Placement> into)
        {
            if (layers == null) return;
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                var p = parent.Child(layer, s);
                into[layer.id] = p;
                Collect(layer.Children, p, s, into);
            }
        }
    }
}
