using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UISpriteMaker.Editor.Tests
{
    public class LayerTests
    {
        const string Root = "Assets/__UISpriteMakerLayerTests";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
        }

        static RasterResult Render(string spec)
        {
            var style = SpriteSpec.Parse(spec);
            try
            {
                return SpriteRasterizer.Render(style);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        static SpriteLayout Layout(string spec)
        {
            var style = SpriteSpec.Parse(spec);
            try
            {
                return SpriteRasterizer.ComputeLayout(style);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        static Color32 Px(RasterResult r, int x, int y) => r.Pixels[y * r.Width + x];

        /// <summary>Canvas pixel of a point given in frame units (top-left origin, y down), for a 1px-padded @1x canvas.</summary>
        static Color32 At(RasterResult r, int ux, int uy) => Px(r, r.ShapeRect.x + ux, r.ShapeRect.y + r.ShapeRect.height - 1 - uy);

        static ulong Hash(Color32[] px)
        {
            ulong h = 1469598103934665603UL;
            foreach (var p in px)
            {
                h = (h ^ p.r) * 1099511628211UL;
                h = (h ^ p.g) * 1099511628211UL;
                h = (h ^ p.b) * 1099511628211UL;
                h = (h ^ p.a) * 1099511628211UL;
            }
            return h;
        }

        // Pixel hashes of specs without layers, recorded with the renderer before layers existed.
        [TestCase(@"{ ""size"": [100, 50] }", 102, 52, 4032646771185585635UL)]
        [TestCase(@"{ ""size"": [240, 72], ""radius"": 36, ""scale"": 2, ""fill"": { ""type"": ""linear"", ""direction"": ""to top"", ""colors"": [""#2F6BFF"", ""#6FA8FF""] }, ""shadows"": [ { ""color"": ""#1B3A8A66"", ""offset"": [0, 6], ""blur"": 14 } ], ""innerGlow"": { ""color"": ""#FFFFFF40"", ""blur"": 6 } }", 566, 230, 9336113594454519703UL)]
        [TestCase(@"{ ""size"": [320, 200], ""radius"": 20, ""fill"": ""#1E2233"", ""stroke"": { ""width"": 1.5, ""color"": ""#FFFFFF1F"" }, ""shadows"": [ { ""color"": ""#00000080"", ""offset"": [0, 12], ""blur"": 28 } ] }", 406, 286, 10822473188067912491UL)]
        [TestCase(@"{ ""size"": [96, 96], ""radius"": 48, ""fill"": { ""type"": ""radial"", ""center"": [0.4, 0.6], ""colors"": [""#FFE27A"", ""#FF7A45""] }, ""glow"": { ""color"": ""#FF9A3C"", ""size"": 18, ""intensity"": 1.2 }, ""stroke"": { ""width"": 3, ""color"": ""#FFFFFFCC"" } }", 152, 152, 8130805988160373456UL)]
        [TestCase(@"{ ""size"": [120, 60], ""radius"": [4, 8, 12, 16], ""fill"": ""#335"", ""innerShadow"": { ""offset"": [2, 3], ""blur"": 5, ""choke"": 1 }, ""stroke"": { ""width"": 4, ""position"": ""center"", ""fill"": { ""type"": ""linear"", ""angle"": 30, ""colors"": [""#F00"", ""#00F""] } }, ""shadows"": [ { ""offset"": [-3, 2], ""blur"": 0, ""spread"": 3 } ] }", 130, 69, 15142296090364616481UL)]
        public void SpecsWithoutLayers_RenderExactlyAsBefore(string spec, int w, int h, ulong hash)
        {
            var r = Render(spec);
            Assert.AreEqual(w, r.Width);
            Assert.AreEqual(h, r.Height);
            Assert.AreEqual(hash, Hash(r.Pixels));
        }

        [Test]
        public void ChildShape_IsDrawnAtItsPosition()
        {
            var r = Render(@"{ ""size"": [100, 50], ""fill"": ""#FFFFFF"",
                ""layers"": [ { ""type"": ""shape"", ""position"": [10, 10], ""size"": [20, 10], ""fill"": ""#FF0000"" } ] }");

            Assert.AreEqual(102, r.Width, "a layer inside the frame needs no extra padding");
            Assert.AreEqual(new Color32(255, 0, 0, 255), At(r, 15, 15));
            Assert.AreEqual(new Color32(255, 0, 0, 255), At(r, 10, 10), "top-left pixel of the child");
            Assert.AreEqual(new Color32(255, 255, 255, 255), At(r, 9, 15));
            Assert.AreEqual(new Color32(255, 255, 255, 255), At(r, 15, 20), "y grows downwards");
        }

        [Test]
        public void NestedChildren_ArePositionedInTheirParentsBox()
        {
            var r = Render(@"{ ""size"": [100, 50], ""fill"": ""#FFFFFF"",
                ""layers"": [ { ""type"": ""group"", ""position"": [40, 20], ""size"": [30, 20],
                    ""layers"": [ { ""type"": ""shape"", ""position"": [5, 5], ""size"": [4, 4], ""fill"": ""#0000FF"" } ] } ] }");

            Assert.AreEqual(new Color32(0, 0, 255, 255), At(r, 46, 26));
            Assert.AreEqual(new Color32(255, 255, 255, 255), At(r, 44, 26));
        }

        [Test]
        public void Opacity_ScalesAlpha_AndHiddenLayersAreSkipped()
        {
            var r = Render(@"{ ""size"": [40, 40], ""fill"": ""#00000000"",
                ""layers"": [
                    { ""type"": ""shape"", ""position"": [0, 0], ""size"": [20, 40], ""fill"": ""#FF0000"", ""opacity"": 0.5 },
                    { ""type"": ""shape"", ""position"": [20, 0], ""size"": [20, 40], ""fill"": ""#FF0000"", ""visible"": false } ] }");

            var half = At(r, 10, 20);
            Assert.AreEqual(128, half.a, 1);
            Assert.AreEqual(255, half.r);
            Assert.AreEqual(0, At(r, 30, 20).a);
        }

        [TestCase("multiply", 128, 0, 0)]
        [TestCase("screen", 255, 128, 128)]
        [TestCase("darken", 128, 0, 0)]
        [TestCase("lighten", 255, 128, 128)]
        [TestCase("normal", 255, 0, 0)]
        public void BlendModes_MixWithWhatIsBelow(string mode, int r, int g, int b)
        {
            var result = Render(@"{ ""size"": [20, 20], ""fill"": ""#808080"",
                ""layers"": [ { ""type"": ""shape"", ""size"": [20, 20], ""fill"": ""#FF0000"", ""blendMode"": """ + mode + @""" } ] }");

            var c = At(result, 10, 10);
            Assert.AreEqual(r, c.r, 1);
            Assert.AreEqual(g, c.g, 1);
            Assert.AreEqual(b, c.b, 1);
            Assert.AreEqual(255, c.a);
        }

        [Test]
        public void GroupWithoutOpacity_PassesBlendModesThrough()
        {
            var r = Render(@"{ ""size"": [20, 20], ""fill"": ""#808080"",
                ""layers"": [ { ""type"": ""group"", ""size"": [20, 20],
                    ""layers"": [ { ""type"": ""shape"", ""size"": [20, 20], ""fill"": ""#FF0000"", ""blendMode"": ""multiply"" } ] } ] }");

            Assert.AreEqual(128, At(r, 10, 10).r, 1, "the child multiplies with the frame, not with an empty group");
        }

        [Test]
        public void Clip_CutsLayersAtTheFrame_AndKeepsTheCanvasSize()
        {
            const string layers = @"""layers"": [ { ""type"": ""shape"", ""position"": [30, 10], ""size"": [40, 10], ""fill"": ""#FF0000"" } ]";
            var clipped = Render(@"{ ""size"": [50, 30], ""fill"": ""#FFFFFF"", ""clip"": true, " + layers + " }");
            var open = Render(@"{ ""size"": [50, 30], ""fill"": ""#FFFFFF"", " + layers + " }");

            Assert.AreEqual(52, clipped.Width);
            Assert.AreEqual(0, Px(clipped, clipped.ShapeRect.xMax + 0, clipped.ShapeRect.y + 15).a, "nothing past the frame");
            Assert.AreEqual(72, open.Width, "unclipped layer grows the canvas: 30 + 40 + 1 + 1");
            Assert.AreEqual(new Color32(255, 0, 0, 255), At(open, 60, 15));
        }

        [Test]
        public void Rotation_TurnsTheLayerAroundItsCenter()
        {
            var r = Render(@"{ ""size"": [60, 60], ""fill"": ""#FFFFFF"",
                ""layers"": [ { ""type"": ""shape"", ""position"": [10, 25], ""size"": [40, 10], ""fill"": ""#000000"", ""rotation"": 90 } ] }");

            Assert.AreEqual(0, At(r, 30, 12).r, "rotated bar reaches up");
            Assert.AreEqual(0, At(r, 30, 47).r, "and down");
            Assert.AreEqual(255, At(r, 12, 30).r, "but no longer sideways");
        }

        [Test]
        public void LayerEffects_GrowThePadding()
        {
            var plain = Layout(@"{ ""size"": [100, 50], ""layers"": [ { ""type"": ""shape"", ""position"": [10, 10], ""size"": [20, 20] } ] }");
            var shadow = Layout(@"{ ""size"": [100, 50], ""layers"": [ { ""type"": ""shape"", ""position"": [0, 0], ""size"": [20, 20],
                ""shadows"": [ { ""offset"": [0, 0], ""blur"": 10 } ] } ] }");

            Assert.AreEqual(new Vector4(1, 1, 1, 1), plain.Padding);
            Assert.AreEqual(16, shadow.Padding.x, "blur 10 reaches 15px past the layer's left edge");
            Assert.AreEqual(16, shadow.Padding.w);
            Assert.AreEqual(1, shadow.Padding.z);
        }

        [Test]
        public void NineSliceBorder_MovesAroundDecorations()
        {
            var layout = Layout(@"{ ""size"": [200, 60], ""radius"": 10,
                ""layers"": [ { ""type"": ""shape"", ""position"": [170, 5], ""size"": [20, 20], ""radius"": 10 } ] }");

            Assert.IsFalse(layout.SliceBlockedX);
            float badgeLeft = layout.ShapeRect.x + 170;
            Assert.GreaterOrEqual(layout.Border.z, layout.Width - badgeLeft, "the badge is inside the fixed right border");
            Assert.Greater(layout.Width - layout.Border.x - layout.Border.z, 100, "a wide stretch band remains");
        }

        [Test]
        public void NineSlice_IsBlockedByFullWidthLayers_UnlessTheyStretch()
        {
            const string spec = @"{ ""size"": [200, 60],
                ""layers"": [ { ""type"": ""shape"", ""position"": [0, 20], ""size"": [200, 4], ""constraints"": { ""horizontal"": ""$H"" } } ] }";
            Assert.IsTrue(Layout(spec.Replace("$H", "left")).SliceBlockedX);
            Assert.IsFalse(Layout(spec.Replace("$H", "stretch")).SliceBlockedX);
        }

        [Test]
        public void Text_IsRenderedInsideItsBox()
        {
            var r = Render(@"{ ""size"": [120, 40], ""fill"": ""#00000000"",
                ""layers"": [ { ""type"": ""text"", ""text"": ""Hello"", ""fontSize"": 20, ""color"": ""#FFFFFF"",
                    ""position"": [10, 5], ""size"": [100, 30] } ] }");

            Assert.IsEmpty(r.Warnings);
            int inked = 0;
            for (int uy = 0; uy < 40; uy++)
            for (int ux = 0; ux < 120; ux++)
            {
                if (At(r, ux, uy).a < 128) continue;
                inked++;
                Assert.That(ux >= 9 && ux <= 111 && uy >= 4 && uy <= 36, $"ink at {ux},{uy} is outside the text box");
            }
            Assert.Greater(inked, 50);
        }

        [Test]
        public void Text_WithoutSize_IsSizedToFit()
        {
            var style = SpriteSpec.Parse(@"{ ""size"": [200, 60], ""layers"": [ { ""type"": ""text"", ""text"": ""Wide text"", ""fontSize"": 20 } ] }");
            try
            {
                var t = (TextLayer)style.layers[0];
                Assert.Greater(t.size.x, 60f);
                Assert.Greater(t.size.y, 18f);
                Assert.IsFalse(t.wrap);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void Text_OutsideStroke_AddsInk()
        {
            const string spec = @"{ ""size"": [120, 40], ""fill"": ""#00000000"",
                ""layers"": [ { ""type"": ""text"", ""text"": ""Hello"", ""fontSize"": 20, ""position"": [10, 5], ""size"": [100, 30] $S } ] }";
            int Count(RasterResult r) => r.Pixels.Count(p => p.a > 128);
            int plain = Count(Render(spec.Replace("$S", "")));
            int stroked = Count(Render(spec.Replace("$S", @", ""stroke"": { ""width"": 2, ""position"": ""outside"", ""color"": ""#FF0000"" }")));
            Assert.Greater(stroked, plain * 1.3f);
        }

        [Test]
        public void Text_WithCharactersTheFontLacks_IsAnError()
        {
            var e = Assert.Throws<SpriteSpecException>(() => SpriteSpec.Parse(
                @"{ ""size"": [100, 40], ""layers"": [ { ""type"": ""text"", ""text"": ""한글"" } ] }"));
            StringAssert.StartsWith("$.layers[0].text:", e.Message);
            StringAssert.Contains("한", e.Message);
        }

        [Test]
        public void Image_IsSampledIntoItsBox()
        {
            string path = CreatePng("Red.png", 4, 4, Color.red);
            var r = Render(@"{ ""size"": [40, 40], ""fill"": ""#FFFFFF"",
                ""layers"": [ { ""type"": ""image"", ""source"": """ + path + @""", ""position"": [10, 10], ""size"": [20, 20], ""tint"": ""#FFFFFF80"" } ] }");

            Assert.IsEmpty(r.Warnings);
            var c = At(r, 20, 20);
            Assert.AreEqual(255, c.r);
            Assert.AreEqual(127, c.g, 2, "half transparent red over white");
            Assert.AreEqual(new Color32(255, 255, 255, 255), At(r, 5, 20));
        }

        [Test]
        public void Image_WithoutSize_UsesTheSpriteSize()
        {
            string path = CreatePng("Small.png", 8, 6, Color.green);
            var style = SpriteSpec.Parse(@"{ ""size"": [40, 40], ""layers"": [ { ""type"": ""image"", ""source"": """ + path + @""" } ] }");
            try
            {
                Assert.AreEqual(new Vector2(8, 6), style.layers[0].size);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void Spec_RoundTripsLayers()
        {
            const string spec = @"{ ""size"": [200, 80], ""clip"": true,
                ""layers"": [
                    { ""type"": ""shape"", ""name"": ""Badge"", ""position"": [150, 8], ""size"": [40, 20], ""radius"": [4, 4, 10, 10],
                      ""fill"": { ""type"": ""linear"", ""direction"": ""to right"", ""colors"": [""#FF0000"", ""#0000FF""] },
                      ""rotation"": 12, ""opacity"": 0.75, ""blendMode"": ""screen"", ""export"": ""object"",
                      ""constraints"": { ""horizontal"": ""right"", ""vertical"": ""top"" },
                      ""shadows"": [ { ""offset"": [0, 2], ""blur"": 4 } ], ""stroke"": { ""width"": 1, ""color"": ""#FFFFFF"" },
                      ""layers"": [ { ""type"": ""group"", ""size"": [10, 10], ""clip"": true, ""visible"": false } ] },
                    { ""type"": ""text"", ""text"": ""Hi"", ""fontSize"": 18, ""color"": ""#112233"", ""align"": ""center"",
                      ""verticalAlign"": ""middle"", ""size"": [80, 30], ""lineHeight"": 1.2, ""letterSpacing"": 1, ""wrap"": false,
                      ""glow"": { ""size"": 6 } }
                ] }";
            var first = SpriteSpec.Parse(spec);
            string json = SpriteSpec.ToJson(first);
            var second = SpriteSpec.Parse(json);
            try
            {
                Assert.AreEqual(json, SpriteSpec.ToJson(second));
                var badge = (ShapeLayer)second.layers[0];
                Assert.AreEqual(LayerBlendMode.Screen, badge.blendMode);
                Assert.AreEqual(HorizontalConstraint.Right, badge.horizontal);
                Assert.AreEqual(LayerExport.Object, badge.export);
                Assert.IsTrue(((GroupLayer)badge.children[0]).clip);
                Assert.IsFalse(badge.children[0].visible);
                var text = (TextLayer)second.layers[1];
                Assert.AreEqual(TextVerticalAlign.Middle, text.verticalAlign);
                Assert.IsTrue(text.effects.outerGlow.enabled);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [TestCase(@"{ ""size"": [10, 10], ""layers"": [ { ""size"": [1, 1] } ] }", "$.layers[0].type: required")]
        [TestCase(@"{ ""size"": [10, 10], ""layers"": [ { ""type"": ""circle"" } ] }", "$.layers[0].type: expected")]
        [TestCase(@"{ ""size"": [10, 10], ""layers"": [ { ""type"": ""shape"" } ] }", "$.layers[0].size: required")]
        [TestCase(@"{ ""size"": [10, 10], ""layers"": [ { ""type"": ""shape"", ""size"": [1, 1], ""source"": ""x"" } ] }", "$.layers[0].source: unknown property")]
        [TestCase(@"{ ""size"": [10, 10], ""layers"": [ { ""type"": ""shape"", ""size"": [1, 1], ""opacity"": 2 } ] }", "$.layers[0].opacity:")]
        [TestCase(@"{ ""size"": [10, 10], ""layers"": [ { ""type"": ""group"", ""size"": [1, 1], ""layers"": [ { ""type"": ""shape"", ""size"": [1, 1], ""blendMode"": ""burn"" } ] } ] }", "$.layers[0].layers[0].blendMode:")]
        [TestCase(@"{ ""size"": [10, 10], ""layers"": [ { ""type"": ""image"", ""source"": ""Assets/nope.png"" } ] }", "$.layers[0].source: no Sprite")]
        [TestCase(@"{ ""size"": [10, 10], ""layers"": { ""type"": ""shape"" } }", "$.layers: expected an array")]
        public void InvalidLayers_NameTheField(string spec, string expected)
        {
            var e = Assert.Throws<SpriteSpecException>(() => SpriteSpec.Parse(spec));
            StringAssert.StartsWith(expected, e.Message);
        }

        [Test]
        public void Clone_CopiesDeeplyWithNewIds()
        {
            var style = SpriteSpec.Parse(@"{ ""size"": [10, 10], ""layers"": [ { ""type"": ""group"", ""size"": [5, 5],
                ""layers"": [ { ""type"": ""shape"", ""size"": [1, 1], ""fill"": ""#123456"" } ] } ] }");
            try
            {
                var original = style.layers[0];
                var copy = LayerClipboard.Clone(original);
                Assert.AreNotEqual(original.id, copy.id);
                Assert.AreNotEqual(original.Children[0].id, copy.Children[0].id);
                Assert.AreNotSame(original.Children[0], copy.Children[0]);
                Assert.AreEqual(MiniJson.Serialize(SpriteSpec.LayerToObject(original)), MiniJson.Serialize(SpriteSpec.LayerToObject(copy)));
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void LoadJson_OfAnOldStyle_DropsCurrentLayers()
        {
            var style = SpriteSpec.Parse(@"{ ""size"": [10, 10], ""clip"": true, ""layers"": [ { ""type"": ""shape"", ""size"": [1, 1] } ] }");
            try
            {
                style.LoadJson(@"{ ""shape"": { ""size"": { ""x"": 20, ""y"": 20 } } }");
                Assert.IsEmpty(style.layers);
                Assert.IsFalse(style.clip);
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        [Test]
        public void StyleJson_KeepsLayersAcrossSaveAndLoad()
        {
            var a = SpriteSpec.Parse(@"{ ""size"": [10, 10], ""layers"": [ { ""type"": ""text"", ""text"": ""A"", ""size"": [5, 5] } ] }");
            var b = ScriptableObject.CreateInstance<UISpriteStyle>();
            try
            {
                b.LoadJson(a.ToJson());
                Assert.IsInstanceOf<TextLayer>(b.layers[0]);
                Assert.AreEqual("A", ((TextLayer)b.layers[0]).text);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        static string CreatePng(string name, int w, int h, Color color)
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", Root.Substring("Assets/".Length));
            string path = $"{Root}/{name}";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(Enumerable.Repeat(color, w * h).ToArray());
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.SaveAndReimport();
            return path;
        }
    }
}
