using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UIImage = UnityEngine.UI.Image;

namespace UISpriteMaker.Editor.Tests
{
    public class ApiTests
    {
        const string Root = "Assets/__UISpriteMakerApiTests";
        const string Spec = @"{ ""size"": [120, 40], ""radius"": 12, ""fill"": ""#3366FF"",
                                ""shadows"": [ { ""offset"": [0, 4], ""blur"": 8 } ] }";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Root);

        [Test]
        public void Bake_CreatesFoldersAndStoresSpec()
        {
            string path = Root + "/Nested/Deeper/Button.png";
            var layout = UISpriteMakerApi.Bake(Spec, path);

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsNotNull(sprite);
            Assert.AreEqual(layout.Width, sprite.texture.width);
            Assert.AreEqual(layout.Border, sprite.border);

            string spec = UISpriteMakerApi.GetSpec(path);
            StringAssert.Contains("\"size\": [120, 40]", spec);
            StringAssert.Contains("#3366FF", spec);
        }

        [Test]
        public void Bake_AppendsPngExtensionAndRejectsPathsOutsideAssets()
        {
            UISpriteMakerApi.Bake(Spec, Root + "/NoExtension");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/NoExtension.png"));
            Assert.Throws<System.ArgumentException>(() => UISpriteMakerApi.Bake(Spec, "Packages/foo.png"));
        }

        [Test]
        public void ApplyToImage_SizesRectAndSlices()
        {
            string path = Root + "/Apply.png";
            var layout = UISpriteMakerApi.Bake(Spec, path);
            var go = new GameObject("img", typeof(RectTransform), typeof(UIImage));
            try
            {
                var image = go.GetComponent<UIImage>();
                UISpriteMakerApi.ApplyToImage(image, path);

                Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Sprite>(path), image.sprite);
                Assert.AreEqual(UIImage.Type.Sliced, image.type);
                Assert.AreEqual(new Vector2(layout.Width, layout.Height), image.rectTransform.rect.size);
                Assert.AreEqual(layout.Padding, image.raycastPadding);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RenderPng_WritesOutsideProject()
        {
            string file = Path.Combine(Path.GetTempPath(), "UISpriteMakerTest", "preview.png");
            if (File.Exists(file)) File.Delete(file);
            var layout = UISpriteMakerApi.RenderPng(Spec, file);
            Assert.IsTrue(File.Exists(file));

            var tex = new Texture2D(2, 2);
            try
            {
                tex.LoadImage(File.ReadAllBytes(file));
                Assert.AreEqual(layout.Width, tex.width);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void Cli_BakesValidSpecsAndReportsInvalidOnes()
        {
            string specFile = Path.Combine(Path.GetTempPath(), "UISpriteMakerTest", "batch.json");
            Directory.CreateDirectory(Path.GetDirectoryName(specFile));
            File.WriteAllText(specFile, @"{ ""sprites"": [
                { ""output"": """ + Root + @"/A.png"", ""size"": [64, 64], ""radius"": 32, ""fill"": ""#FF8800"" },
                { ""output"": """ + Root + @"/B.png"", ""size"": [64, 64], ""fill"": ""not-a-color"" },
                { ""size"": [10, 10] }
            ] }");

            LogAssert.Expect(LogType.Error, new Regex(@"sprites\[1\]\.fill: invalid color"));
            LogAssert.Expect(LogType.Error, new Regex(@"sprites\[2\]\.output: required"));
            bool ok = UISpriteMakerCli.Run(new[] { "-spec", specFile });

            Assert.IsFalse(ok);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/A.png"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/B.png"));
        }
    }
}
