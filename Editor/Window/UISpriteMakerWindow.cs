using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UIImage = UnityEngine.UI.Image;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Figma-like editor: layer tree (left), canvas with direct manipulation (center), properties (right).
    /// </summary>
    public sealed class UISpriteMakerWindow : EditorWindow
    {
        const long RenderDelayMs = 16;

        [SerializeField] UISpriteStyle _style;
        [SerializeField] string _assetPath;
        [SerializeField] string _prefabPath;
        [SerializeField] float _zoom = 1f;
        [SerializeField] bool _showGuides = true;
        [SerializeField] string _selectedId;

        SerializedObject _serializedStyle;
        RasterResult _result;
        Texture2D _preview;
        bool _renderQueued;
        VisualElement _tracker;
        int _dragUndoGroup = -1;
        readonly Dictionary<string, Vector2> _boxSizes = new Dictionary<string, Vector2>();
        const string FrameKey = "";

        ObjectField _presetField;
        Slider _zoomSlider;
        LayerTreePanel _tree;
        CanvasView _canvasView;
        PropertiesPanel _properties;
        Label _info;
        Label _pathLabel;
        HelpBox _warning;
        Button _overwriteButton;

        [MenuItem("Tools/UI Sprite Maker")]
        public static UISpriteMakerWindow Open()
        {
            var window = GetWindow<UISpriteMakerWindow>();
            window.titleContent = new GUIContent("UI Sprite Maker");
            window.minSize = new Vector2(980, 520);
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

        /// <summary>Loads the style a sprite or prefab was made with.</summary>
        public void LoadFromAsset(string assetPath)
        {
            EnsureStyle();
            Undo.RecordObject(_style, "Load UI Sprite Style");
            if (!SpriteExporter.TryLoadStyle(assetPath, _style)) return;
            if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) _prefabPath = assetPath;
            else _assetPath = assetPath;
            _selectedId = null;
            OnStyleReplaced();
        }

        void OnEnable()
        {
            EnsureStyle();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            DestroyTexture(ref _preview);
            _canvasView?.Dispose();
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

        // ------------------------------------------------------------------ state for the panels

        internal UISpriteStyle Style => _style;
        internal string SelectedId => _selectedId;
        internal Layer SelectedLayer => LayerTree.Find(_style.layers, _selectedId, out _, out _);

        /// <summary>Selects a layer by id (null = the frame).</summary>
        internal void Select(string id)
        {
            if (id != null && LayerTree.Find(_style.layers, id, out _, out _) == null) id = null;
            if (id == _selectedId) return;
            _selectedId = id;
            _tree?.SyncSelection();
            RebuildProperties();
            _canvasView?.Repaint();
        }

        internal void SelectParent()
        {
            if (_selectedId == null) return;
            LayerTree.Find(_style.layers, _selectedId, out _, out var parent);
            Select(parent?.id);
        }

        /// <summary>Records undo, runs <paramref name="action"/> and refreshes everything if it returned true.</summary>
        internal void Edit(string undoName, Func<bool> action, bool structural)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(_style, undoName);
            if (!action()) return;
            AfterChange(structural);
        }

        internal void BeginDrag(string undoName)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(_style, undoName);
            _dragUndoGroup = Undo.GetCurrentGroup();
        }

        internal void DragChanged()
        {
            Undo.RecordObject(_style, Undo.GetCurrentGroupName());
            AfterChange(false);
        }

        internal void EndDrag()
        {
            if (_dragUndoGroup >= 0) Undo.CollapseUndoOperations(_dragUndoGroup);
            _dragUndoGroup = -1;
        }

        void AfterChange(bool structural)
        {
            EditorUtility.SetDirty(_style);
            _serializedStyle?.Update();
            SnapshotBoxSizes();
            if (structural)
            {
                if (SelectedLayer == null) _selectedId = null;
                _tree?.Rebuild();
                RebuildProperties();
            }
            else
            {
                _tree?.RefreshRows();
            }
            ScheduleRender();
            _canvasView?.Repaint();
        }

        internal void SetZoom(float zoom)
        {
            _zoom = Mathf.Clamp(zoom, 0.25f, 8f);
            _zoomSlider?.SetValueWithoutNotify(_zoom);
            if (_canvasView != null)
            {
                _canvasView.Zoom = _zoom;
                _canvasView.LayoutCanvas();
            }
        }

        // ------------------------------------------------------------------ layer commands

        internal void AddLayer(Layer layer)
        {
            Edit("Add Layer", () =>
            {
                LayerOps.Add(_style, layer, _selectedId);
                _selectedId = layer.id;
                return true;
            }, true);
        }

        internal void DeleteSelected()
        {
            var id = _selectedId;
            if (id == null) return;
            LayerTree.Find(_style.layers, id, out _, out var parent);
            Edit("Delete Layer", () =>
            {
                if (!LayerOps.Delete(_style, id)) return false;
                _selectedId = parent?.id;
                return true;
            }, true);
        }

        internal void DuplicateSelected()
        {
            if (_selectedId == null) return;
            Edit("Duplicate Layer", () =>
            {
                var copy = LayerOps.Duplicate(_style, _selectedId);
                if (copy == null) return false;
                _selectedId = copy.id;
                return true;
            }, true);
        }

        internal void GroupSelected()
        {
            if (_selectedId == null) return;
            Edit("Group Layer", () =>
            {
                var group = LayerOps.Group(_style, _selectedId);
                if (group == null) return false;
                _selectedId = group.id;
                return true;
            }, true);
        }

        internal void ReorderSelected(int delta)
        {
            if (_selectedId != null) Edit(delta > 0 ? "Bring Forward" : "Send Backward", () => LayerOps.Reorder(_style, _selectedId, delta), true);
        }

        internal void MoveOutOfParent()
        {
            var id = _selectedId;
            if (id == null) return;
            LayerTree.Find(_style.layers, id, out _, out var parent);
            if (parent == null) return;
            LayerTree.Find(_style.layers, parent.id, out var parentOwner, out var grandParent);
            int index = parentOwner.IndexOf(parent) + 1;
            Edit("Move Out of Parent", () => LayerOps.Move(_style, id, grandParent?.id, index), true);
        }

        internal void Nudge(Vector2 delta)
        {
            var layer = SelectedLayer;
            if (layer == null || layer.locked) return;
            Edit("Nudge Layer", () =>
            {
                layer.position += delta;
                return true;
            }, false);
        }

        // ------------------------------------------------------------------ GUI

        void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            // ----- Toolbar -----
            var toolbar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4, paddingRight = 4,
                    paddingTop = 3, paddingBottom = 3, borderBottomWidth = 1, borderBottomColor = new Color(0f, 0f, 0f, 0.3f),
                },
            };
            _presetField = new ObjectField("Preset") { objectType = typeof(UISpriteStyle), allowSceneObjects = false };
            _presetField.style.width = 260;
            _presetField.labelElement.style.minWidth = 44;
            toolbar.Add(_presetField);
            toolbar.Add(new Button(LoadPreset) { text = "Load" });
            toolbar.Add(new Button(SavePreset) { text = "Save As…" });
            toolbar.Add(new VisualElement { style = { width = 12 } });
            toolbar.Add(new Label("Spec JSON") { style = { unityTextAlign = TextAnchor.MiddleLeft, marginRight = 2 } });
            toolbar.Add(new Button(CopySpec) { text = "Copy", tooltip = "Copy the style as a JSON sprite spec (for scripts / AI agents)." });
            toolbar.Add(new Button(PasteSpec) { text = "Paste", tooltip = "Load a JSON sprite spec from the clipboard." });
            toolbar.Add(new VisualElement { style = { flexGrow = 1 } });
            _zoomSlider = new Slider("Zoom", 0.25f, 8f) { value = _zoom, showInputField = true, tooltip = "Ctrl + mouse wheel on the canvas" };
            _zoomSlider.style.width = 240;
            _zoomSlider.labelElement.style.minWidth = 40;
            _zoomSlider.RegisterValueChangedCallback(e => SetZoom(e.newValue));
            toolbar.Add(_zoomSlider);
            var guides = new Toggle("9-Slice Guides") { value = _showGuides };
            guides.RegisterValueChangedCallback(e =>
            {
                _showGuides = e.newValue;
                _canvasView.ShowGuides = e.newValue;
                _canvasView.LayoutCanvas();
            });
            toolbar.Add(guides);
            root.Add(toolbar);

            // ----- Panes: layers | canvas | properties -----
            var outer = new TwoPaneSplitView(0, 230, TwoPaneSplitViewOrientation.Horizontal) { style = { flexGrow = 1 } };
            var inner = new TwoPaneSplitView(1, 340, TwoPaneSplitViewOrientation.Horizontal);
            root.Add(outer);

            _tree = new LayerTreePanel(this);
            outer.Add(_tree);
            outer.Add(inner);

            var center = new VisualElement { style = { flexGrow = 1, paddingLeft = 4, paddingRight = 4, paddingBottom = 4 } };
            _canvasView = new CanvasView(this) { Zoom = _zoom, ShowGuides = _showGuides };
            center.Add(_canvasView);

            _info = new Label { style = { marginTop = 2 } };
            center.Add(_info);
            _warning = new HelpBox("", HelpBoxMessageType.Warning);
            center.Add(_warning);
            _pathLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Italic, marginTop = 2 } };
            center.Add(_pathLabel);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
            buttons.Add(new Button(ExportAs) { text = "Export PNG…" });
            _overwriteButton = new Button(() => ExportTo(_assetPath)) { text = "Overwrite" };
            buttons.Add(_overwriteButton);
            buttons.Add(new Button(ApplyToSelection) { text = "Save & Apply to Selected Image" });
            buttons.Add(new Button(ExportPrefab)
            {
                text = "Export Prefab…",
                tooltip = "Build a UGUI prefab: text and image layers become their own TextMeshPro/Image objects.",
            });
            center.Add(buttons);
            inner.Add(center);

            _properties = new PropertiesPanel(this);
            inner.Add(_properties);

            OnStyleReplaced();
        }

        /// <summary>Everything changed (load, paste, undo): rebind and rebuild all panels.</summary>
        void OnStyleReplaced()
        {
            if (_tree == null) return;
            _serializedStyle = new SerializedObject(_style);
            // One tracker per SerializedObject: replace it so reloads don't stack callbacks.
            _tracker?.RemoveFromHierarchy();
            _tracker = new VisualElement { style = { display = DisplayStyle.None } };
            rootVisualElement.Add(_tracker);
            _tracker.TrackSerializedObjectValue(_serializedStyle, _ => OnInspectorChanged());
            if (SelectedLayer == null) _selectedId = null;
            SnapshotBoxSizes();
            _tree.Rebuild();
            RebuildProperties();
            UpdatePathState();
            RenderPreview();
        }

        void RebuildProperties()
        {
            if (_properties == null || _serializedStyle == null) return;
            var layer = SelectedLayer;
            if (layer == null)
            {
                _properties.ShowFrame(_serializedStyle);
                return;
            }
            string path = PropertyPath(_style.layers, layer, nameof(UISpriteStyle.layers));
            if (path == null) _properties.ShowMessage("Layer not found.");
            else _properties.ShowLayer(_serializedStyle, path, layer);
        }

        static string PropertyPath(List<Layer> list, Layer target, string listPath)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var layer = list[i];
                if (layer == null) continue;
                string path = $"{listPath}.Array.data[{i}]";
                if (layer == target) return path;
                if (layer.Children != null)
                {
                    var found = PropertyPath(layer.Children, target, path + ".children");
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>A field in the properties panel changed.</summary>
        void OnInspectorChanged()
        {
            // A box resized from the inspector: let its children follow their constraints.
            bool resized = false;
            var frameSize = (Vector2)_style.shape.size;
            if (_boxSizes.TryGetValue(FrameKey, out var oldFrame) && oldFrame != frameSize)
            {
                Undo.RecordObject(_style, "Resize Frame");
                LayerOps.ApplyConstraints(_style.layers, oldFrame, frameSize);
                resized = true;
            }
            foreach (var layer in LayerTree.Walk(_style.layers))
            {
                if (layer.Children == null || !_boxSizes.TryGetValue(layer.id, out var old) || old == layer.size) continue;
                Undo.RecordObject(_style, "Resize Layer");
                LayerOps.ApplyConstraints(layer.Children, old, layer.size);
                resized = true;
            }
            SnapshotBoxSizes();
            if (resized)
            {
                EditorUtility.SetDirty(_style);
                _serializedStyle.Update();
            }
            _tree?.RefreshRows();
            ScheduleRender();
            _canvasView?.Repaint();
        }

        void SnapshotBoxSizes()
        {
            _boxSizes.Clear();
            _boxSizes[FrameKey] = _style.shape.size;
            foreach (var layer in LayerTree.Walk(_style.layers))
                if (layer.Children != null) _boxSizes[layer.id] = layer.size;
        }

        void OnUndoRedo()
        {
            if (_tree == null) return;
            _serializedStyle?.Update();
            if (SelectedLayer == null) _selectedId = null;
            SnapshotBoxSizes();
            _tree.Rebuild();
            RebuildProperties();
            ScheduleRender();
        }

        // ------------------------------------------------------------------ rendering

        void ScheduleRender()
        {
            if (_canvasView == null) return;
            // Throttle rather than debounce, so dragging keeps updating the preview.
            if (_renderQueued) return;
            _renderQueued = true;
            rootVisualElement.schedule.Execute(RenderPreview).StartingIn(RenderDelayMs);
        }

        void RenderPreview()
        {
            _renderQueued = false;
            if (_canvasView == null || _style == null) return;

            _result = SpriteRasterizer.Render(_style);
            DestroyTexture(ref _preview);
            _preview = _result.ToTexture();
            _preview.hideFlags = HideFlags.HideAndDontSave;
            _canvasView.SetResult(_result, _preview);

            var layout = SpriteRasterizer.ComputeLayout(_style);
            var b = _result.Border;
            _info.text = $"{_result.Width} × {_result.Height} px (@{_result.Scale}x)    " +
                         (_style.nineSlice ? $"Border L{b.x} B{b.y} R{b.z} T{b.w}" : "9-slice off") +
                         $"    Layers {LayerTree.Walk(_style.layers).Count()}";

            var warnings = new List<string>(_result.Warnings);
            if (_style.nineSlice && SpriteRasterizer.HasNonUniformFill(_style))
                warnings.Add("9-slice와 그라데이션을 함께 쓰면 늘어날 때 그라데이션이 고르게 늘어나지 않습니다. 크기를 고정해서 쓰거나 Image Type을 Simple로 사용하세요.");
            if (_style.nineSlice && (layout.SliceBlockedX || layout.SliceBlockedY))
                warnings.Add("레이어가 9-slice로 늘어나는 영역을 가리고 있어 늘리면 레이어가 찌그러집니다. 디자인한 크기 그대로 쓰거나, 늘어나야 하는 레이어의 Constraints를 Stretch로 하거나, Export Prefab을 사용하세요.");
            _warning.text = string.Join("\n", warnings);
            _warning.style.display = warnings.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void UpdatePathState()
        {
            if (_pathLabel == null) return;
            bool hasPath = !string.IsNullOrEmpty(_assetPath);
            var parts = new List<string>();
            if (hasPath) parts.Add($"PNG: {_assetPath}");
            if (!string.IsNullOrEmpty(_prefabPath)) parts.Add($"Prefab: {_prefabPath}");
            _pathLabel.text = parts.Count > 0 ? "Editing  " + string.Join("   ", parts) : "Not saved yet";
            _overwriteButton.SetEnabled(hasPath);
        }

        // ------------------------------------------------------------------ presets & spec

        void LoadPreset()
        {
            if (_presetField.value is not UISpriteStyle preset) return;
            Undo.RecordObject(_style, "Load UI Sprite Preset");
            _style.CopyFrom(preset);
            _selectedId = null;
            OnStyleReplaced();
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
            _selectedId = null;
            OnStyleReplaced();
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

        // ------------------------------------------------------------------ export

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

        void ExportPrefab()
        {
            string suggested = string.IsNullOrEmpty(_prefabPath) ? "UISprite" : System.IO.Path.GetFileNameWithoutExtension(_prefabPath);
            var path = EditorUtility.SaveFilePanelInProject("Export UI Prefab", suggested, "prefab",
                "Save the layers as a UGUI prefab. Sprites are written to a <Name>_Sprites folder next to it.");
            if (string.IsNullOrEmpty(path)) return;

            var result = UISpriteMakerApi.BakePrefab(_style, path);
            _prefabPath = result.PrefabPath;
            UpdatePathState();
            foreach (var w in result.Warnings) Debug.LogWarning($"[UISpriteMaker] {result.PrefabPath}: {w}");
            ShowNotification(new GUIContent(result.Warnings.Count == 0
                ? $"Prefab saved ({result.Objects} objects)"
                : $"Prefab saved with {result.Warnings.Count} warning(s), see Console"));
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath));
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

        static void DestroyTexture(ref Texture2D tex)
        {
            if (tex != null) DestroyImmediate(tex);
            tex = null;
        }
    }
}
