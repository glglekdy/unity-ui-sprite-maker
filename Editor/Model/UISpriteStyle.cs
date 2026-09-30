using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    public enum FillMode { Solid, LinearGradient, RadialGradient }

    public enum StrokePosition { Inside, Center, Outside }

    [Serializable]
    public class ShapeSettings
    {
        [Tooltip("Logical size of the shape in UI units (pixels at @1x).")]
        public Vector2Int size = new Vector2Int(240, 80);

        [Tooltip("Use one radius for all four corners.")]
        public bool linkCorners = true;
        [Min(0)] public float radius = 20f;
        [Min(0)] public float topLeft = 20f;
        [Min(0)] public float topRight = 20f;
        [Min(0)] public float bottomRight = 20f;
        [Min(0)] public float bottomLeft = 20f;

        [Tooltip("Output resolution multiplier (@1x, @2x, ...). Pixels Per Unit is scaled to match so the sprite keeps its logical size.")]
        [Range(1, 4)] public int scale = 1;

        /// <summary>Corner radii as (topLeft, topRight, bottomRight, bottomLeft).</summary>
        public Vector4 GetRadii() => linkCorners
            ? new Vector4(radius, radius, radius, radius)
            : new Vector4(topLeft, topRight, bottomRight, bottomLeft);
    }

    [Serializable]
    public class FillSettings
    {
        public FillMode mode = FillMode.Solid;
        public Color color = Color.white;
        public Gradient gradient = GradientUtil.TwoColor(Color.white, Color.gray);

        [Tooltip("Linear gradient direction in degrees. 0 = left to right, 90 = bottom to top.")]
        [Range(0, 360)] public float angle = 90f;

        [Tooltip("Radial gradient center, normalized to the shape (0.5, 0.5 = middle).")]
        public Vector2 radialCenter = new Vector2(0.5f, 0.5f);

        [Tooltip("Radial gradient radius. 1 = distance from the center to a corner of the shape.")]
        [Min(0.01f)] public float radialRadius = 1f;
    }

    [Serializable]
    public class StrokeSettings
    {
        public bool enabled;
        [Min(0)] public float width = 2f;
        public StrokePosition position = StrokePosition.Inside;
        public FillSettings fill = new FillSettings { color = Color.white };
    }

    [Serializable]
    public class ShadowSettings
    {
        public bool enabled = true;
        public Color color = new Color(0f, 0f, 0f, 0.35f);
        [Tooltip("Offset in UI units. Positive Y moves the shadow down (CSS/Figma convention).")]
        public Vector2 offset = new Vector2(0f, 4f);
        [Min(0)] public float blur = 12f;
        public float spread;
    }

    [Serializable]
    public class GlowSettings
    {
        public bool enabled;
        public Color color = new Color(0.35f, 0.75f, 1f, 1f);
        [Min(0)] public float size = 16f;
        public float spread;
        [Range(0, 4)] public float intensity = 1f;
    }

    [Serializable]
    public class InnerEffectSettings
    {
        public bool enabled;
        public Color color = new Color(0f, 0f, 0f, 0.35f);
        [Tooltip("Offset in UI units. Positive Y moves the light source down, so the shadow appears at the top edge.")]
        public Vector2 offset;
        [Min(0)] public float blur = 8f;
        [Tooltip("Grows the effect inwards from the edge.")]
        [Min(0)] public float choke;
    }

    [CreateAssetMenu(menuName = "UI Sprite Maker/UI Sprite Style", fileName = "UISpriteStyle")]
    public class UISpriteStyle : ScriptableObject
    {
        public ShapeSettings shape = new ShapeSettings();
        public FillSettings fill = new FillSettings
        {
            mode = FillMode.LinearGradient,
            angle = 90f,
            gradient = GradientUtil.TwoColor(new Color32(0x2F, 0x6B, 0xFF, 0xFF), new Color32(0x6F, 0xA8, 0xFF, 0xFF)),
        };
        public StrokeSettings stroke = new StrokeSettings();
        public List<ShadowSettings> dropShadows = new List<ShadowSettings> { new ShadowSettings() };
        public GlowSettings outerGlow = new GlowSettings();
        public InnerEffectSettings innerShadow = new InnerEffectSettings { offset = new Vector2(0f, 2f), blur = 4f };
        public InnerEffectSettings innerGlow = new InnerEffectSettings { color = new Color(1f, 1f, 1f, 0.5f), blur = 10f };

        [Tooltip("Set sprite borders so the sprite can be used with Image Type = Sliced.")]
        public bool nineSlice = true;

        [Tooltip("Clip layers to the frame's shape.")]
        public bool clip;

        /// <summary>Layers drawn on top of the frame's fill, bottom to top.</summary>
        [SerializeReference] public List<Layer> layers = new List<Layer>();

        public string ToJson() => EditorJsonUtility.ToJson(this);

        /// <summary>Overwrites this style from JSON while keeping its name and hide flags.</summary>
        public void LoadJson(string json)
        {
            var flags = hideFlags;
            var objName = name;
            // Styles saved before layers existed don't mention these fields: don't keep stale values.
            layers = new List<Layer>();
            clip = false;
            EditorJsonUtility.FromJsonOverwrite(json, this);
            layers ??= new List<Layer>();
            layers.RemoveAll(l => l == null);
            hideFlags = flags;
            name = objName;
        }

        public void CopyFrom(UISpriteStyle other) => LoadJson(other.ToJson());
    }

    public static class GradientUtil
    {
        public static Gradient TwoColor(Color from, Color to)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(from.a, 0f), new GradientAlphaKey(to.a, 1f) });
            return g;
        }
    }
}
