using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UISpriteMaker.Editor
{
    /// <summary>Right pane: the properties of the selected layer, or of the frame.</summary>
    sealed class PropertiesPanel : VisualElement
    {
        readonly UISpriteMakerWindow _host;
        readonly ScrollView _scroll;

        public PropertiesPanel(UISpriteMakerWindow host)
        {
            _host = host;
            style.flexGrow = 1;
            _scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            _scroll.contentContainer.style.paddingLeft = 6;
            _scroll.contentContainer.style.paddingRight = 6;
            _scroll.contentContainer.style.paddingBottom = 12;
            Add(_scroll);
        }

        public void ShowFrame(SerializedObject so)
        {
            _scroll.Clear();
            _scroll.Add(Header("Frame", "The sprite's base shape. Layers are drawn on top of its fill."));
            var it = so.GetIterator();
            it.NextVisible(true);
            do
            {
                if (it.propertyPath == "m_Script" || it.propertyPath == nameof(UISpriteStyle.layers)) continue;
                _scroll.Add(new PropertyField(it.Copy()));
            } while (it.NextVisible(false));
            _scroll.Bind(so);
        }

        public void ShowLayer(SerializedObject so, string path, Layer layer)
        {
            _scroll.Clear();
            string kind = layer switch
            {
                ShapeLayer _ => "Shape",
                ImageLayer _ => "Image",
                TextLayer _ => "Text",
                GroupLayer _ => "Group",
                _ => "Layer",
            };
            _scroll.Add(Header(kind + " layer", null));
            AddField(so, path, nameof(Layer.name));

            var transform = Section("Transform");
            AddField(so, path, nameof(Layer.position), transform);
            AddField(so, path, nameof(Layer.size), transform);
            AddField(so, path, nameof(Layer.rotation), transform);

            var look = Section("Layer");
            AddField(so, path, nameof(Layer.opacity), look);
            AddField(so, path, nameof(Layer.blendMode), look);
            AddField(so, path, nameof(Layer.visible), look);
            AddField(so, path, nameof(Layer.locked), look);
            if (layer.Children != null) AddField(so, path, "clip", look, "Clip Content");
            AddField(so, path, nameof(Layer.export), look, "Prefab Export");

            var constraints = Section("Constraints");
            AddField(so, path, nameof(Layer.horizontal), constraints);
            AddField(so, path, nameof(Layer.vertical), constraints);

            switch (layer)
            {
                case ShapeLayer _:
                {
                    var s = Section("Shape");
                    AddField(so, path, nameof(ShapeLayer.radius), s);
                    AddField(so, path, nameof(ShapeLayer.fill), s);
                    break;
                }
                case ImageLayer image:
                {
                    var s = Section("Image");
                    AddField(so, path, nameof(ImageLayer.source), s);
                    AddField(so, path, nameof(ImageLayer.tint), s);
                    AddField(so, path, nameof(ImageLayer.fit), s);
                    var help = new HelpBox("Source must be a Sprite or a Texture2D.", HelpBoxMessageType.Warning);
                    s.Add(help);
                    void RefreshHelp() => help.style.display =
                        image.source == null || ImageSource.IsSupported(image.source) ? DisplayStyle.None : DisplayStyle.Flex;
                    RefreshHelp();
                    help.TrackSerializedObjectValue(so, _ => RefreshHelp());
                    s.Add(new Button(() => _host.Edit("Image Natural Size", () =>
                    {
                        if (image.source == null) return false;
                        LayerOps.Resize(image, SpriteSpec.NaturalSize(image.source));
                        return true;
                    }, false)) { text = "Use the image's size" });
                    break;
                }
                case TextLayer text:
                {
                    var s = Section("Text");
                    AddField(so, path, nameof(TextLayer.text), s);
                    AddField(so, path, nameof(TextLayer.font), s, "Font (empty = Inter, Latin only)");
                    AddField(so, path, nameof(TextLayer.fontSize), s);
                    AddField(so, path, nameof(TextLayer.fill), s);
                    AddField(so, path, nameof(TextLayer.align), s);
                    AddField(so, path, nameof(TextLayer.verticalAlign), s);
                    AddField(so, path, nameof(TextLayer.lineHeight), s);
                    AddField(so, path, nameof(TextLayer.letterSpacing), s);
                    AddField(so, path, nameof(TextLayer.wrap), s);
                    s.Add(new Button(() => _host.Edit("Fit Text Box", () =>
                    {
                        text.size = TextEngine.Measure(text);
                        text.wrap = false;
                        return true;
                    }, false)) { text = "Fit box to text" });
                    break;
                }
            }

            var fx = Section("Effects");
            string e = path + "." + nameof(Layer.effects);
            AddField(so, e, nameof(LayerEffects.stroke), fx);
            AddField(so, e, nameof(LayerEffects.dropShadows), fx, "Drop Shadows");
            AddField(so, e, nameof(LayerEffects.outerGlow), fx, "Outer Glow");
            AddField(so, e, nameof(LayerEffects.innerShadow), fx, "Inner Shadow");
            AddField(so, e, nameof(LayerEffects.innerGlow), fx, "Inner Glow");

            _scroll.Bind(so);
        }

        public void ShowMessage(string message)
        {
            _scroll.Clear();
            _scroll.Add(new HelpBox(message, HelpBoxMessageType.Info));
        }

        static VisualElement Header(string title, string subtitle)
        {
            var header = new VisualElement { style = { marginTop = 6, marginBottom = 4 } };
            header.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13 } });
            if (subtitle != null)
                header.Add(new Label(subtitle) { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f } });
            return header;
        }

        Foldout Section(string title)
        {
            var foldout = new Foldout { text = title, value = true, style = { marginTop = 4 } };
            foldout.Q<Toggle>().style.unityFontStyleAndWeight = FontStyle.Bold;
            _scroll.Add(foldout);
            return foldout;
        }

        void AddField(SerializedObject so, string path, string field, VisualElement parent = null, string label = null)
        {
            var prop = so.FindProperty(path + "." + field);
            if (prop == null)
            {
                Debug.LogWarning($"[UISpriteMaker] missing property {path}.{field}");
                return;
            }
            var pf = label != null ? new PropertyField(prop, label) : new PropertyField(prop);
            (parent ?? _scroll).Add(pf);
        }
    }
}
