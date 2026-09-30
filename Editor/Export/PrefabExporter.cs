using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UIImage = UnityEngine.UI.Image;

namespace UISpriteMaker.Editor
{
    public sealed class PrefabBakeResult
    {
        public string PrefabPath;
        /// <summary>Number of GameObjects in the prefab, including the root.</summary>
        public int Objects;
        /// <summary>Sprites baked for the prefab.</summary>
        public List<string> Sprites = new List<string>();
        /// <summary>Layers that couldn't be exported the way they were asked to (and what happened instead).</summary>
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Builds a UGUI prefab from a layered style: the frame and layers that bake become 9-sliced sprites on
    /// Images, while text and image layers (and anything asked to) become their own TextMeshPro/Image objects.
    /// </summary>
    internal static class PrefabExporter
    {
        sealed class Run
        {
            public UISpriteStyle Style;
            public int Scale;
            public string SpriteDir;
            public string FontDir;
            public PrefabBakeResult Result;
            public readonly Dictionary<Layer, bool> IsObject = new Dictionary<Layer, bool>();
            public readonly Dictionary<Layer, string> Paths = new Dictionary<Layer, string>();
            public readonly Dictionary<Font, TMP_FontAsset> Fonts = new Dictionary<Font, TMP_FontAsset>();
            public readonly HashSet<string> SpriteNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            public bool? HasTmp;
        }

        /// <summary>Where a child is placed: the parent's RectTransform size and the inset of its box inside it (units).</summary>
        struct Frame
        {
            public Vector2 RectSize;
            /// <summary>Left, bottom, right, top.</summary>
            public Vector4 Inset;
        }

