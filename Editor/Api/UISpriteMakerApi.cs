using System.IO;
using UnityEditor;
using UnityEngine;
using UIImage = UnityEngine.UI.Image;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Scripting entry points for baking UI sprites from code (editor scripts, Unity MCP, batch mode).
    /// Every method taking <c>specJson</c> accepts the JSON sprite spec documented in AGENTS.md.
    /// </summary>
    public static class UISpriteMakerApi
    {
        /// <summary>
        /// Bakes a sprite from a JSON spec into a PNG inside the project and configures it as a (9-sliced) sprite.
        /// Missing folders are created. Existing files are overwritten.
        /// </summary>
        /// <param name="assetPath">Project-relative path, e.g. "Assets/UI/Sprites/PrimaryButton.png".</param>
        public static SpriteLayout Bake(string specJson, string assetPath)
        {
            var style = SpriteSpec.Parse(specJson);
            try
            {
                return Bake(style, assetPath);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        public static SpriteLayout Bake(UISpriteStyle style, string assetPath) => Bake(style, assetPath, out _);

        /// <param name="raster">The rendered pixels, including warnings about layers that couldn't be drawn.</param>
        internal static SpriteLayout Bake(UISpriteStyle style, string assetPath, out RasterResult raster)
        {
            assetPath = NormalizeAssetPath(assetPath, ".png");
            EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            raster = SpriteExporter.Export(style, assetPath);
            return SpriteRasterizer.ComputeLayout(style);
        }

        /// <summary>
        /// Builds a UGUI prefab from a layered spec: the frame (and layers that bake) become 9-sliced sprite Images,
        /// text layers become TextMeshPro objects and image layers become Images, anchored by their constraints.
        /// Sprites are written next to the prefab in "&lt;Name&gt;_Sprites/". Existing files are overwritten.
        /// </summary>
        /// <param name="prefabPath">Project-relative path, e.g. "Assets/UI/Prefabs/RewardCard.prefab".</param>
        public static PrefabBakeResult BakePrefab(string specJson, string prefabPath)
        {
            var style = SpriteSpec.Parse(specJson);
            try
            {
                return BakePrefab(style, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        public static PrefabBakeResult BakePrefab(UISpriteStyle style, string prefabPath)
        {
            prefabPath = NormalizeAssetPath(prefabPath, ".prefab");
            EnsureFolder(Path.GetDirectoryName(prefabPath)?.Replace('\\', '/'));
            return PrefabExporter.Export(style, prefabPath);
        }

        /// <summary>
        /// Renders a spec to a PNG anywhere on disk without importing it. Useful for checking the look
        /// (e.g. an AI viewing the image) before baking it into the project.
        /// </summary>
        public static SpriteLayout RenderPng(string specJson, string filePath)
        {
            var style = SpriteSpec.Parse(specJson);
            try
            {
                var result = SpriteRasterizer.Render(style);
                var tex = result.ToTexture();
                try
                {
                    var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(filePath, tex.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(tex);
                }
                return SpriteRasterizer.ComputeLayout(style);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        /// <summary>Validates a spec. Returns null when valid, otherwise the error message.</summary>
        public static string Validate(string specJson)
        {
            try
            {
                Object.DestroyImmediate(SpriteSpec.Parse(specJson));
                return null;
            }
            catch (SpriteSpecException e)
            {
                return e.Message;
            }
        }

        /// <summary>Returns the spec JSON a sprite or prefab was baked with, or null if it was not made by this tool.</summary>
        public static string GetSpec(string assetPath)
        {
            var style = ScriptableObject.CreateInstance<UISpriteStyle>();
            try
            {
                return SpriteExporter.TryLoadStyle(assetPath, style) ? SpriteSpec.ToJson(style) : null;
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        /// <summary>
        /// Assigns a sprite baked by this tool to a UGUI Image: sets the sprite, Sliced/Simple type,
        /// sizes the RectTransform so the shape appears at its designed size, and excludes the
        /// shadow/glow padding from raycasts. Undo-able.
        /// </summary>
        public static void ApplyToImage(UIImage image, string assetPath)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
                throw new System.ArgumentException($"No sprite at \"{assetPath}\"", nameof(assetPath));

            var style = ScriptableObject.CreateInstance<UISpriteStyle>();
            try
            {
                if (!SpriteExporter.TryLoadStyle(assetPath, style))
                    throw new System.ArgumentException($"\"{assetPath}\" was not made by UI Sprite Maker", nameof(assetPath));
                ApplyToImage(image, sprite, SpriteRasterizer.ComputeLayout(style));
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        internal static void ApplyToImage(UIImage image, Sprite sprite, SpriteLayout layout)
        {
            float s = layout.Scale;
            var rt = image.rectTransform;
            Undo.RecordObjects(new Object[] { image, rt }, "Apply UI Sprite");
            image.sprite = sprite;
            image.type = layout.Border != Vector4.zero ? UIImage.Type.Sliced : UIImage.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f;
            // The sprite includes transparent padding for shadows/glows: size the rect so the
            // shape keeps its designed size and keep clicks on the shape only.
            image.raycastPadding = layout.Padding / s;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, layout.Width / s);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, layout.Height / s);
            PrefabUtility.RecordPrefabInstancePropertyModifications(image);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rt);
        }

        static string NormalizeAssetPath(string assetPath, string extension)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new System.ArgumentException("Asset path is empty", nameof(assetPath));
            assetPath = assetPath.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/"))
                throw new System.ArgumentException($"Asset path must start with \"Assets/\": \"{assetPath}\"", nameof(assetPath));
            if (!assetPath.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase))
                assetPath += extension;
            return assetPath;
        }

        internal static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }
    }
}
