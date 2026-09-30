using System.IO;
using UnityEditor;
using UnityEngine;

namespace UISpriteMaker.Editor
{
    public static class SpriteExporter
    {
        // Prefix for the style JSON stored in the importer's userData (PNG or prefab), so the asset can be re-edited.
        internal const string UserDataPrefix = "UISpriteMaker:";

        /// <summary>Renders <paramref name="style"/> to a PNG at <paramref name="assetPath"/> and imports it as a sprite.</summary>
        public static RasterResult Export(UISpriteStyle style, string assetPath)
        {
            var result = SpriteRasterizer.Render(style);
            var tex = result.ToTexture();
            try
            {
                File.WriteAllBytes(ToFullPath(assetPath), tex.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 100f * result.Scale;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteBorder = result.Border;
            importer.SetTextureSettings(settings);

            importer.userData = UserDataPrefix + style.ToJson();
            importer.SaveAndReimport();
            return result;
        }

        /// <summary>True for sprites and prefabs made by UI Sprite Maker.</summary>
        public static bool HasStyle(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) &&
            AssetImporter.GetAtPath(assetPath) is AssetImporter importer &&
            importer.userData != null && importer.userData.StartsWith(UserDataPrefix);

        /// <summary>Loads the style that was used to bake the sprite or prefab at <paramref name="assetPath"/> into <paramref name="target"/>.</summary>
        public static bool TryLoadStyle(string assetPath, UISpriteStyle target)
        {
            if (!HasStyle(assetPath)) return false;
            var importer = AssetImporter.GetAtPath(assetPath);
            target.LoadJson(importer.userData.Substring(UserDataPrefix.Length));
            return true;
        }

        static string ToFullPath(string assetPath) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath));
    }
}