        public static PrefabBakeResult Export(UISpriteStyle style, string prefabPath)
        {
            string dir = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(prefabPath);
            var run = new Run
            {
                Style = style,
                Scale = Mathf.Clamp(style.shape.scale, 1, 4),
                SpriteDir = $"{dir}/{baseName}_Sprites",
                FontDir = $"{dir}/{baseName}_Sprites/Fonts",
                Result = new PrefabBakeResult { PrefabPath = prefabPath },
            };
            UISpriteMakerApi.EnsureFolder(run.SpriteDir);

            for (int i = 0; i < style.layers.Count; i++)
                if (style.layers[i] != null) Decide(run, style.layers[i], $"$.layers[{i}]", true);
            WarnAboutOrder(run, style.layers);

            var root = new GameObject(baseName, typeof(RectTransform));
            try
            {
                // Root: the frame plus every layer that bakes into it.
                var sub = SubStyle(run, style.shape.size, CornerOf(style.shape), style.fill, Fx.Of(style), style.clip, style.layers);
                var layout = BakeSprite(run, sub, baseName);
                var s = (float)run.Scale;
                var image = root.AddComponent<UIImage>();
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(run.Result.Sprites[0]);
                SetupSlicedImage(image, layout);

                var rt = (RectTransform)root.transform;
                var pad = layout.Padding / s;
                var rectSize = new Vector2(layout.Width / s, layout.Height / s);
                rt.sizeDelta = rectSize;
                rt.pivot = new Vector2((pad.x + style.shape.size.x * 0.5f) / rectSize.x, (pad.y + style.shape.size.y * 0.5f) / rectSize.y);
                if (style.clip && HasObjectChild(run, style.layers)) AddClip(root, pad);
                Object.DestroyImmediate(sub);

                var frame = new Frame { RectSize = rectSize, Inset = pad };
                BuildChildren(run, root.transform, style.layers, frame);

                run.Result.Objects = root.GetComponentsInChildren<RectTransform>(true).Length;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            var importer = AssetImporter.GetAtPath(prefabPath);
            if (importer != null)
            {
                importer.userData = SpriteExporter.UserDataPrefix + style.ToJson();
                importer.SaveAndReimport();
            }
            return run.Result;
        }

        // ------------------------------------------------------------------ bake or object?

        /// <returns>True when the layer becomes its own GameObject.</returns>
        static bool Decide(Run run, Layer layer, string path, bool parentIsObject)
        {
            run.Paths[layer] = path;
            string reason = parentIsObject ? BakeReason(run, layer) : "its parent is baked";

            bool isObject;
            if (layer.export == LayerExport.Bake || reason != null)
            {
                // Text and images are objects unless asked otherwise, so say why they aren't.
                bool expected = layer.export == LayerExport.Object ||
                                (layer.export == LayerExport.Auto && parentIsObject && (layer is TextLayer || layer is ImageLayer));
                if (reason != null && expected)
                    run.Result.Warnings.Add($"{path} ({layer.DisplayName}): {reason}; baked into the sprite instead");
                isObject = false;
            }
            else if (layer is TextLayer || layer is ImageLayer)
            {
                isObject = true;
            }
            else
            {
                // Shapes and groups become objects when asked to, or when something inside them does.
                bool anyChild = false;
                var children = layer.Children;
                for (int i = 0; i < children.Count; i++)
                    if (children[i] != null) anyChild |= Decide(run, children[i], $"{path}.layers[{i}]", true);
                isObject = layer.export == LayerExport.Object || anyChild;
                run.IsObject[layer] = isObject;
                if (isObject) WarnAboutOrder(run, children);
                return isObject;
            }

            run.IsObject[layer] = isObject;
            if (layer.Children != null)
                for (int i = 0; i < layer.Children.Count; i++)
                    if (layer.Children[i] != null) Decide(run, layer.Children[i], $"{path}.layers[{i}]", isObject);
            return isObject;
        }

        /// <summary>Why a layer can't be a UGUI object (null if it can).</summary>
        static string BakeReason(Run run, Layer layer)
        {
            if (layer.blendMode != LayerBlendMode.Normal) return $"blend mode \"{layer.blendMode}\" has no UGUI equivalent";
            bool effects = Fx.Of(layer.effects).Any;
            switch (layer)
            {
                case ImageLayer image:
                    if (image.source == null) return "it has no source";
                    if (!(image.source is Sprite)) return "its source is a Texture2D, not a Sprite";
                    if (image.fit == ImageFit.Cover) return "fit \"cover\" has no UGUI equivalent";
                    if (effects) return "effects on images need baking";
                    return null;
                case TextLayer text:
                    if (effects) return "effects on text need baking";
                    if (text.fill.mode != FillMode.Solid) return "gradient text needs baking";
                    return FontAssetFor(run, text.font) == null ? FontProblem(run, text.font) : null;
                case GroupLayer _:
                    return effects ? "effects on groups need baking" : null;
                default:
                    return null;
            }
        }

        static bool HasObjectChild(Run run, List<Layer> layers)
        {
            foreach (var l in layers)
                if (l != null && run.IsObject.TryGetValue(l, out bool o) && o) return true;
            return false;
        }

        /// <summary>Baked layers are drawn in their parent's sprite, below every object sibling.</summary>
        static void WarnAboutOrder(Run run, List<Layer> siblings)
        {
            Layer firstObject = null;
            foreach (var layer in siblings)
            {
                if (layer == null || !layer.visible) continue;
                bool obj = run.IsObject.TryGetValue(layer, out bool o) && o;
                if (obj && firstObject == null) firstObject = layer;
                else if (!obj && firstObject != null)
                    run.Result.Warnings.Add(
                        $"{run.Paths[layer]} ({layer.DisplayName}): baked, so it is drawn below \"{firstObject.DisplayName}\" even though it is above it");
            }
        }

        // ------------------------------------------------------------------ objects

        static void BuildChildren(Run run, Transform parent, List<Layer> layers, Frame frame)
        {
            foreach (var layer in layers)
                if (layer != null && run.IsObject.TryGetValue(layer, out bool o) && o)
                    BuildObject(run, parent, layer, frame);
        }

        static void BuildObject(Run run, Transform parent, Layer layer, Frame frame)
        {
            float s = run.Scale;
            var go = new GameObject(SafeName(layer.DisplayName), typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.SetActive(layer.visible);
            var pad = Vector4.zero;
            bool objectChildren = layer.Children != null && HasObjectChild(run, layer.Children);
            float opacity = Mathf.Clamp01(layer.opacity);

            switch (layer)
            {
                case ShapeLayer shape:
                {
                    var sub = SubStyle(run, Round(shape.size), shape.radius, shape.fill, Fx.Of(shape.effects), shape.clip, shape.children);
                    var layout = BakeSprite(run, sub, layer.DisplayName);
                    Object.DestroyImmediate(sub);
                    var image = go.AddComponent<UIImage>();
                    image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(run.Result.Sprites[^1]);
                    SetupSlicedImage(image, layout);
                    pad = layout.Padding / s;
                    if (!objectChildren) image.color = new Color(1f, 1f, 1f, opacity);
                    if (shape.clip && objectChildren) AddClip(go, pad);
                    break;
                }
                case GroupLayer group:
                {
                    if (HasBakedChild(run, group.children))
                    {
                        var clear = new FillSettings { mode = FillMode.Solid, color = new Color(0f, 0f, 0f, 0f) };
                        var sub = SubStyle(run, Round(group.size), new CornerRadius(), clear, Fx.Of(new LayerEffects()), group.clip, group.children);
                        var layout = BakeSprite(run, sub, layer.DisplayName);
                        Object.DestroyImmediate(sub);
                        var image = go.AddComponent<UIImage>();
                        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(run.Result.Sprites[^1]);
                        SetupSlicedImage(image, layout);
                        image.raycastTarget = false;
                        pad = layout.Padding / s;
                    }
                    if (group.clip) AddClip(go, pad);
                    break;
                }
                case ImageLayer imageLayer:
                {
                    var image = go.AddComponent<UIImage>();
                    image.sprite = (Sprite)imageLayer.source;
                    var tint = imageLayer.tint;
                    tint.a *= opacity;
                    image.color = tint;
                    image.type = imageLayer.fit == ImageFit.Sliced ? UIImage.Type.Sliced : UIImage.Type.Simple;
                    image.preserveAspect = imageLayer.fit == ImageFit.Contain;
                    break;
                }
                case TextLayer text:
                {
                    var tmp = go.AddComponent<TextMeshProUGUI>();
                    tmp.font = FontAssetFor(run, text.font);
                    tmp.text = text.text;
                    tmp.fontSize = text.fontSize;
                    var color = text.fill.color;
                    color.a *= opacity;
                    tmp.color = color;
                    tmp.alignment = Alignment(text.align, text.verticalAlign);
                    tmp.textWrappingMode = text.wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
                    tmp.overflowMode = TextOverflowModes.Overflow;
                    tmp.characterSpacing = text.letterSpacing / Mathf.Max(0.01f, text.fontSize) * 100f;
                    tmp.lineSpacing = (text.lineHeight - 1f) * TextEngine.LineHeightEm(text.font) * 100f;
                    tmp.raycastTarget = false;
                    break;
                }
            }

            // Opacity of a layer with children applies to the whole subtree.
            if (opacity < 1f && (objectChildren || layer is GroupLayer)) go.AddComponent<CanvasGroup>().alpha = opacity;

            var rt = (RectTransform)go.transform;
            var rectSize = Place(rt, layer, frame, pad);
            if (layer.Children != null)
                BuildChildren(run, go.transform, layer.Children, new Frame { RectSize = rectSize, Inset = pad });
        }

        static bool HasBakedChild(Run run, List<Layer> layers)
        {
            foreach (var l in layers)
                if (l != null && l.visible && (!run.IsObject.TryGetValue(l, out bool o) || !o)) return true;
            return false;
        }

        /// <summary>Anchors and sizes a child's RectTransform from its box, constraints and sprite padding. Returns its rect size.</summary>
        static Vector2 Place(RectTransform rt, Layer layer, Frame frame, Vector4 pad)
        {
            float pw = frame.RectSize.x, ph = frame.RectSize.y;
            float w = layer.size.x, h = layer.size.y;
            // Box edges in the parent rect, x from its left edge and y from its bottom edge.
            float l = frame.Inset.x + layer.position.x, r = l + w;
            float t = ph - frame.Inset.w - layer.position.y, b = t - h;
            // The object's rect also holds its sprite padding (shadows, glows).
            float l2 = l - pad.x, r2 = r + pad.z, b2 = b - pad.y, t2 = t + pad.w;

            (float min, float max) Anchor(float lo, float hi, float size, int mode) => mode switch
            {
                0 => (0f, 0f),       // left / bottom
                1 => (1f, 1f),       // right / top
                2 => (0.5f, 0.5f),   // center
                3 => (0f, 1f),       // stretch
                _ => (size > 0f ? lo / size : 0f, size > 0f ? hi / size : 0f), // scale
            };
            var (ax0, ax1) = Anchor(l2, r2, pw, layer.horizontal switch
            {
                HorizontalConstraint.Right => 1, HorizontalConstraint.Center => 2, HorizontalConstraint.Stretch => 3,
                HorizontalConstraint.Scale => 4, _ => 0,
            });
            var (ay0, ay1) = Anchor(b2, t2, ph, layer.vertical switch
            {
                VerticalConstraint.Top => 1, VerticalConstraint.Center => 2, VerticalConstraint.Stretch => 3,
                VerticalConstraint.Scale => 4, _ => 0,
            });

            rt.anchorMin = new Vector2(ax0, ay0);
            rt.anchorMax = new Vector2(ax1, ay1);
            float rw = r2 - l2, rh = t2 - b2;
            // Rotate around the box center, which isn't the rect center when the padding is uneven.
            rt.pivot = new Vector2(rw > 0f ? (l + w * 0.5f - l2) / rw : 0.5f, rh > 0f ? (b + h * 0.5f - b2) / rh : 0.5f);
            rt.offsetMin = new Vector2(l2 - ax0 * pw, b2 - ay0 * ph);
            rt.offsetMax = new Vector2(r2 - ax1 * pw, t2 - ay1 * ph);
            rt.localRotation = Quaternion.Euler(0f, 0f, -layer.rotation);
            rt.localScale = Vector3.one;
            return new Vector2(rw, rh);
        }

        static void AddClip(GameObject go, Vector4 pad)
        {
            var mask = go.AddComponent<RectMask2D>();
            mask.padding = pad; // clip to the shape, not to its shadow room
        }

        static void SetupSlicedImage(UIImage image, SpriteLayout layout)
        {
            image.type = layout.Border != Vector4.zero ? UIImage.Type.Sliced : UIImage.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
            image.raycastPadding = layout.Padding / layout.Scale;
        }

        static TextAlignmentOptions Alignment(TextAlign h, TextVerticalAlign v) => (h, v) switch
        {
            (TextAlign.Left, TextVerticalAlign.Top) => TextAlignmentOptions.TopLeft,
            (TextAlign.Center, TextVerticalAlign.Top) => TextAlignmentOptions.Top,
            (TextAlign.Right, TextVerticalAlign.Top) => TextAlignmentOptions.TopRight,
            (TextAlign.Left, TextVerticalAlign.Middle) => TextAlignmentOptions.Left,
            (TextAlign.Center, TextVerticalAlign.Middle) => TextAlignmentOptions.Center,
            (TextAlign.Right, TextVerticalAlign.Middle) => TextAlignmentOptions.Right,
            (TextAlign.Left, _) => TextAlignmentOptions.BottomLeft,
            (TextAlign.Center, _) => TextAlignmentOptions.Bottom,
            _ => TextAlignmentOptions.BottomRight,
        };

        // ------------------------------------------------------------------ sprites

        /// <summary>A temporary style with the given shape as its frame and only the children that bake.</summary>
        static UISpriteStyle SubStyle(Run run, Vector2Int size, CornerRadius radius, FillSettings fill, Fx fx, bool clip, List<Layer> children)
        {
            var sub = ScriptableObject.CreateInstance<UISpriteStyle>();
            sub.hideFlags = HideFlags.DontSave;
            sub.shape.size = new Vector2Int(Mathf.Clamp(size.x, 1, SpriteRasterizer.MaxSize), Mathf.Clamp(size.y, 1, SpriteRasterizer.MaxSize));
            sub.shape.scale = run.Scale;
            sub.shape.linkCorners = radius.linkCorners;
            sub.shape.radius = radius.radius;
            sub.shape.topLeft = radius.topLeft;
            sub.shape.topRight = radius.topRight;
            sub.shape.bottomRight = radius.bottomRight;
            sub.shape.bottomLeft = radius.bottomLeft;
            sub.nineSlice = run.Style.nineSlice;
            sub.fill = fill;
            sub.stroke = fx.Stroke;
            sub.dropShadows = new List<ShadowSettings>(fx.Shadows);
            sub.outerGlow = fx.Glow;
            sub.innerShadow = fx.InnerShadow;
            sub.innerGlow = fx.InnerGlow;
            sub.clip = clip;
            foreach (var child in children)
                if (child != null && (!run.IsObject.TryGetValue(child, out bool o) || !o))
                    sub.layers.Add(child);
            return sub;
        }

        static SpriteLayout BakeSprite(Run run, UISpriteStyle sub, string name)
        {
            string file = SafeName(name);
            string unique = file;
            for (int i = 2; !run.SpriteNames.Add(unique); i++) unique = $"{file}_{i}";
            string path = $"{run.SpriteDir}/{unique}.png";
            var raster = SpriteExporter.Export(sub, path);
            run.Result.Sprites.Add(path);
            run.Result.Warnings.AddRange(raster.Warnings);
            return SpriteRasterizer.ComputeLayout(sub);
        }

        static CornerRadius CornerOf(ShapeSettings shape) => new CornerRadius
        {
            linkCorners = shape.linkCorners, radius = shape.radius,
            topLeft = shape.topLeft, topRight = shape.topRight, bottomRight = shape.bottomRight, bottomLeft = shape.bottomLeft,
        };

        static Vector2Int Round(Vector2 size) => new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(size.x)), Mathf.Max(1, Mathf.RoundToInt(size.y)));

