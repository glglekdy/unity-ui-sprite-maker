using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UISpriteMaker.Editor.Tests
{
    public class ExporterTests
    {
        const string Folder = "Assets/__UISpriteMakerTests";
        const string AssetPath = Folder + "/TestSprite.png";

        UISpriteStyle _style;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "__UISpriteMakerTests");
            _style = ScriptableObject.CreateInstance<UISpriteStyle>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_style);
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Export_CreatesSlicedSpriteWithStyleData()
        {
            _style.shape.scale = 2;
            var result = SpriteExporter.Export(_style, AssetPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetPath);
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
            Assert.AreEqual(200f, importer.spritePixelsPerUnit);
            Assert.AreEqual(result.Border, importer.spriteBorder);
            Assert.AreNotEqual(Vector4.zero, importer.spriteBorder);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetPath);
            Assert.IsNotNull(sprite);
            Assert.AreEqual(result.Width, sprite.texture.width);
            Assert.AreEqual(result.Height, sprite.texture.height);

            Assert.IsTrue(SpriteExporter.HasStyle(AssetPath));
            var loaded = ScriptableObject.CreateInstance<UISpriteStyle>();
            try
            {
                Assert.IsTrue(SpriteExporter.TryLoadStyle(AssetPath, loaded));
                Assert.AreEqual(2, loaded.shape.scale);
                Assert.AreEqual(_style.shape.size, loaded.shape.size);
            }
            finally
            {
                Object.DestroyImmediate(loaded);
            }
        }
    }
}
