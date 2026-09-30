using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UIImage = UnityEngine.UI.Image;

namespace UISpriteMaker.Editor.Tests
{
    public class PrefabTests
    {
        const string Root = "Assets/__UISpriteMakerPrefabTests";

        string _icon;

        [SetUp]
        public void SetUp()
        {
            UISpriteMakerApi.EnsureFolder(Root);
            _icon = $"{Root}/Icon.png";
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            tex.SetPixels(Enumerable.Repeat(Color.yellow, 64).ToArray());
            File.WriteAllBytes(_icon, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(_icon, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(_icon);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Root);

        string Spec => @"{ ""size"": [200, 80], ""radius"": 12, ""fill"": ""#223"",
            ""shadows"": [ { ""offset"": [0, 4], ""blur"": 8 } ],
            ""layers"": [
                { ""type"": ""shape"", ""name"": ""Deco"", ""position"": [0, 70], ""size"": [200, 10], ""fill"": ""#FFFFFF22"" },
                { ""type"": ""image"", ""name"": ""Icon"", ""source"": """ + _icon + @""", ""position"": [10, 20], ""size"": [40, 40], ""opacity"": 0.5 },
                { ""type"": ""text"", ""name"": ""Title"", ""text"": ""Reward"", ""fontSize"": 20, ""color"": ""#FFFFFF"",
                  ""position"": [60, 20], ""size"": [100, 30], ""constraints"": { ""horizontal"": ""stretch"", ""vertical"": ""center"" } },
                { ""type"": ""shape"", ""name"": ""Badge"", ""position"": [150, 6], ""size"": [40, 20], ""radius"": 10, ""fill"": ""#F44"",
                  ""constraints"": { ""horizontal"": ""right"", ""vertical"": ""top"" }, ""rotation"": 10,
                  ""shadows"": [ { ""offset"": [0, 2], ""blur"": 3 } ],
                  ""layers"": [ { ""type"": ""image"", ""source"": """ + _icon + @""", ""position"": [2, 2], ""size"": [16, 16] } ] },
                { ""type"": ""group"", ""name"": ""Faded"", ""position"": [0, 0], ""size"": [50, 20], ""opacity"": 0.4, ""clip"": true,
                  ""layers"": [ { ""type"": ""image"", ""source"": """ + _icon + @""", ""size"": [10, 10] } ] },
                { ""type"": ""image"", ""name"": ""Screen"", ""source"": """ + _icon + @""", ""position"": [170, 50], ""size"": [20, 20],
                  ""blendMode"": ""screen"", ""export"": ""object"" }
            ] }";

        static Transform Child(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        [Test]
        public void BakePrefab_BuildsObjectsForLayers()
        {
            string path = Root + "/Card.prefab";
            var result = UISpriteMakerApi.BakePrefab(Spec, path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab);

            var rootImage = prefab.GetComponent<UIImage>();
            Assert.IsNotNull(rootImage.sprite, "the frame is a baked sprite");
            Assert.AreEqual(UIImage.Type.Sliced, rootImage.type);
            Assert.IsNull(Child(prefab, "Deco"), "plain shapes bake into their parent's sprite");

            var icon = Child(prefab, "Icon").GetComponent<UIImage>();
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Sprite>(_icon), icon.sprite);
            Assert.AreEqual(0.5f, icon.color.a, 1e-4f);

            var badge = (RectTransform)Child(prefab, "Badge");
            Assert.IsNotNull(badge, "a shape with an object child becomes an object");
            Assert.IsNotNull(badge.GetComponent<UIImage>().sprite);
            Assert.AreEqual(new Vector2(1, 1), badge.anchorMin);
            Assert.AreEqual(new Vector2(1, 1), badge.anchorMax);
            Assert.AreEqual(-10f, Mathf.DeltaAngle(0f, badge.localEulerAngles.z), 1e-3f);
            Assert.AreEqual(1, badge.childCount);

            var faded = Child(prefab, "Faded");
            Assert.AreEqual(0.4f, faded.GetComponent<CanvasGroup>().alpha, 1e-4f);
            Assert.IsNotNull(faded.GetComponent<RectMask2D>());

            Assert.IsNull(Child(prefab, "Screen"), "blend modes can't be objects");
            Assert.That(result.Warnings.Any(w => w.Contains("Screen") && w.Contains("blend mode")));

            var title = Child(prefab, "Title");
            if (Resources.Load<TMP_Settings>("TMP Settings") != null)
            {
                var tmp = title.GetComponent<TextMeshProUGUI>();
                Assert.AreEqual("Reward", tmp.text);
                Assert.AreEqual(20f, tmp.fontSize);
                var rt = (RectTransform)title;
                Assert.AreEqual(0f, rt.anchorMin.x);
                Assert.AreEqual(1f, rt.anchorMax.x);
                Assert.AreEqual(0.5f, rt.anchorMin.y);
            }
            else
            {
                Assert.IsNull(title, "without TextMeshPro set up, text is baked");
                Assert.That(result.Warnings.Any(w => w.Contains("Title") && w.Contains("TextMeshPro")));
            }

            Assert.AreEqual(prefab.GetComponentsInChildren<RectTransform>(true).Length, result.Objects);
            Assert.That(result.Sprites.All(s => s.StartsWith(Root + "/Card_Sprites/")));
        }

        [Test]
        public void ObjectsSitWhereTheLayerBoxesAre()
        {
            string path = Root + "/Place.prefab";
            UISpriteMakerApi.BakePrefab(@"{ ""size"": [200, 100], ""shadows"": [ { ""offset"": [0, 10], ""blur"": 6 } ],
                ""layers"": [ { ""type"": ""image"", ""name"": ""A"", ""source"": """ + _icon + @""", ""position"": [20, 10], ""size"": [40, 30] },
                              { ""type"": ""image"", ""name"": ""B"", ""source"": """ + _icon + @""", ""position"": [150, 70], ""size"": [40, 20],
                                ""constraints"": { ""horizontal"": ""scale"", ""vertical"": ""bottom"" } } ] }", path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            // The root pivot is the frame's center, so local positions are offsets from it (y up).
            AssertCenter((RectTransform)Child(prefab, "A"), new Vector2(20 + 20 - 100, -(10 + 15 - 50)), new Vector2(40, 30));
            AssertCenter((RectTransform)Child(prefab, "B"), new Vector2(150 + 20 - 100, -(70 + 10 - 50)), new Vector2(40, 20));

            static void AssertCenter(RectTransform rt, Vector2 center, Vector2 size)
            {
                Assert.AreEqual(size.x, rt.rect.width, 1e-3f);
                Assert.AreEqual(size.y, rt.rect.height, 1e-3f);
                Vector2 c = (Vector2)rt.localPosition + (new Vector2(0.5f, 0.5f) - rt.pivot) * rt.rect.size;
                Assert.AreEqual(center.x, c.x, 1e-3f, rt.name + " x");
                Assert.AreEqual(center.y, c.y, 1e-3f, rt.name + " y");
            }
        }

        [Test]
        public void Prefab_KeepsItsSpecAndGuid()
        {
            string path = Root + "/Keep.prefab";
            UISpriteMakerApi.BakePrefab(Spec, path);
            string guid = AssetDatabase.AssetPathToGUID(path);

            string spec = UISpriteMakerApi.GetSpec(path);
            StringAssert.Contains("\"layers\"", spec);
            StringAssert.Contains("\"Badge\"", spec);

            UISpriteMakerApi.BakePrefab(spec, path);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
        }
    }
}