        static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "")
                sb.Append(System.Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == '#' ? '_' : c);
            var s = sb.ToString().Trim().Trim('.');
            return s.Length == 0 ? "Layer" : s.Length > 48 ? s.Substring(0, 48) : s;
        }

        // ------------------------------------------------------------------ fonts

        static bool TmpReady(Run run) =>
            run.HasTmp ??= Resources.Load<TMP_Settings>("TMP Settings") != null;

        static string FontProblem(Run run, Font font) => !TmpReady(run)
            ? "TextMeshPro isn't set up in this project (Window › TextMeshPro › Import TMP Essential Resources)"
            : font == null
                ? "there's no default TextMeshPro font asset"
                : $"couldn't create a TextMeshPro font asset for \"{font.name}\"";

        /// <summary>A TMP font asset for a font: an existing one made from it, or a new dynamic one next to the prefab.</summary>
        static TMP_FontAsset FontAssetFor(Run run, Font font)
        {
            if (!TmpReady(run)) return null;
            if (font == null) return TMP_Settings.defaultFontAsset;
            if (run.Fonts.TryGetValue(font, out var cached)) return cached;

            TMP_FontAsset asset = null;
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.sourceFontFile == font && candidate.atlasPopulationMode != AtlasPopulationMode.Static)
                {
                    asset = candidate;
                    break;
                }
            }

            if (asset == null)
            {
                try
                {
                    asset = TMP_FontAsset.CreateFontAsset(font);
                }
                catch (System.Exception)
                {
                    asset = null;
                }
                if (asset != null)
                {
                    UISpriteMakerApi.EnsureFolder(run.FontDir);
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{run.FontDir}/{SafeName(font.name)} SDF.asset");
                    asset.name = Path.GetFileNameWithoutExtension(path);
                    AssetDatabase.CreateAsset(asset, path);
                    if (asset.atlasTexture != null)
                    {
                        asset.atlasTexture.name = asset.name + " Atlas";
                        AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
                    }
                    if (asset.material != null)
                    {
                        asset.material.name = asset.name + " Material";
                        AssetDatabase.AddObjectToAsset(asset.material, asset);
                    }
                    AssetDatabase.SaveAssets();
                }
            }
            run.Fonts[font] = asset;
            return asset;
        }
    }
}
