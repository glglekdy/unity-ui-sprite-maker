using NUnit.Framework;
using UnityEngine;

namespace UISpriteMaker.Editor.Tests
{
    public class LayerOpsTests
    {
        UISpriteStyle _style;

        [SetUp]
        public void SetUp() => _style = SpriteSpec.Parse(@"{ ""size"": [200, 100], ""layers"": [
            { ""type"": ""shape"", ""name"": ""A"", ""position"": [10, 10], ""size"": [20, 20] },
            { ""type"": ""group"", ""name"": ""G"", ""position"": [100, 40], ""size"": [80, 50], ""rotation"": 90,
              ""layers"": [ { ""type"": ""shape"", ""name"": ""B"", ""position"": [5, 5], ""size"": [10, 10] } ] } ] }");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_style);

        Layer Named(string name)
        {
            foreach (var l in LayerTree.Walk(_style.layers))
                if (l.name == name) return l;
            return null;
        }

        static Vector2 CanvasCenter(UISpriteStyle style, Layer layer)
        {
            var layout = SpriteRasterizer.ComputeLayout(style);
            var p = LayerGeometry.Placements(style, layout.ShapeRect, layout.Scale)[layer.id];
            return new Vector2(p.Cx - layout.ShapeRect.x, p.Cy - layout.ShapeRect.y);
        }

        [Test]
        public void Add_CentersInTheSelectedContainer_OrAboveTheSelectedLayer()
        {
            var inGroup = new ShapeLayer { size = new Vector2(20, 10) };
            LayerOps.Add(_style, inGroup, Named("G").id);
            Assert.AreSame(inGroup, Named("G").Children[1]);
            Assert.AreEqual(new Vector2(30, 20), inGroup.position);

            var label = new TextLayer { name = "Label", size = new Vector2(10, 10) };
            LayerOps.Add(_style, label, Named("A").id);
            Assert.AreSame(label, Named("A").Children[0], "shapes hold children too (e.g. a button's label)");

            var aboveLabel = new ShapeLayer { size = new Vector2(4, 4) };
            LayerOps.Add(_style, aboveLabel, label.id);
            Assert.AreSame(aboveLabel, Named("A").Children[1], "text can't hold children: added just above it");
        }

        [Test]
        public void Duplicate_InsertsACopyAbove()
        {
            var copy = LayerOps.Duplicate(_style, Named("G").id);
            Assert.AreSame(copy, _style.layers[2]);
            Assert.AreEqual("G copy", copy.name);
            Assert.AreNotEqual(Named("B").id, copy.Children[0].id);
        }

        [Test]
        public void Group_KeepsTheLayerInPlace()
        {
            var a = Named("A");
            var before = CanvasCenter(_style, a);
            var group = LayerOps.Group(_style, a.id);
            Assert.AreSame(group, _style.layers[0]);
            Assert.AreEqual(new Vector2(10, 10), group.position);
            Assert.AreEqual(Vector2.zero, a.position);
            Assert.AreEqual(before, CanvasCenter(_style, a));
        }

        [Test]
        public void Move_BetweenParents_KeepsTheLayerWhereItIs()
        {
            var b = Named("B");
            var before = CanvasCenter(_style, b);
            Assert.IsTrue(LayerOps.Move(_style, b.id, null, 0));
            Assert.AreSame(b, _style.layers[0]);
            Assert.AreEqual(90f, b.rotation, 1e-3f, "keeps the parent's rotation");
            var after = CanvasCenter(_style, b);
            Assert.AreEqual(before.x, after.x, 1e-3f);
            Assert.AreEqual(before.y, after.y, 1e-3f);

            Assert.IsTrue(LayerOps.Move(_style, b.id, Named("G").id, 1));
            Assert.AreEqual(0f, b.rotation, 1e-3f);
            Assert.AreEqual(5f, b.position.x, 1e-3f);
            Assert.AreEqual(5f, b.position.y, 1e-3f);
        }

        [Test]
        public void Move_IntoItself_IsRefused()
        {
            var g = Named("G");
            Assert.IsFalse(LayerOps.Move(_style, g.id, g.id, 0));
            Assert.IsFalse(LayerOps.Move(_style, g.id, Named("B").id, 0), "B can't hold children");
        }

        [Test]
        public void Reorder_StaysInsideTheList()
        {
            Assert.IsTrue(LayerOps.Reorder(_style, Named("A").id, 1));
            Assert.AreEqual("A", _style.layers[1].name);
            Assert.IsFalse(LayerOps.Reorder(_style, Named("A").id, 1));
        }

        [Test]
        public void ResizeFrame_AppliesConstraints()
        {
            var a = Named("A");
            var g = Named("G");
            a.horizontal = HorizontalConstraint.Right;
            a.vertical = VerticalConstraint.Center;
            g.horizontal = HorizontalConstraint.Stretch;
            g.vertical = VerticalConstraint.Scale;
            var b = Named("B");
            b.horizontal = HorizontalConstraint.Center;

            LayerOps.ResizeFrame(_style, new Vector2Int(300, 200));

            Assert.AreEqual(new Vector2(110, 60), a.position);
            Assert.AreEqual(new Vector2(20, 20), a.size);
            Assert.AreEqual(new Vector2(100, 80), g.position);
            Assert.AreEqual(new Vector2(180, 100), g.size);
            Assert.AreEqual(55f, b.position.x, "the group grew by 100, so a centered child moves by 50");
        }
    }
}
