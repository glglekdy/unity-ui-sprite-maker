using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UIImage = UnityEngine.UI.Image;

namespace UISpriteMaker.Editor
{
    public sealed class UISpriteMakerWindow : EditorWindow
    {
        const long RenderDebounceMs = 50;

        [SerializeField] UISpriteStyle _style;
        [SerializeField] string _assetPath;
        [SerializeField] float _zoom = 1f;
        [SerializeField] bool _showGuides = true;

        SerializedObject _serializedStyle;
        RasterResult _result;
        Texture2D _preview;
        Texture2D _checker;
        IVisualElementScheduledItem _pendingRender;

        ScrollView _inspectorHost;
        ObjectField _presetField;
        VisualElement _canvas;
        Image _image;
        readonly List<VisualElement> _guides = new List<VisualElement>();
        Label _info;
        Label _pathLabel;
        HelpBox _gradientWarning;
        Button _overwriteButton;

        [MenuItem("Tools/UI Sprite Maker")]
        public static UISpriteMakerWindow Open()
        {
            var window = GetWindow<UISpriteMakerWindow>();
            window.titleContent = new GUIContent("UI Sprite Maker");
            window.minSize = new Vector2(720, 420);
            return window;
        }

        [MenuItem("Assets/UI Sprite Maker/Edit in Sprite Maker", true)]
        static bool CanEditSelected()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) && SpriteExporter.HasStyle(path);
        }

        [MenuItem("Assets/UI Sprite Maker/Edit in Sprite Maker")]
        static void EditSelected() => Open().LoadFromAsset(AssetDatabase.GetAssetPath(Selection.activeObject));

        public void LoadFromAsset(string assetPath)
        {
            EnsureStyle();
            Undo.RecordObject(_style, "Load UI Sprite Style");
            if (!SpriteExporter.TryLoadStyle(assetPath, _style)) return;
            _assetPath = assetPath;
            RebuildInspector();
            UpdatePathState();
            ScheduleRender();
        }

        void OnEnable()
        {
            EnsureStyle();
            Undo.undoRedoPerformed += ScheduleRender;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= ScheduleRender;
            DestroyTexture(ref _preview);
            DestroyTexture(ref _checker);
        }

        void OnDestroy()
        {
            if (_style != null && !EditorUtility.IsPersistent(_style))
                DestroyImmediate(_style);
        }

        void EnsureStyle()
        {
            if (_style != null) return;
            _style = CreateInstance<UISpriteStyle>();
            _style.name = "Working Style";
            _style.hideFlags = HideFlags.DontSave;
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Row;

            // ----- Left: style settings -----
            var left = new VisualElement();
            left.style.width = 380;
            left.style.minWidth = 300;
            left.style.borderRightWidth = 1;
            left.style.borderRightColor = new Color(0f, 0f, 0f, 0.3f);
            root.Add(left);

            var presetRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingRight = 4, paddingTop = 4 } };
            _presetField = new ObjectField("Preset") { objectType = typeof(UISpriteStyle), allowSceneObjects = false };
            _presetField.style.flexGrow = 1;
            _presetField.labelElement.style.minWidth = 50;
            presetRow.Add(_presetField);
            presetRow.Add(new Button(LoadPreset) { text = "Load" });
            presetRow.Add(new Button(SavePreset) { text = "Save As…" });
            left.Add(presetRow);

            var specRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingRight = 4, paddingBottom = 4 } };
            specRow.Add(new Label("Spec JSON") { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } });
            specRow.Add(new Button(CopySpec) { text = "Copy", tooltip = "Copy the style as a JSON sprite spec (for scripts / AI agents)." });
            specRow.Add(new Button(PasteSpec) { text = "Paste", tooltip = "Load a JSON sprite spec from the clipboard." });
            left.Add(specRow);

            _inspectorHost = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            left.Add(_inspectorHost);
            RebuildInspector();

            // ----- Right: preview + actions -----
            var right = new VisualElement { style = { flexGrow = 1, paddingLeft = 6, paddingRight = 6, paddingTop = 4, paddingBottom = 6 } };
            root.Add(right);

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var zoom = new Slider("Zoom", 0.25f, 4f) { value = _zoom, showInputField = true };
            zoom.style.flexGrow = 1;
            zoom.RegisterValueChangedCallback(e => { _zoom = e.newValue; LayoutPreview(); });
            toolbar.Add(zoom);
            var guides = new Toggle("9-Slice Guides") { value = _showGuides };
            guides.RegisterValueChangedCallback(e => { _showGuides = e.newValue; LayoutPreview(); });
            toolbar.Add(guides);
            right.Add(toolbar);

            var viewport = new VisualElement
            {
                style =
                {
                    flexGrow = 1, marginTop = 4, marginBottom = 4, overflow = Overflow.Hidden,
                    alignItems = Align.Center, justifyContent = Justify.Center,
                    backgroundColor = new Color(0.16f, 0.16f, 0.16f),
                },
            };
            right.Add(viewport);

            _checker = CreateCheckerTexture();
            _canvas = new VisualElement();
            _canvas.style.backgroundImage = _checker;
            _canvas.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
            _canvas.style.backgroundSize = new BackgroundSize(16, 16);
            _canvas.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            _canvas.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
            viewport.Add(_canvas);

            _image = new Image { scaleMode = ScaleMode.StretchToFill };
            _image.style.position = Position.Absolute;
            _image.style.left = _image.style.top = _image.style.right = _image.style.bottom = 0;
            _canvas.Add(_image);

            for (int i = 0; i < 4; i++)
            {
                var line = new VisualElement { pickingMode = PickingMode.Ignore };
                line.style.position = Position.Absolute;
                line.style.backgroundColor = new Color(0f, 1f, 0.4f, 0.8f);
                _guides.Add(line);
                _canvas.Add(line);
            }

            _info = new Label();
            right.Add(_info);
            _gradientWarning = new HelpBox(
                "9-slice와 그라데이션을 함께 쓰면 늘어날 때 그라데이션이 고르게 늘어나지 않습니다. 크기를 고정해서 쓰거나 Image Type을 Simple로 사용하세요.",
                HelpBoxMessageType.Warning);
            right.Add(_gradientWarning);

            _pathLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Italic, marginTop = 2 } };
            right.Add(_pathLabel);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            buttons.Add(new Button(ExportAs) { text = "Export PNG…" });
            _overwriteButton = new Button(() => ExportTo(_assetPath)) { text = "Overwrite" };
            buttons.Add(_overwriteButton);
            buttons.Add(new Button(ApplyToSelection) { text = "Save & Apply to Selected Image" });
            right.Add(buttons);

            UpdatePathState();
            RenderPreview();
        }

        void RebuildInspector()
        {
            if (_inspectorHost == null) return;
            _inspectorHost.Clear();

            var fields = new VisualElement { style = { paddingLeft = 4, paddingRight = 4 } };
            _serializedStyle = new SerializedObject(_style);
            var it = _serializedStyle.GetIterator();
            it.NextVisible(true);
            do
            {
                if (it.propertyPath == "m_Script") continue;
                fields.Add(new PropertyField(it.Copy()));
            } while (it.NextVisible(false));

            fields.Bind(_serializedStyle);
            fields.TrackSerializedObjectValue(_serializedStyle, _ => ScheduleRender());
            _inspectorHost.Add(fields);
        }

        void ScheduleRender()
        {
            if (_canvas == null) return;
            _pendingRender?.Pause();
            _pendingRender = rootVisualElement.schedule.Execute(RenderPreview).StartingIn(RenderDebounceMs);
        }

        void RenderPreview()
        {
            if (_canvas == null || _style == null) return;

            _result = SpriteRasterizer.Render(_style);
            DestroyTexture(ref _preview);
            _preview = _result.ToTexture();
            _preview.hideFlags = HideFlags.HideAndDontSave;
            _image.image = _preview;

            var b = _result.Border;
            _info.text = $"{_result.Width} × {_result.Height} px (@{_result.Scale}x)    " +
                         (_style.nineSlice ? $"Border L{b.x} B{b.y} R{b.z} T{b.w}" : "9-slice off");
            _gradientWarning.style.display = _style.nineSlice && SpriteRasterizer.HasNonUniformFill(_style)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            LayoutPreview();
        }

        void LayoutPreview()
        {
            if (_result == null || _canvas == null) return;

            // Display at logical size (pixels / scale) times zoom.
            float k = _zoom / _result.Scale;
            _canvas.style.width = _result.Width * k;
            _canvas.style.height = _result.Height * k;

            var b = _result.Border;
            bool show = _showGuides && _style.nineSlice;
            PlaceVertical(_guides[0], b.x * k);
            PlaceVertical(_guides[1], (_result.Width - b.z) * k);
            PlaceHorizontal(_guides[2], b.w * k); // UI Toolkit y goes down: top border first
            PlaceHorizontal(_guides[3], (_result.Height - b.y) * k);
            foreach (var g in _guides)
                g.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

            static void PlaceVertical(VisualElement e, float x)
            {
                e.style.left = x;
                e.style.top = 0;
                e.style.bottom = 0;
                e.style.width = 1;
            }

            static void PlaceHorizontal(VisualElement e, float y)
            {
                e.style.top = y;
                e.style.left = 0;
                e.style.right = 0;
                e.style.height = 1;
            }
        }

        void UpdatePathState()
        {
            if (_pathLabel == null) return;
            bool hasPath = !string.IsNullOrEmpty(_assetPath);
            _pathLabel.text = hasPath ? $"Editing: {_assetPath}" : "Not saved yet";
            _overwriteButton.SetEnabled(hasPath);
        }

        void LoadPreset()
        {
            if (_presetField.value is not UISpriteStyle preset) return;
            Undo.RecordObject(_style, "Load UI Sprite Preset");
            _style.CopyFrom(preset);
            RebuildInspector();
            ScheduleRender();
        }

        void CopySpec()
        {
            EditorGUIUtility.systemCopyBuffer = SpriteSpec.ToJson(_style);
            ShowNotification(new GUIContent("Spec JSON copied"));
        }

        void PasteSpec()
        {
            UISpriteStyle parsed;
            try
            {
                parsed = SpriteSpec.Parse(EditorGUIUtility.systemCopyBuffer);
            }
            catch (SpriteSpecException e)
            {
                EditorUtility.DisplayDialog("Invalid sprite spec", e.Message, "OK");
                return;
            }

            Undo.RecordObject(_style, "Paste UI Sprite Spec");
            _style.CopyFrom(parsed);
            DestroyImmediate(parsed);
            RebuildInspector();
            ScheduleRender();
        }

        void SavePreset()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save UI Sprite Preset", "UISpriteStyle", "asset", "Save the current style as a preset.");
            if (string.IsNullOrEmpty(path)) return;

            var preset = CreateInstance<UISpriteStyle>();
            preset.CopyFrom(_style);
            preset.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            _presetField.value = preset;
            EditorGUIUtility.PingObject(preset);
        }

        void ExportAs()
        {
            var path = EditorUtility.SaveFilePanelInProject("Export UI Sprite", "UISprite", "png", "Save the sprite as a PNG.");
            if (!string.IsNullOrEmpty(path)) ExportTo(path);
        }

        void ExportTo(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _result = SpriteExporter.Export(_style, path);
            _assetPath = path;
            UpdatePathState();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Sprite>(path));
        }

        void ApplyToSelection()
        {
            var images = new List<UIImage>();
            foreach (var go in Selection.gameObjects)
                if (go.TryGetComponent(out UIImage img)) images.Add(img);

            if (images.Count == 0)
            {
                ShowNotification(new GUIContent("Select a GameObject with a UI Image."));
                return;
            }

            if (string.IsNullOrEmpty(_assetPath)) ExportAs();
            else ExportTo(_assetPath);
            if (string.IsNullOrEmpty(_assetPath)) return;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(_assetPath);
            var layout = SpriteRasterizer.ComputeLayout(_style);
            foreach (var img in images)
                UISpriteMakerApi.ApplyToImage(img, sprite, layout);
        }

        static Texture2D CreateCheckerTexture()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var light = new Color32(0x6A, 0x6A, 0x6A, 0xFF);
            var dark = new Color32(0x4E, 0x4E, 0x4E, 0xFF);
            tex.SetPixels32(new[] { light, dark, dark, light });
            tex.Apply(false);
            return tex;
        }

        static void DestroyTexture(ref Texture2D tex)
        {
            if (tex != null) DestroyImmediate(tex);
            tex = null;
        }
    }
}
