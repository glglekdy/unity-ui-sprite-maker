using NUnit.Framework;
using UnityEngine;

namespace UISpriteMaker.Editor.Tests
{
    public class SpecTests
    {
        const string FullSpec = @"{
  // comments are allowed
  ""size"": [240, 80],
  ""radius"": [4, 8, 12, 16],
  ""scale"": 2,
  ""nineSlice"": false,
  ""fill"": { ""type"": ""linear"", ""direction"": ""to right"", ""colors"": [""#FF0000"", { ""color"": ""rgba(0, 0, 255, 0.5)"", ""at"": 1 }] },
  ""stroke"": { ""width"": 3, ""position"": ""outside"", ""color"": ""#FFFFFF"" },
  ""shadows"": [ { ""color"": ""#00000080"", ""offset"": [0, 6], ""blur"": 10, ""spread"": 2 }, { ""blur"": 2 } ],
  ""glow"": { ""color"": ""#5AC8FF"", ""size"": 20, ""intensity"": 1.5 },
  ""innerShadow"": { ""offset"": [0, 3], ""blur"": 5 },
  ""innerGlow"": { ""color"": ""#FFFFFF66"", ""blur"": 8, ""choke"": 1 }
}";

        static UISpriteStyle Parse(string json) => SpriteSpec.Parse(json);

        [Test]
        public void MinimalSpec_DisablesEverythingNotMentioned()
        {
            var s = Parse(@"{ ""size"": [100, 50] }");
            try
            {
                Assert.AreEqual(new Vector2Int(100, 50), s.shape.size);
                Assert.AreEqual(0f, s.shape.radius);
                Assert.AreEqual(FillMode.Solid, s.fill.mode);
                Assert.AreEqual(Color.white, s.fill.color);
                Assert.IsEmpty(s.dropShadows);
                Assert.IsFalse(s.outerGlow.enabled);
                Assert.IsFalse(s.stroke.enabled);
                Assert.IsFalse(s.innerShadow.enabled);
                Assert.IsFalse(s.innerGlow.enabled);
                Assert.IsTrue(s.nineSlice);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void FullSpec_MapsEveryField()
        {
            var s = Parse(FullSpec);
            try
            {
                Assert.IsFalse(s.shape.linkCorners);
                Assert.AreEqual(new Vector4(4, 8, 12, 16), s.shape.GetRadii());
                Assert.AreEqual(2, s.shape.scale);
                Assert.IsFalse(s.nineSlice);

                Assert.AreEqual(FillMode.LinearGradient, s.fill.mode);
                Assert.AreEqual(0f, s.fill.angle);
                Assert.AreEqual(Color.red, s.fill.gradient.Evaluate(0f));
                Assert.AreEqual(0.5f, s.fill.gradient.Evaluate(1f).a, 0.01f);

                Assert.IsTrue(s.stroke.enabled);
                Assert.AreEqual(StrokePosition.Outside, s.stroke.position);
                Assert.AreEqual(3f, s.stroke.width);

                Assert.AreEqual(2, s.dropShadows.Count);
                Assert.AreEqual(new Vector2(0, 6), s.dropShadows[0].offset);
                Assert.AreEqual(2f, s.dropShadows[0].spread);
                Assert.AreEqual(2f, s.dropShadows[1].blur);
                Assert.AreEqual(new Vector2(0, 4), s.dropShadows[1].offset, "defaults fill unspecified fields");

                Assert.IsTrue(s.outerGlow.enabled);
                Assert.AreEqual(1.5f, s.outerGlow.intensity);
                Assert.IsTrue(s.innerShadow.enabled);
                Assert.AreEqual(5f, s.innerShadow.blur);
                Assert.IsTrue(s.innerGlow.enabled);
                Assert.AreEqual(1f, s.innerGlow.choke);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void SpecRoundTrip_IsStable()
        {
            var a = Parse(FullSpec);
            string json1 = SpriteSpec.ToJson(a);
            var b = Parse(json1);
            try
            {
                Assert.AreEqual(json1, SpriteSpec.ToJson(b));
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void DefaultWindowStyle_RoundTrips()
        {
            var style = ScriptableObject.CreateInstance<UISpriteStyle>();
            UISpriteStyle parsed = null;
            try
            {
                parsed = Parse(SpriteSpec.ToJson(style));
                var original = SpriteRasterizer.Render(style);
                var copy = SpriteRasterizer.Render(parsed);
                Assert.AreEqual(original.Width, copy.Width);
                Assert.AreEqual(original.Height, copy.Height);
                int mid = original.Width * (original.Height / 2) + original.Width / 2;
                Assert.AreEqual(original.Pixels[mid], copy.Pixels[mid]);
            }
            finally
            {
                Object.DestroyImmediate(style);
                if (parsed != null) Object.DestroyImmediate(parsed);
            }
        }

        [TestCase(@"{ ""radius"": 4 }", "$.size: required")]
        [TestCase(@"{ ""size"": [100, 50], ""shadow"": {} }", "$.shadow: unknown property")]
        [TestCase(@"{ ""size"": [100, 50], ""fill"": ""#12345"" }", "$.fill: invalid color")]
        [TestCase(@"{ ""size"": [100.5, 50] }", "$.size[0]: expected a whole number")]
        [TestCase(@"{ ""size"": [100, 50], ""fill"": { ""type"": ""linear"" } }", "$.fill.colors: required")]
        [TestCase(@"{ ""size"": [100, 50], ""stroke"": { ""position"": ""middle"" } }", "$.stroke.position: expected one of")]
        [TestCase(@"{ ""size"": [100, 50], ""shadows"": [ { ""blur"": -1 } ] }", "$.shadows[0].blur: must be >= 0")]
        [TestCase(@"{ ""size"": [100, 50], ", "Invalid JSON")]
        public void InvalidSpec_ReportsPath(string json, string expected)
        {
            var e = Assert.Throws<SpriteSpecException>(() => Parse(json));
            StringAssert.Contains(expected, e.Message);
        }

        [Test]
        public void ParseColor_SupportsCommonFormats()
        {
            Assert.AreEqual(new Color(1, 0, 0, 1), SpriteSpec.ParseColor("#F00", "c"));
            Assert.AreEqual(new Color(0, 1, 0, 1), SpriteSpec.ParseColor("#00FF00", "c"));
            Assert.AreEqual(0.5f, SpriteSpec.ParseColor("#0000FF80", "c").a, 0.01f);
            Assert.AreEqual(new Color(1, 1, 1, 0.25f), SpriteSpec.ParseColor("rgba(255, 255, 255, 0.25)", "c"));
            Assert.AreEqual(Color.white, SpriteSpec.ParseColor("white", "c"));
        }

        [Test]
        public void Validate_ReturnsNullOrMessage()
        {
            Assert.IsNull(UISpriteMakerApi.Validate(@"{ ""size"": [10, 10] }"));
            StringAssert.Contains("size", UISpriteMakerApi.Validate("{}"));
        }
    }
}
