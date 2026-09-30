using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Center pane: the rendered sprite plus an overlay to select, move, resize and rotate layers (Figma-like).
    /// Click selects a top-level layer, double-click goes one level deeper, Ctrl/Cmd+click picks the deepest.
    /// Ctrl+wheel zooms, arrows nudge, Shift constrains/snaps, Alt resizes from the center.
    /// </summary>
    sealed class CanvasView : VisualElement
    {
        const float HandleSize = 7f;
        const float HandleHit = 6f;
        const float RotateHit = 18f;
        const float SnapDistance = 5f;

        static readonly Color Accent = new Color(0.18f, 0.55f, 1f, 1f);
        static readonly Color HoverColor = new Color(0.18f, 0.55f, 1f, 0.55f);
        static readonly Color SnapColor = new Color(1f, 0.25f, 0.6f, 0.9f);
        static readonly Color GuideColor = new Color(0f, 1f, 0.4f, 0.8f);

        // Handles, clockwise from top-left: direction of each in the layer's frame (x right, y down).
        static readonly Vector2Int[] HandleDirs =
        {
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(1, 0),
            new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1), new Vector2Int(-1, 0),
        };

        enum DragMode { None, Move, Resize, Rotate }

        readonly UISpriteMakerWindow _host;
        readonly VisualElement _canvas;
        readonly Image _image;
        readonly VisualElement _overlay;
        readonly List<VisualElement> _guides = new List<VisualElement>();
        Texture2D _checker;
        RasterResult _result;

        DragMode _drag;
        int _handle;
        Vector2 _startMouse;
        Vector2 _startPos, _startSize;
        float _startRotation, _startAngle, _parentAngle;
        Vector2 _rotateCenter;
        Vector2Int _startFrameSize;
        string _hoverId;
        readonly List<(Vector2 a, Vector2 b)> _snapLines = new List<(Vector2, Vector2)>();

        public float Zoom = 1f;
        public bool ShowGuides = true;

        public CanvasView(UISpriteMakerWindow host)
        {
            _host = host;
            style.flexGrow = 1;
            style.overflow = Overflow.Hidden;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);

            _checker = CreateCheckerTexture();
            _canvas = new VisualElement { pickingMode = PickingMode.Ignore };
            _canvas.style.backgroundImage = _checker;
            _canvas.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
            _canvas.style.backgroundSize = new BackgroundSize(16, 16);
            _canvas.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            _canvas.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);
            Add(_canvas);

            _image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _image.style.position = Position.Absolute;
            _image.style.left = _image.style.top = _image.style.right = _image.style.bottom = 0;
            _canvas.Add(_image);

            for (int i = 0; i < 4; i++)
            {
                var line = new VisualElement { pickingMode = PickingMode.Ignore };
                line.style.position = Position.Absolute;
                line.style.backgroundColor = GuideColor;
                _guides.Add(line);
                _canvas.Add(line);
            }

            _overlay = new VisualElement { focusable = true, pickingMode = PickingMode.Position };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = _overlay.style.top = _overlay.style.right = _overlay.style.bottom = 0;
            _overlay.generateVisualContent += DrawOverlay;
            Add(_overlay);

            _overlay.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _overlay.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _overlay.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _overlay.RegisterCallback<PointerLeaveEvent>(_ => SetHover(null));
            _overlay.RegisterCallback<WheelEvent>(OnWheel);
            _overlay.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _overlay.RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            _overlay.RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);
            _canvas.RegisterCallback<GeometryChangedEvent>(_ => _overlay.MarkDirtyRepaint());
        }

        public void Dispose()
        {
            if (_checker != null) Object.DestroyImmediate(_checker);
            _checker = null;
        }

        public void SetResult(RasterResult result, Texture2D texture)
        {
            _result = result;
            _image.image = texture;
            LayoutCanvas();
        }

        public void Repaint() => _overlay.MarkDirtyRepaint();

        public void LayoutCanvas()
        {
            if (_result == null) return;

            // Display at logical size (pixels / scale) times zoom.
            float k = Zoom / _result.Scale;
            _canvas.style.width = _result.Width * k;
            _canvas.style.height = _result.Height * k;

            var b = _result.Border;
            bool show = ShowGuides && _host.Style.nineSlice;
            PlaceVertical(_guides[0], b.x * k);
            PlaceVertical(_guides[1], (_result.Width - b.z) * k);
            PlaceHorizontal(_guides[2], b.w * k); // UI Toolkit y goes down: top border first
            PlaceHorizontal(_guides[3], (_result.Height - b.y) * k);
            foreach (var g in _guides)
                g.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _overlay.MarkDirtyRepaint();

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

        // ------------------------------------------------------------------ coordinates

        float PixelsToDisplay => _result != null ? Zoom / _result.Scale : 1f;

        /// <summary>Canvas pixel (y up) → overlay point.</summary>
        Vector2 ToOverlay(Vector2 canvasPx)
        {
            float k = PixelsToDisplay;
            return _canvas.ChangeCoordinatesTo(_overlay, new Vector2(canvasPx.x * k, (_result.Height - canvasPx.y) * k));
        }

        /// <summary>Overlay point → canvas pixel (y up).</summary>
        Vector2 ToCanvas(Vector2 overlayPoint)
        {
            float k = PixelsToDisplay;
            var local = _overlay.ChangeCoordinatesTo(_canvas, overlayPoint);
            return new Vector2(local.x / k, _result.Height - local.y / k);
        }

        Dictionary<string, Placement> Placements() =>
            LayerGeometry.Placements(_host.Style, _result.ShapeRect, _result.Scale);

        Placement FramePlacement() => Placement.Root(_result.ShapeRect);

        /// <summary>Placement of the selected layer, or of the frame when nothing is selected.</summary>
        bool SelectedPlacement(out Placement p, out Layer layer)
        {
            layer = _host.SelectedLayer;
            if (layer == null)
            {
                p = FramePlacement();
                return true;
            }
            return Placements().TryGetValue(layer.id, out p);
        }

        static bool Inside(Placement p, Vector2 canvasPoint)
        {
            p.ToLocal(canvasPoint.x, canvasPoint.y, out float lx, out float ly);
            return Mathf.Abs(lx) <= p.Hw && Mathf.Abs(ly) <= p.Hh;
        }

        /// <summary>
        /// Layers under a canvas point, from the top-level layer down to the deepest one
        /// (topmost first among siblings; groups are hit through their children; hidden/locked skipped).
        /// </summary>
        List<Layer> HitChain(Vector2 canvasPoint)
        {
            var placements = Placements();
            var chain = new List<Layer>();
            Hit(_host.Style.layers);
            return chain;

            bool Hit(List<Layer> layers)
            {
                for (int i = layers.Count - 1; i >= 0; i--)
                {
                    var layer = layers[i];
                    if (layer == null || !layer.visible || layer.locked) continue;
                    var p = placements[layer.id];
                    chain.Add(layer);
                    if (layer.Children != null && (!layer.Clips || Inside(p, canvasPoint)) && Hit(layer.Children)) return true;
                    if (!(layer is GroupLayer) && Inside(p, canvasPoint)) return true;
                    chain.RemoveAt(chain.Count - 1);
                }
                return false;
            }
        }

        /// <summary>
        /// Figma-style click target: the top-level layer, or a sibling of the current selection when working
        /// inside a container. Clicking the selection (or anything inside it) keeps it. Deep = deepest layer.
        /// </summary>
        Layer PickTarget(List<Layer> chain, bool deep, bool drillIn)
        {
            if (chain.Count == 0) return null;
            if (deep) return chain[^1];
            var selected = _host.SelectedLayer;
            int index = selected != null ? chain.IndexOf(selected) : -1;
            if (index >= 0) return drillIn && index + 1 < chain.Count ? chain[index + 1] : selected;
            if (selected != null)
            {
                // Stay at the selection's depth when the click lands inside the same parent.
                LayerTree.Find(_host.Style.layers, selected.id, out _, out var parent);
                int parentIndex = parent != null ? chain.IndexOf(parent) : -1;
                if (parentIndex >= 0 && parentIndex + 1 < chain.Count) return chain[parentIndex + 1];
            }
            return chain[0];
        }


        Vector2 HandlePoint(Placement p, int i)
        {
            var d = HandleDirs[i];
            return ToOverlay(p.ToCanvas(d.x * p.Hw, -d.y * p.Hh));
        }

        int HandleAt(Vector2 overlayPoint, Placement p)
        {
            for (int i = 0; i < HandleDirs.Length; i++)
                if ((HandlePoint(p, i) - overlayPoint).sqrMagnitude <= HandleHit * HandleHit) return i;
            return -1;
        }

        bool InRotateZone(Vector2 overlayPoint, Placement p)
        {
            if (Inside(p, ToCanvas(overlayPoint))) return false;
            for (int i = 0; i < HandleDirs.Length; i += 2)
            {
                float d = (HandlePoint(p, i) - overlayPoint).magnitude;
                if (d > HandleHit && d <= RotateHit) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ pointer

        void OnPointerDown(PointerDownEvent e)
        {
            if (_result == null || e.button != 0) return;
            _overlay.Focus();
            Vector2 pos = e.localPosition;

            var selected = _host.SelectedLayer;
            bool canEditSelection = selected == null || !selected.locked;
            if (canEditSelection && SelectedPlacement(out var sp, out _))
            {
                int handle = HandleAt(pos, sp);
                if (handle >= 0)
                {
                    BeginDrag(DragMode.Resize, pos, e.pointerId, handle);
                    e.StopPropagation();
                    return;
                }
                if (selected != null && InRotateZone(pos, sp))
                {
                    BeginDrag(DragMode.Rotate, pos, e.pointerId, -1);
                    e.StopPropagation();
                    return;
                }
            }

            // Click: top-level layer (or the selection's sibling). Double-click: one level deeper. Ctrl/Cmd+click: deepest.
            var hit = PickTarget(HitChain(ToCanvas(pos)), e.actionKey, e.clickCount >= 2);
            _host.Select(hit?.id);
            if (hit != null) BeginDrag(DragMode.Move, pos, e.pointerId, -1);
            e.StopPropagation();
        }

        void BeginDrag(DragMode mode, Vector2 pos, int pointerId, int handle)
        {
            var layer = _host.SelectedLayer;
            _drag = mode;
            _handle = handle;
            _startMouse = pos;
            _startFrameSize = _host.Style.shape.size;
            if (layer != null)
            {
                _startPos = layer.position;
                _startSize = layer.size;
                _startRotation = layer.rotation;
                var placements = Placements();
                LayerTree.Find(_host.Style.layers, layer.id, out _, out var parent);
                _parentAngle = parent != null ? placements[parent.id].Angle : 0f;
                var p = placements[layer.id];
                _rotateCenter = ToOverlay(new Vector2(p.Cx, p.Cy));
                var v = pos - _rotateCenter;
                _startAngle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            }
            _overlay.CapturePointer(pointerId);
            _host.BeginDrag(mode switch
            {
                DragMode.Resize => layer == null ? "Resize Frame" : "Resize Layer",
                DragMode.Rotate => "Rotate Layer",
                _ => "Move Layer",
            });
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (_result == null) return;
            Vector2 pos = e.localPosition;
            if (_drag == DragMode.None)
            {
                var hit = PickTarget(HitChain(ToCanvas(pos)), e.actionKey, false);
                SetHover(hit?.id);
                return;
            }
            if (!_overlay.HasPointerCapture(e.pointerId)) return;

            var layer = _host.SelectedLayer;
            Vector2 delta = (pos - _startMouse) / Mathf.Max(0.01f, Zoom); // UI units, y down
            _snapLines.Clear();
            switch (_drag)
            {
                case DragMode.Move when layer != null:
                    Move(layer, delta, e.shiftKey, e.actionKey);
                    break;
                case DragMode.Resize when layer != null:
                    ResizeLayer(layer, delta, e.shiftKey, e.altKey);
                    break;
                case DragMode.Resize:
                    ResizeFrame(delta, e.shiftKey, e.altKey);
                    break;
                case DragMode.Rotate when layer != null:
                    var v = pos - _rotateCenter;
                    float angle = _startRotation + Mathf.DeltaAngle(_startAngle, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                    if (e.shiftKey) angle = Mathf.Round(angle / 15f) * 15f;
                    layer.rotation = Mathf.Round(Mathf.DeltaAngle(0f, angle) * 10f) / 10f;
                    break;
            }
            _host.DragChanged();
            _overlay.MarkDirtyRepaint();
            e.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (_drag == DragMode.None || !_overlay.HasPointerCapture(e.pointerId)) return;
            _overlay.ReleasePointer(e.pointerId);
            _drag = DragMode.None;
            _snapLines.Clear();
            _host.EndDrag();
            _overlay.MarkDirtyRepaint();
            e.StopPropagation();
        }

        void SetHover(string id)
        {
            if (_hoverId == id) return;
            _hoverId = id;
            _overlay.MarkDirtyRepaint();
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            // Clockwise on screen (y down).
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        void Move(Layer layer, Vector2 delta, bool constrain, bool noSnap)
        {
            if (constrain)
            {
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) delta.y = 0f;
                else delta.x = 0f;
            }
            var pos = _startPos + Rotate(delta, -_parentAngle);
            pos = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));

            // Snap edges and centers to the parent's edges and center.
            bool axisAligned = Mathf.Approximately(Mathf.Repeat(_parentAngle, 360f), 0f) && Mathf.Approximately(Mathf.Repeat(layer.rotation, 360f), 0f);
            if (!noSnap && axisAligned)
            {
                LayerTree.Find(_host.Style.layers, layer.id, out _, out var parent);
                var box = LayerOps.ParentSize(_host.Style, parent);
                float threshold = SnapDistance / Mathf.Max(0.01f, Zoom);
                if (Snap(pos.x, layer.size.x, box.x, threshold, out float sx, out float gx))
                {
                    pos.x = sx;
                    AddSnapLine(parent, new Vector2(gx, 0f), new Vector2(gx, box.y));
                }
                if (Snap(pos.y, layer.size.y, box.y, threshold, out float sy, out float gy))
                {
                    pos.y = sy;
                    AddSnapLine(parent, new Vector2(0f, gy), new Vector2(box.x, gy));
                }
            }
            layer.position = pos;
        }

        static bool Snap(float start, float size, float box, float threshold, out float snapped, out float guide)
        {
            snapped = start;
            guide = 0f;
            float best = threshold;
            bool found = false;
            float[] targets = { 0f, box * 0.5f, box };
            float[] offsets = { 0f, size * 0.5f, size };
            foreach (float t in targets)
            foreach (float o in offsets)
            {
                float d = Mathf.Abs(start + o - t);
                if (d > best) continue;
                best = d;
                snapped = t - o;
                guide = t;
                found = true;
            }
            return found;
        }

        /// <summary>Adds a guide line given in the parent's box coordinates (units, y down).</summary>
        void AddSnapLine(Layer parent, Vector2 a, Vector2 b)
        {
            var p = parent != null ? Placements()[parent.id] : FramePlacement();
            float s = _result.Scale;
            Vector2 ToOverlayPoint(Vector2 u) => ToOverlay(p.ToCanvas(u.x * s - p.Hw, p.Hh - u.y * s));
            _snapLines.Add((ToOverlayPoint(a), ToOverlayPoint(b)));
        }

        void ResizeLayer(Layer layer, Vector2 delta, bool keepAspect, bool fromCenter)
        {
            var dir = HandleDirs[_handle];
            float totalAngle = _parentAngle + _startRotation;
            var local = Rotate(delta, -totalAngle); // mouse movement in the layer's own frame

            float l = 0f, t = 0f, r = _startSize.x, b = _startSize.y;
            if (dir.x < 0) l += local.x;
            if (dir.x > 0) r += local.x;
            if (dir.y < 0) t += local.y;
            if (dir.y > 0) b += local.y;
            if (fromCenter)
            {
                if (dir.x < 0) r -= local.x;
                if (dir.x > 0) l -= local.x;
                if (dir.y < 0) b -= local.y;
                if (dir.y > 0) t -= local.y;
            }

            float w = Mathf.Max(1f, r - l), h = Mathf.Max(1f, b - t);
            if (keepAspect && dir.x != 0 && dir.y != 0 && _startSize.x > 0f && _startSize.y > 0f)
            {
                float k = Mathf.Max(w / _startSize.x, h / _startSize.y);
                w = _startSize.x * k;
                h = _startSize.y * k;
            }
            w = Mathf.Max(1f, Mathf.Round(w));
            h = Mathf.Max(1f, Mathf.Round(h));

            // Recompute the moving edges from the fixed ones.
            if (fromCenter)
            {
                l = (_startSize.x - w) * 0.5f;
                t = (_startSize.y - h) * 0.5f;
            }
            else
            {
                l = dir.x < 0 ? _startSize.x - w : dir.x > 0 ? 0f : (_startSize.x - w) * 0.5f;
                t = dir.y < 0 ? _startSize.y - h : dir.y > 0 ? 0f : (_startSize.y - h) * 0.5f;
                if (dir.x == 0) { l = 0f; w = _startSize.x; }
                if (dir.y == 0) { t = 0f; h = _startSize.y; }
            }

            // The box rotates around its center, so move the position by the rotated center shift.
            var shift = new Vector2(l + w * 0.5f - _startSize.x * 0.5f, t + h * 0.5f - _startSize.y * 0.5f);
            var center = _startPos + _startSize * 0.5f + Rotate(shift, _startRotation);
            var pos = center - new Vector2(w, h) * 0.5f;
            if (Mathf.Approximately(Mathf.Repeat(totalAngle, 360f), 0f)) pos = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));

            layer.position = pos;
            LayerOps.Resize(layer, new Vector2(w, h));
        }

        void ResizeFrame(Vector2 delta, bool keepAspect, bool fromCenter)
        {
            var dir = HandleDirs[_handle];
            float k = fromCenter ? 2f : 1f;
            float w = _startFrameSize.x + dir.x * delta.x * k;
            float h = _startFrameSize.y + dir.y * delta.y * k;
            if (dir.x == 0) w = _startFrameSize.x;
            if (dir.y == 0) h = _startFrameSize.y;
            if (keepAspect && dir.x != 0 && dir.y != 0)
            {
                float f = Mathf.Max(w / _startFrameSize.x, h / _startFrameSize.y);
                w = _startFrameSize.x * f;
                h = _startFrameSize.y * f;
            }
            var size = new Vector2Int(
                Mathf.Clamp(Mathf.RoundToInt(w), 1, SpriteRasterizer.MaxSize),
                Mathf.Clamp(Mathf.RoundToInt(h), 1, SpriteRasterizer.MaxSize));
            LayerOps.ResizeFrame(_host.Style, size);
        }

        // ------------------------------------------------------------------ keys and wheel

        void OnWheel(WheelEvent e)
        {
            if (!e.actionKey) return;
            _host.SetZoom(Zoom * (e.delta.y < 0 ? 1.1f : 1f / 1.1f));
            e.StopPropagation();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            int step = e.shiftKey ? 10 : 1;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow: _host.Nudge(new Vector2(-step, 0)); break;
                case KeyCode.RightArrow: _host.Nudge(new Vector2(step, 0)); break;
                case KeyCode.UpArrow: _host.Nudge(new Vector2(0, -step)); break;
                case KeyCode.DownArrow: _host.Nudge(new Vector2(0, step)); break;
                case KeyCode.Delete:
                case KeyCode.Backspace: _host.DeleteSelected(); break;
                case KeyCode.Escape: _host.SelectParent(); break;
                case KeyCode.G when e.actionKey: _host.GroupSelected(); break;
                case KeyCode.D when e.actionKey: _host.DuplicateSelected(); break;
                case KeyCode.RightBracket when e.actionKey: _host.ReorderSelected(1); break;
                case KeyCode.LeftBracket when e.actionKey: _host.ReorderSelected(-1); break;
                default: return;
            }
            e.StopPropagation();
        }

        void OnValidateCommand(ValidateCommandEvent e)
        {
            if (e.commandName == "Duplicate" || e.commandName == "Delete" || e.commandName == "SoftDelete")
                e.StopPropagation();
        }

        void OnExecuteCommand(ExecuteCommandEvent e)
        {
            switch (e.commandName)
            {
                case "Duplicate": _host.DuplicateSelected(); break;
                case "Delete":
                case "SoftDelete": _host.DeleteSelected(); break;
                default: return;
            }
            e.StopPropagation();
        }

        // ------------------------------------------------------------------ drawing

        void DrawOverlay(MeshGenerationContext ctx)
        {
            if (_result == null || _host.Style == null) return;
            var painter = ctx.painter2D;
            Dictionary<string, Placement> placements;
            try
            {
                placements = Placements();
            }
            catch (System.Exception)
            {
                return;
            }

            if (_hoverId != null && _hoverId != _host.SelectedId && placements.TryGetValue(_hoverId, out var hp))
                Outline(painter, hp, HoverColor, 1f);

            var selected = _host.SelectedLayer;
            Placement sp;
            if (selected == null) sp = FramePlacement();
            else if (!placements.TryGetValue(selected.id, out sp)) return;

            Outline(painter, sp, Accent, 1.5f);
            if (selected == null || !selected.locked)
            {
                painter.lineWidth = 1f;
                for (int i = 0; i < HandleDirs.Length; i++)
                {
                    var c = HandlePoint(sp, i);
                    painter.fillColor = Color.white;
                    painter.strokeColor = Accent;
                    painter.BeginPath();
                    painter.MoveTo(c + new Vector2(-HandleSize, -HandleSize) * 0.5f);
                    painter.LineTo(c + new Vector2(HandleSize, -HandleSize) * 0.5f);
                    painter.LineTo(c + new Vector2(HandleSize, HandleSize) * 0.5f);
                    painter.LineTo(c + new Vector2(-HandleSize, HandleSize) * 0.5f);
                    painter.ClosePath();
                    painter.Fill();
                    painter.Stroke();
                }
            }

            painter.strokeColor = SnapColor;
            painter.lineWidth = 1f;
            foreach (var (a, b) in _snapLines)
            {
                painter.BeginPath();
                painter.MoveTo(a);
                painter.LineTo(b);
                painter.Stroke();
            }
        }

        void Outline(Painter2D painter, Placement p, Color color, float width)
        {
            var corners = p.Corners();
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(ToOverlay(corners[0]));
            for (int i = 1; i < 4; i++) painter.LineTo(ToOverlay(corners[i]));
            painter.ClosePath();
            painter.Stroke();
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
    }
}
