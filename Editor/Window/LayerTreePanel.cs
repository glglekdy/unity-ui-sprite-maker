using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UISpriteMaker.Editor
{
    /// <summary>
    /// Left pane: the layer tree, frontmost layer on top (like Figma). Drag rows to reorder or reparent,
    /// toggle visibility/lock per row, double-click to rename, right-click for more.
    /// </summary>
    sealed class LayerTreePanel : VisualElement
    {
        const int FrameId = 0;

        readonly UISpriteMakerWindow _host;
        readonly TreeView _tree;
        readonly Dictionary<int, Layer> _byId = new Dictionary<int, Layer>();
        bool _expandedOnce;
        int _draggedId = -1;

        static Texture2D s_Visible, s_Hidden, s_Locked, s_Unlocked;

        public LayerTreePanel(UISpriteMakerWindow host)
        {
            _host = host;
            style.flexGrow = 1;

            var add = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, paddingLeft = 2, paddingTop = 2 } };
            add.Add(SmallButton("+ Shape", () => _host.AddLayer(new ShapeLayer { name = "", size = new Vector2(80, 40), radius = { radius = 8 } }), "Add a rounded rectangle"));
            add.Add(SmallButton("+ Image", () => _host.AddLayer(new ImageLayer { size = new Vector2(48, 48) }), "Add an image (Sprite or Texture2D)"));
            add.Add(SmallButton("+ Text", () =>
            {
                var t = new TextLayer();
                t.size = TextEngine.Measure(t);
                t.wrap = false;
                _host.AddLayer(t);
            }, "Add a text layer"));
            add.Add(SmallButton("+ Group", () => _host.AddLayer(new GroupLayer { size = new Vector2(100, 60) }), "Add an empty group"));
            Add(add);

            _tree = new TreeView
            {
                fixedItemHeight = 20,
                reorderable = true,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
                style = { flexGrow = 1, marginTop = 2 },
            };
            _tree.selectedIndicesChanged += _ =>
            {
                int index = _tree.selectedIndex;
                if (index < 0) return;
                int id = _tree.GetIdForIndex(index);
                _host.Select(id == FrameId || !_byId.TryGetValue(id, out var l) ? null : l.id);
            };
            _tree.canStartDrag += args => args.id != FrameId && _byId.ContainsKey(args.id);
            _tree.setupDragAndDrop += args =>
            {
                _draggedId = args.selectedIds.FirstOrDefault();
                return args.startDragArgs;
            };
            _tree.dragAndDropUpdate += args => CanDrop(args) ? DragVisualMode.Move : DragVisualMode.Rejected;
            _tree.handleDrop += args =>
            {
                if (!CanDrop(args)) return DragVisualMode.Rejected;
                Drop(args);
                return DragVisualMode.Move;
            };
            Add(_tree);

            var tools = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, paddingLeft = 2, paddingBottom = 2 } };
            tools.Add(SmallButton("Duplicate", _host.DuplicateSelected, "Duplicate (Ctrl+D)"));
            tools.Add(SmallButton("Group", _host.GroupSelected, "Put the layer in a group (Ctrl+G)"));
            tools.Add(SmallButton("▲", () => _host.ReorderSelected(1), "Bring forward (Ctrl+])"));
            tools.Add(SmallButton("▼", () => _host.ReorderSelected(-1), "Send backward (Ctrl+[)"));
            tools.Add(SmallButton("Delete", _host.DeleteSelected, "Delete (Del)"));
            Add(tools);
        }

        static Button SmallButton(string text, System.Action action, string tooltip) =>
            new Button(action) { text = text, tooltip = tooltip, style = { marginLeft = 1, marginRight = 1, paddingLeft = 4, paddingRight = 4 } };

        static int IdOf(Layer layer)
        {
            int id = layer.id.GetHashCode();
            return id == FrameId || id == -1 ? id + 7 : id;
        }

        /// <summary>Rebuilds the rows from the style (after structural changes, undo, loading).</summary>
        public void Rebuild()
        {
            _byId.Clear();
            var style = _host.Style;

            TreeViewItemData<Layer> Make(Layer layer)
            {
                int id = IdOf(layer);
                _byId[id] = layer;
                List<TreeViewItemData<Layer>> kids = null;
                if (layer.Children != null)
                    kids = Enumerable.Reverse(layer.Children).Where(c => c != null).Select(Make).ToList();
                return new TreeViewItemData<Layer>(id, layer, kids);
            }

            var top = Enumerable.Reverse(style.layers).Where(l => l != null).Select(Make).ToList();
            _tree.SetRootItems(new List<TreeViewItemData<Layer>> { new TreeViewItemData<Layer>(FrameId, null, top) });
            _tree.Rebuild();
            if (!_expandedOnce)
            {
                _tree.ExpandAll();
                _expandedOnce = true;
            }
            else
            {
                _tree.ExpandItem(FrameId);
            }
            SyncSelection();
        }

        /// <summary>Selects the row of the window's selected layer (expanding its parents).</summary>
        public void SyncSelection()
        {
            var selected = _host.SelectedLayer;
            int id = selected != null ? IdOf(selected) : FrameId;
            if (selected != null)
            {
                // Make sure the row is visible.
                LayerTree.Find(_host.Style.layers, selected.id, out _, out var parent);
                while (parent != null)
                {
                    _tree.ExpandItem(IdOf(parent));
                    LayerTree.Find(_host.Style.layers, parent.id, out _, out parent);
                }
                _tree.ExpandItem(FrameId);
            }
            _tree.SetSelectionByIdWithoutNotify(new[] { id });
            _tree.ScrollToItemById(id);
        }

        /// <summary>Refreshes names and toggles without rebuilding the tree.</summary>
        public void RefreshRows() => _tree.RefreshItems();

        // ------------------------------------------------------------------ rows

        VisualElement MakeRow()
        {
            LoadIcons();
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
            var badge = new Label { name = "badge", style = { width = 16, unityTextAlign = TextAnchor.MiddleCenter, opacity = 0.7f, fontSize = 10 } };
            var label = new Label { name = "label", style = { flexGrow = 1, flexShrink = 1, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } };
            var eye = IconButton("eye");
            var lck = IconButton("lock");
            row.Add(badge);
            row.Add(label);
            row.Add(lck);
            row.Add(eye);

            eye.clicked += () =>
            {
                if (row.userData is Layer l)
                    _host.Edit(l.visible ? "Hide Layer" : "Show Layer", () => { l.visible = !l.visible; return true; }, false);
            };
            lck.clicked += () =>
            {
                if (row.userData is Layer l)
                    _host.Edit(l.locked ? "Unlock Layer" : "Lock Layer", () => { l.locked = !l.locked; return true; }, false);
            };
            label.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.clickCount == 2 && row.userData is Layer l) StartRename(row, label, l);
            });
            row.AddManipulator(new ContextualMenuManipulator(e => BuildMenu(e, row.userData as Layer)));
            return row;
        }

        static Button IconButton(string name)
        {
            var b = new Button { name = name };
            b.style.width = 18;
            b.style.height = 16;
            b.style.paddingLeft = b.style.paddingRight = b.style.paddingTop = b.style.paddingBottom = 0;
            b.style.marginLeft = b.style.marginRight = 1;
            b.style.backgroundColor = Color.clear;
            b.style.borderLeftWidth = b.style.borderRightWidth = b.style.borderTopWidth = b.style.borderBottomWidth = 0;
            b.style.backgroundSize = new BackgroundSize(14, 14);
            return b;
        }

        void BindRow(VisualElement row, int index)
        {
            var layer = _tree.GetItemDataForIndex<Layer>(index);
            row.userData = layer;
            var badge = row.Q<Label>("badge");
            var label = row.Q<Label>("label");
            var eye = row.Q<Button>("eye");
            var lck = row.Q<Button>("lock");

            if (layer == null)
            {
                badge.text = "▣";
                label.text = "Frame";
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.opacity = 1f;
                eye.style.visibility = lck.style.visibility = Visibility.Hidden;
                return;
            }

            badge.text = layer switch { ShapeLayer _ => "▢", ImageLayer _ => "◩", TextLayer _ => "T", GroupLayer _ => "▤", _ => "?" };
            label.text = layer.DisplayName;
            label.style.unityFontStyleAndWeight = FontStyle.Normal;
            label.style.opacity = layer.visible ? 1f : 0.45f;
            eye.style.visibility = lck.style.visibility = Visibility.Visible;
            eye.style.backgroundImage = layer.visible ? s_Visible : s_Hidden;
            eye.style.opacity = layer.visible ? 0.8f : 0.5f;
            eye.tooltip = layer.visible ? "Hide" : "Show";
            lck.style.backgroundImage = layer.locked ? s_Locked : s_Unlocked;
            lck.style.opacity = layer.locked ? 0.9f : 0.25f;
            lck.tooltip = layer.locked ? "Unlock" : "Lock (can't be selected on the canvas)";
        }

        static void LoadIcons()
        {
            if (s_Visible != null) return;
            s_Visible = Icon("animationvisibilitytoggleon");
            s_Hidden = Icon("animationvisibilitytoggleoff");
            s_Locked = Icon("IN LockButton on");
            s_Unlocked = Icon("IN LockButton");

            static Texture2D Icon(string name) => EditorGUIUtility.IconContent(name)?.image as Texture2D;
        }

        void StartRename(VisualElement row, Label label, Layer layer)
        {
            var field = new TextField { value = layer.DisplayName, style = { flexGrow = 1, marginLeft = 0 } };
            label.style.display = DisplayStyle.None;
            row.Insert(row.IndexOf(label) + 1, field);
            bool done = false;

            void Finish(bool apply)
            {
                if (done) return;
                done = true;
                string value = field.value?.Trim() ?? "";
                field.RemoveFromHierarchy();
                label.style.display = DisplayStyle.Flex;
                if (apply && value != layer.DisplayName)
                    _host.Edit("Rename Layer", () => { layer.name = value; return true; }, false);
            }

            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Finish(true);
                else if (e.keyCode == KeyCode.Escape) Finish(false);
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => Finish(true));
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectAll();
            });
        }

        void BuildMenu(ContextualMenuPopulateEvent e, Layer layer)
        {
            if (layer == null) return;
            _host.Select(layer.id);
            e.menu.AppendAction("Rename", _ =>
            {
                var row = _tree.GetRootElementForId(IdOf(layer));
                var label = row?.Q<Label>("label");
                if (label != null) StartRename(label.parent, label, layer);
            });
            e.menu.AppendAction("Duplicate", _ => _host.DuplicateSelected());
            e.menu.AppendAction("Group", _ => _host.GroupSelected());
            e.menu.AppendSeparator();
            e.menu.AppendAction("Bring Forward", _ => _host.ReorderSelected(1));
            e.menu.AppendAction("Send Backward", _ => _host.ReorderSelected(-1));
            LayerTree.Find(_host.Style.layers, layer.id, out _, out var parent);
            e.menu.AppendAction("Move Out of Parent", _ => _host.MoveOutOfParent(),
                parent != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            e.menu.AppendSeparator();
            e.menu.AppendAction("Delete", _ => _host.DeleteSelected());
        }

        // ------------------------------------------------------------------ drag and drop

        bool CanDrop(HandleDragAndDropArgs args)
        {
            if (!_byId.TryGetValue(_draggedId, out var dragged)) return false;
            if (args.dropPosition == DragAndDropPosition.OutsideItems) return true;
            if (args.parentId == FrameId) return true;
            if (!_byId.TryGetValue(args.parentId, out var parent)) return false;
            return parent.Children != null && !LayerTree.IsSelfOrDescendant(dragged, parent);
        }

        void Drop(HandleDragAndDropArgs args)
        {
            var dragged = _byId[_draggedId];
            var style = _host.Style;
            Layer parent = args.dropPosition != DragAndDropPosition.OutsideItems && args.parentId != FrameId
                ? _byId[args.parentId]
                : null;
            var list = parent != null ? parent.Children : style.layers;

            // Rows are shown frontmost first; the model is in drawing order (bottom first).
            int display = args.dropPosition == DragAndDropPosition.OverItem || args.childIndex < 0 ? 0 : args.childIndex;
            if (args.dropPosition == DragAndDropPosition.OutsideItems) display = int.MaxValue;
            LayerTree.Find(style.layers, dragged.id, out var owner, out _);
            bool sameList = owner == list;
            if (sameList && list.Count - 1 - list.IndexOf(dragged) < display) display--;
            int remaining = list.Count - (sameList ? 1 : 0);
            int modelIndex = Mathf.Clamp(remaining - Mathf.Min(display, remaining), 0, remaining);

            _host.Edit("Move Layer", () => LayerOps.Move(style, dragged.id, parent?.id, modelIndex), true);
            _draggedId = -1;
        }
    }
}
