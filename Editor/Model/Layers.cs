using System;
using System.Collections.Generic;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    public enum LayerBlendMode { Normal, Multiply, Screen, Overlay, Darken, Lighten, Add }

    public enum HorizontalConstraint { Left, Right, Center, Stretch, Scale }

    public enum VerticalConstraint { Top, Bottom, Center, Stretch, Scale }

    /// <summary>How a layer is exported to a prefab. Flattened PNGs always bake every layer.</summary>
    public enum LayerExport { Auto, Bake, Object }

    public enum ImageFit { Stretch, Contain, Cover, Sliced }

    public enum TextAlign { Left, Center, Right }

    public enum TextVerticalAlign { Top, Middle, Bottom }

    [Serializable]
    public class CornerRadius
    {
        [Tooltip("Use one radius for all four corners.")]
        public bool linkCorners = true;
        [Min(0)] public float radius;
        [Min(0)] public float topLeft;
        [Min(0)] public float topRight;
        [Min(0)] public float bottomRight;
        [Min(0)] public float bottomLeft;

        /// <summary>Corner radii as (topLeft, topRight, bottomRight, bottomLeft).</summary>
        public Vector4 GetRadii() => linkCorners
            ? new Vector4(radius, radius, radius, radius)
            : new Vector4(topLeft, topRight, bottomRight, bottomLeft);
    }

    /// <summary>Effects a layer can have. Everything is off by default.</summary>
    [Serializable]
    public class LayerEffects
    {
        public StrokeSettings stroke = new StrokeSettings();
        public List<ShadowSettings> dropShadows = new List<ShadowSettings>();
        public GlowSettings outerGlow = new GlowSettings();
        public InnerEffectSettings innerShadow = new InnerEffectSettings { offset = new Vector2(0f, 2f), blur = 4f };
        public InnerEffectSettings innerGlow = new InnerEffectSettings { color = new Color(1f, 1f, 1f, 0.5f), blur = 10f };

        public bool Any =>
            stroke.enabled || outerGlow.enabled || innerShadow.enabled || innerGlow.enabled ||
            dropShadows.Exists(s => s != null && s.enabled);
    }

    /// <summary>
    /// A layer inside a sprite. Coordinates are UI units relative to the parent's box:
    /// position is the top-left corner of this layer's box, y grows downwards (like Figma/CSS).
    /// Rotation is in degrees, clockwise, around the box center.
    /// </summary>
    [Serializable]
    public abstract class Layer
    {
        [HideInInspector] public string id = NewId();
        public string name = "";
        public bool visible = true;
        [Tooltip("Locked layers can't be selected or moved on the canvas.")]
        public bool locked;

        [Tooltip("Top-left corner of the box, relative to the parent's top-left corner (y down), in UI units.")]
        public Vector2 position;
        public Vector2 size = new Vector2(100f, 100f);
        [Tooltip("Degrees, clockwise, around the center of the layer.")]
        public float rotation;
        [Range(0, 1)] public float opacity = 1f;
        public LayerBlendMode blendMode = LayerBlendMode.Normal;

        [Tooltip("How the layer follows its parent when the parent is resized (and how it is anchored in an exported prefab).")]
        public HorizontalConstraint horizontal = HorizontalConstraint.Left;
        public VerticalConstraint vertical = VerticalConstraint.Top;

        [Tooltip("Prefab export: Bake draws the layer into its parent's sprite, Object makes it a separate Image/Text object.")]
        public LayerExport export = LayerExport.Auto;

        public LayerEffects effects = new LayerEffects();

        /// <summary>Spec "type" name: shape, image, text or group.</summary>
        public abstract string TypeName { get; }

        /// <summary>Child layers (bottom to top), or null when this layer type can't have children.</summary>
        public virtual List<Layer> Children => null;

        /// <summary>True when children are clipped to this layer's shape/box.</summary>
        public virtual bool Clips => false;

        public string DisplayName => string.IsNullOrEmpty(name) ? DefaultName : name;

        protected virtual string DefaultName => char.ToUpperInvariant(TypeName[0]) + TypeName.Substring(1);

        internal static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>Gives this layer and all of its descendants new ids (after duplicating).</summary>
        public void RegenerateIds()
        {
            id = NewId();
            if (Children != null)
                foreach (var child in Children)
                    child?.RegenerateIds();
        }
    }

    [Serializable]
    public class ShapeLayer : Layer
    {
        public CornerRadius radius = new CornerRadius();
        public FillSettings fill = new FillSettings { color = Color.white };
        [Tooltip("Clip children to this shape.")]
        public bool clip;
        [SerializeReference] public List<Layer> children = new List<Layer>();

        public override string TypeName => "shape";
        public override List<Layer> Children => children;
        public override bool Clips => clip;
    }

    [Serializable]
    public class ImageLayer : Layer
    {
        [Tooltip("A Sprite or a Texture2D.")]
        public UnityEngine.Object source;
        public Color tint = Color.white;
        public ImageFit fit = ImageFit.Stretch;

        public override string TypeName => "image";
    }

    [Serializable]
    public class TextLayer : Layer
    {
        [TextArea(1, 6)] public string text = "Text";
        [Tooltip("Font file (.ttf/.otf). Empty = default font (Latin only).")]
        public Font font;
        [Min(1)] public float fontSize = 24f;
        public FillSettings fill = new FillSettings { color = Color.black };
        public TextAlign align = TextAlign.Left;
        public TextVerticalAlign verticalAlign = TextVerticalAlign.Top;
        [Tooltip("Multiplier of the font's line height.")]
        [Min(0.1f)] public float lineHeight = 1f;
        [Tooltip("Extra space between characters, in UI units.")]
        public float letterSpacing;
        [Tooltip("Wrap lines at the width of the box.")]
        public bool wrap = true;

        public override string TypeName => "text";

        protected override string DefaultName
        {
            get
            {
                var t = (text ?? "").Replace('\n', ' ').Trim();
                return t.Length == 0 ? "Text" : t.Length > 24 ? t.Substring(0, 24) + "…" : t;
            }
        }
    }

    [Serializable]
    public class GroupLayer : Layer
    {
        [Tooltip("Clip children to the group's box.")]
        public bool clip;
        [SerializeReference] public List<Layer> children = new List<Layer>();

        public override string TypeName => "group";
        public override List<Layer> Children => children;
        public override bool Clips => clip;
    }

    public static class LayerTree
    {
        /// <summary>Depth-first walk over a layer list (parents before children).</summary>
        public static IEnumerable<Layer> Walk(IEnumerable<Layer> layers)
        {
            if (layers == null) yield break;
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                yield return layer;
                foreach (var child in Walk(layer.Children))
                    yield return child;
            }
        }

        /// <summary>Finds a layer by id and returns the list that contains it.</summary>
        public static Layer Find(List<Layer> root, string id, out List<Layer> owner, out Layer parent)
        {
            owner = null;
            parent = null;
            if (string.IsNullOrEmpty(id) || root == null) return null;
            return FindIn(root, null, id, ref owner, ref parent);
        }

        static Layer FindIn(List<Layer> list, Layer listParent, string id, ref List<Layer> owner, ref Layer parent)
        {
            foreach (var layer in list)
            {
                if (layer == null) continue;
                if (layer.id == id)
                {
                    owner = list;
                    parent = listParent;
                    return layer;
                }
                if (layer.Children != null)
                {
                    var found = FindIn(layer.Children, layer, id, ref owner, ref parent);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>True when <paramref name="candidate"/> is <paramref name="layer"/> or one of its descendants.</summary>
        public static bool IsSelfOrDescendant(Layer layer, Layer candidate)
        {
            if (layer == candidate) return true;
            if (layer?.Children == null) return false;
            foreach (var child in layer.Children)
                if (IsSelfOrDescendant(child, candidate)) return true;
            return false;
        }
    }
}
