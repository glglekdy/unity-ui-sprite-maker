using NUnit.Framework;
using UnityEngine;

namespace UISpriteMaker.Editor.Tests
{
    public class RasterizerTests
    {
        UISpriteStyle _style;

        [SetUp]
        public void SetUp()
        {
            _style = ScriptableObject.CreateInstance<UISpriteStyle>();
            _style.shape.size = new Vector2Int(100, 50);
            _style.shape.radius = 0f;
            _style.fill.mode = FillMode.Solid;
            _style.fill.color = Color.white;
            _style.dropShadows.Clear();
            _style.outerGlow.enabled = false;
            _style.innerShadow.enabled = false;
            _style.innerGlow.enabled = false;
            _style.stroke.enabled = false;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_style);

        static byte Alpha(RasterResult r, int x, int y) => r.Pixels[y * r.Width + x].a;

        [Test]
        public void PlainRect_HasOnePixelPaddingAndSharpCorners()
        {
            var r = SpriteRasterizer.Render(_style);

            Assert.AreEqual(102, r.Width);
            Assert.AreEqual(52, r.Height);
            Assert.AreEqual(new RectInt(1, 1, 100, 50), r.ShapeRect);
            Assert.AreEqual(255, Alpha(r, 51, 26), "center");
            Assert.AreEqual(255, Alpha(r, 1, 1), "corner pixel inside the shape");
            Assert.AreEqual(0, Alpha(r, 0, 0), "padding");
        }

        [Test]
        public void RoundedCorners_AreTransparentAtTheCorner()
        {
            _style.shape.radius = 20f;
            var r = SpriteRasterizer.Render(_style);

            Assert.AreEqual(0, Alpha(r, 1, 1));
            Assert.AreEqual(255, Alpha(r, 51, 26));
            Assert.AreEqual(255, Alpha(r, 51, 1), "bottom edge middle");
        }

        [Test]
        public void RadiusIsClampedToHalfTheShortSide()
        {
            _style.shape.radius = 1000f;
            var r = SpriteRasterizer.Render(_style);

            Assert.AreEqual(255, Alpha(r, 51, 26));
            Assert.AreEqual(255, Alpha(r, 26, 26), "capsule: left cap center is filled");
        }

        [Test]
        public void DropShadow_ExtendsCanvasOnlyInOffsetDirection()
        {
            _style.dropShadows.Add(new ShadowSettings
            {
                color = Color.black, offset = new Vector2(0f, 10f), blur = 0f, spread = 0f,
            });
            var r = SpriteRasterizer.Render(_style);

            Assert.AreEqual(11, (int)r.Padding.y, "bottom padding");
            Assert.AreEqual(1, (int)r.Padding.w, "top padding");
            int cx = r.Width / 2;
            Assert.Greater(Alpha(r, cx, 5), 200, "shadow below the shape");
            var below = r.Pixels[5 * r.Width + cx];
            Assert.Less(below.r, 10, "shadow is black");
            Assert.AreEqual(0, Alpha(r, cx, r.Height - 1), "nothing above the shape");
        }

        [Test]
        public void BlurredShadow_FadesOut()
        {
            _style.dropShadows.Add(new ShadowSettings { color = Color.black, offset = Vector2.zero, blur = 10f });
            var r = SpriteRasterizer.Render(_style);

            int cy = r.Height / 2;
            byte near = Alpha(r, r.ShapeRect.xMin - 2, cy);
            byte far = Alpha(r, 1, cy);
            Assert.Greater(near, far);
            Assert.Less(far, 20);
        }

        [Test]
        public void LinearGradient_InterpolatesLeftToRight()
        {
            _style.fill.mode = FillMode.LinearGradient;
            _style.fill.angle = 0f;
            _style.fill.gradient = GradientUtil.TwoColor(Color.red, Color.blue);
            var r = SpriteRasterizer.Render(_style);

            int cy = r.Height / 2;
            var left = r.Pixels[cy * r.Width + 2];
            var right = r.Pixels[cy * r.Width + r.Width - 3];
            Assert.Greater(left.r, 240);
            Assert.Less(left.b, 15);
            Assert.Greater(right.b, 240);
            Assert.Less(right.r, 15);
        }

        [Test]
        public void Stroke_DrawnInsideEdge()
        {
            _style.fill.color = Color.black;
            _style.stroke.enabled = true;
            _style.stroke.width = 4f;
            _style.stroke.position = StrokePosition.Inside;
            _style.stroke.fill.color = Color.red;
            var r = SpriteRasterizer.Render(_style);

            int cy = r.Height / 2;
            Assert.AreEqual(102, r.Width, "inside stroke adds no padding");
            Assert.Greater(r.Pixels[cy * r.Width + 2].r, 240, "stroke");
            Assert.Less(r.Pixels[cy * r.Width + 10].r, 10, "fill");
        }

        [Test]
        public void Scale_MultipliesPixelSize()
        {
            _style.shape.scale = 2;
            var r = SpriteRasterizer.Render(_style);

            Assert.AreEqual(202, r.Width);
            Assert.AreEqual(102, r.Height);
            Assert.AreEqual(2, r.Scale);
        }

        [Test]
        public void NineSliceBorder_CoversRadiusAndPadding()
        {
            _style.shape.radius = 12f;
            var r = SpriteRasterizer.Render(_style);

            Assert.AreEqual(new Vector4(14, 14, 14, 14), r.Border);

            _style.nineSlice = false;
            Assert.AreEqual(Vector4.zero, SpriteRasterizer.Render(_style).Border);
        }

        [Test]
        public void NineSliceBorder_LeavesStretchableCenter()
        {
            _style.shape.size = new Vector2Int(20, 20);
            _style.shape.radius = 10f;
            var r = SpriteRasterizer.Render(_style);

            Assert.Less(r.Border.x + r.Border.z, r.Width);
            Assert.Less(r.Border.y + r.Border.w, r.Height);
        }

        [Test]
        public void JsonRoundTrip_PreservesStyleAndFlags()
        {
            _style.shape.radius = 33f;
            _style.fill.gradient = GradientUtil.TwoColor(Color.green, Color.yellow);
            var copy = ScriptableObject.CreateInstance<UISpriteStyle>();
            copy.hideFlags = HideFlags.DontSave;
            try
            {
                copy.LoadJson(_style.ToJson());
                Assert.AreEqual(33f, copy.shape.radius);
                Assert.AreEqual(Color.green, copy.fill.gradient.Evaluate(0f));
                Assert.AreEqual(HideFlags.DontSave, copy.hideFlags);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }
    }
}
