using System.Collections.Generic;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Structural edits on a style's layer tree (add, delete, duplicate, group, move, resize).
    /// Callers record Undo on the style before calling these.
    /// </summary>
    public static class LayerOps
    {
        /// <summary>Size of the box that a layer's position is relative to.</summary>
        public static Vector2 ParentSize(UISpriteStyle style, Layer parent) =>
            parent != null ? parent.size : (Vector2)style.shape.size;

        /// <summary>
        /// Adds a layer: into the selected layer when it can hold children, otherwise just above it.
        /// New layers are centered in their parent.
        /// </summary>
        public static void Add(UISpriteStyle style, Layer layer, string selectedId)
        {
            var selected = LayerTree.Find(style.layers, selectedId, out var owner, out var parent);
            List<Layer> list;
            int index;
            if (selected == null)
            {
                list = style.layers;
                index = list.Count;
                parent = null;
            }
            else if (selected.Children != null)
            {
                list = selected.Children;
                index = list.Count;
                parent = selected;
            }
            else
            {
                list = owner;
                index = owner.IndexOf(selected) + 1;
            }

            var box = ParentSize(style, parent);
            layer.position = new Vector2(Mathf.Round((box.x - layer.size.x) * 0.5f), Mathf.Round((box.y - layer.size.y) * 0.5f));
            list.Insert(index, layer);
        }

        public static bool Delete(UISpriteStyle style, string id)
        {
            var layer = LayerTree.Find(style.layers, id, out var owner, out _);
            return layer != null && owner.Remove(layer);
        }

        /// <summary>Copies a layer (and its children) just above it. Returns the copy.</summary>
        public static Layer Duplicate(UISpriteStyle style, string id)
        {
            var layer = LayerTree.Find(style.layers, id, out var owner, out _);
            if (layer == null) return null;
            var copy = LayerClipboard.Clone(layer);
            if (!string.IsNullOrEmpty(copy.name)) copy.name = NextCopyName(copy.name);
            owner.Insert(owner.IndexOf(layer) + 1, copy);
            return copy;
        }

        static string NextCopyName(string name) => name.EndsWith(" copy") ? name : name + " copy";

        /// <summary>Wraps a layer in a new group with the same box. Returns the group.</summary>
        public static GroupLayer Group(UISpriteStyle style, string id)
        {
            var layer = LayerTree.Find(style.layers, id, out var owner, out _);
            if (layer == null) return null;
            var group = new GroupLayer { name = "Group", position = layer.position, size = layer.size };
            group.horizontal = layer.horizontal;
            group.vertical = layer.vertical;
            owner[owner.IndexOf(layer)] = group;
            layer.position = Vector2.zero;
            layer.horizontal = HorizontalConstraint.Left;
            layer.vertical = VerticalConstraint.Top;
            group.children.Add(layer);
            return group;
        }

        /// <summary>Moves a layer one step up (towards the front) or down within its parent.</summary>
        public static bool Reorder(UISpriteStyle style, string id, int delta)
        {
            var layer = LayerTree.Find(style.layers, id, out var owner, out _);
            if (layer == null) return false;
            int from = owner.IndexOf(layer), to = Mathf.Clamp(from + delta, 0, owner.Count - 1);
            if (from == to) return false;
            owner.RemoveAt(from);
            owner.Insert(to, layer);
            return true;
        }

        /// <summary>
        /// Moves a layer into <paramref name="newParentId"/> (null = the frame) at <paramref name="index"/>
        /// (in drawing order: 0 = bottom). It keeps its place on the canvas.
        /// </summary>
        public static bool Move(UISpriteStyle style, string id, string newParentId, int index)
        {
            var layer = LayerTree.Find(style.layers, id, out var owner, out var oldParent);
            if (layer == null) return false;
            Layer newParent = null;
            if (newParentId != null)
            {
                newParent = LayerTree.Find(style.layers, newParentId, out _, out _);
                if (newParent?.Children == null || LayerTree.IsSelfOrDescendant(layer, newParent)) return false;
            }

            if (oldParent != newParent)
            {
                // Keep the layer where it is: convert its center and rotation into the new parent's frame.
                var placements = UnitPlacements(style);
                var p = placements[layer.id];
                var np = newParent != null ? placements[newParent.id] : RootPlacement(style);
                np.ToLocal(p.Cx, p.Cy, out float lx, out float ly);
                layer.position = new Vector2(lx + np.Hw - layer.size.x * 0.5f, np.Hh - ly - layer.size.y * 0.5f);
                layer.rotation = Mathf.DeltaAngle(0f, p.Angle - np.Angle);
            }

            var list = newParent != null ? newParent.Children : style.layers;
            int oldIndex = owner.IndexOf(layer);
            owner.RemoveAt(oldIndex);
            list.Insert(Mathf.Clamp(index, 0, list.Count), layer);
            return true;
        }

        /// <summary>Resizes a layer and lets its children follow their constraints.</summary>
        public static void Resize(Layer layer, Vector2 newSize)
        {
            var old = layer.size;
            layer.size = newSize;
            if (layer.Children != null) ApplyConstraints(layer.Children, old, newSize);
        }

        /// <summary>Resizes the frame and lets its layers follow their constraints.</summary>
        public static void ResizeFrame(UISpriteStyle style, Vector2Int newSize)
        {
            var old = (Vector2)style.shape.size;
            style.shape.size = newSize;
            ApplyConstraints(style.layers, old, newSize);
        }

        /// <summary>Moves/resizes children after their parent box changed from <paramref name="oldSize"/> to <paramref name="newSize"/>.</summary>
        public static void ApplyConstraints(List<Layer> children, Vector2 oldSize, Vector2 newSize)
        {
            if (oldSize == newSize) return;
            var d = newSize - oldSize;
            foreach (var child in children)
            {
                if (child == null) continue;
                var pos = child.position;
                var size = child.size;
                switch (child.horizontal)
                {
                    case HorizontalConstraint.Right: pos.x += d.x; break;
                    case HorizontalConstraint.Center: pos.x += d.x * 0.5f; break;
                    case HorizontalConstraint.Stretch: size.x = Mathf.Max(0f, size.x + d.x); break;
                    case HorizontalConstraint.Scale:
                        float kx = oldSize.x > 0f ? newSize.x / oldSize.x : 1f;
                        pos.x *= kx;
                        size.x *= kx;
                        break;
                }
                switch (child.vertical)
                {
                    case VerticalConstraint.Bottom: pos.y += d.y; break;
                    case VerticalConstraint.Center: pos.y += d.y * 0.5f; break;
                    case VerticalConstraint.Stretch: size.y = Mathf.Max(0f, size.y + d.y); break;
                    case VerticalConstraint.Scale:
                        float ky = oldSize.y > 0f ? newSize.y / oldSize.y : 1f;
                        pos.y *= ky;
                        size.y *= ky;
                        break;
                }
                child.position = pos;
                if (size != child.size) Resize(child, size);
            }
        }

        static Placement RootPlacement(UISpriteStyle style)
        {
            float hw = style.shape.size.x * 0.5f, hh = style.shape.size.y * 0.5f;
            return Placement.Axis(hw, hh, hw, hh);
        }

        /// <summary>Placements in frame units (scale 1, origin at the frame's bottom-left corner).</summary>
        static Dictionary<string, Placement> UnitPlacements(UISpriteStyle style)
        {
            var result = new Dictionary<string, Placement>();
            Collect(style.layers, RootPlacement(style), result);
            return result;

            static void Collect(List<Layer> layers, Placement parent, Dictionary<string, Placement> into)
            {
                foreach (var layer in layers)
                {
                    if (layer == null) continue;
                    var p = parent.Child(layer, 1f);
                    into[layer.id] = p;
                    if (layer.Children != null) Collect(layer.Children, p, into);
                }
            }
        }
    }
}
