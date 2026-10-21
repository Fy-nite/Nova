using System;
using System.Collections.Generic;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    /// <summary>
    /// Godot Tree backed widget. Caller data rides on each item as opaque
    /// UserData; the wrapper shuttles item handles (not data) through the
    /// native drag/context/rename callbacks. Items are addressed by a
    /// monotonic ordinal kept in TreeItem metadata, since Godot may hand back
    /// different managed TreeItem wrappers for the same native object.
    /// </summary>
    public partial class GodotTreeView : ITreeView
    {
        private sealed partial class DragTree : Tree
        {
            public Func<bool>? DragEnabled;
            public Func<GodotTreeItem, IContextMenu?>? ContextMenuRequested;
            public Action<GodotTreeItem, GodotTreeItem?, TreeDropPosition>? ItemDropped;
            public Action? RenameCanceled;
            public Func<long, GodotTreeItem?>? ItemFromOrdinal;

            // Self-managed row drag. Godot's native Tree starts a drag only
            // through its own internal pipeline (its C++ control handles item
            // press/move itself and never routes through _GetDragData for its
            // items), so the built-in drop callbacks never fire for us. Driving
            // the gesture from _GuiInput gives us the whole thing: arm on press
            // over a row, start once the pointer clears a threshold, and drop on
            // release. DropModeFlags stay Disabled the entire time; we draw the
            // indicator ourselves from _Draw so plain hovering is inert.
            private const float DragThreshold = 6f;

            private bool _pendingDrag;
            private bool _dragging;
            private Vector2 _pressPos;
            private TreeItem? _dragItem;

            // Where the pointer currently wants the drop, recomputed on every
            // motion and consumed by the release. Null target = world root.
            private TreeItem? _hoverItem;
            private int _hoverSection;
            private bool _hoverValid;

            public override Variant _GetDragData(Vector2 atPosition) => default;

            public override bool _CanDropData(Vector2 atPosition, Variant data) => false;

            // Godot's native Tree handles mouse events in its C++ gui_input and
            // accepts them, so the script virtual _GuiInput is never called for
            // its items. Drive the drag gesture from _Input instead (enabled
            // explicitly in the tree view's constructor), hit-testing against
            // this control's own rect so events aimed elsewhere are ignored.
            public override void _Input(InputEvent @event)
            {
                if (DragEnabled == null || ItemDropped == null) return;
                if (@event is not InputEventMouse mouse) return;

                // Only act on events that land inside this control.
                var local = mouse.Position - GlobalPosition;
                if (!new Rect2(Vector2.Zero, Size).HasPoint(local))
                {
                    if (_dragging && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                        CancelPendingDrag();
                    return;
                }

                HandleLocalInput(@event, local);
            }

            private void HandleLocalInput(InputEvent @event, Vector2 local)
            {
                if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
                {
                    CancelPendingDrag();
                    RenameCanceled?.Invoke();
                    return;
                }

                if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed)
                    {
                        if (DragEnabled?.Invoke() == true)
                        {
                            var hit = GetItemAtPosition(local);
                            if (hit != null)
                            {
                                _pendingDrag = true;
                                _pressPos = local;
                                _dragItem = hit;
                            }
                        }
                        return;
                    }

                    if (_dragging)
                    {
                        FinishDrag(local);
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                    CancelPendingDrag();
                    return;
                }

                if (@event is InputEventMouseButton rmb
                    && rmb.ButtonIndex == MouseButton.Right
                    && rmb.Pressed)
                {
                    var item = GetItemAtPosition(local);
                    if (item != null && ContextMenuRequested != null)
                    {
                        var wrapper = ItemFromOrdinal?.Invoke((long)item.GetMetadata(0));
                        if (wrapper != null)
                        {
                            var menu = ContextMenuRequested(wrapper);
                            if (menu != null)
                            {
                                SetSelected(item, 0);
                                var screen = DisplayServer.MouseGetPosition();
                                menu.Show(screen.X, screen.Y);
                            }
                        }
                    }
                    GetViewport().SetInputAsHandled();
                    return;
                }

                if (@event is InputEventMouseMotion motion)
                {
                    if (_pendingDrag && !_dragging)
                    {
                        if (_pressPos.DistanceTo(motion.Position - GlobalPosition) >= DragThreshold)
                        {
                            _dragging = true;
                            // No SetDragPreview: the self-managed gesture never
                            // starts Godot's native drag pipeline, so
                            // set_drag_preview would fail (!gui_is_dragging)
                            // on every drag. The _Draw indicator is the visual.
                            UpdateHover(local);
                            QueueRedraw();
                            GetViewport().SetInputAsHandled();
                        }
                    }
                    else if (_dragging)
                    {
                        UpdateHover(local);
                        QueueRedraw();
                        GetViewport().SetInputAsHandled();
                    }
                }
            }

            public override void _GuiInput(InputEvent @event)
            {
                // Native Tree eats gui_input in C++, so this is a fallback for
                // hosts/themes that do route it; the real path is _Input above.
                base._GuiInput(@event);
            }

            /// <summary>
            /// Recompute the drop target from the pointer position. Rows read in
            /// three bands (Unity/Godot convention): top quarter = before, middle
            /// half = child, bottom quarter = after. Empty space below the rows
            /// is a drop on the world root.
            /// </summary>
            private void UpdateHover(Vector2 atPosition)
            {
                var item = GetItemAtPosition(atPosition);
                _hoverItem = item;
                if (item == null)
                {
                    _hoverSection = 0;
                    _hoverValid = true;
                    return;
                }

                // Never offer a row as a target for itself.
                if (ReferenceEquals(item, _dragItem))
                {
                    _hoverValid = false;
                    return;
                }

                var rect = GetItemAreaRect(item, 0, -1);
                float t = rect.Size.Y > 0f ? (atPosition.Y - rect.Position.Y) / rect.Size.Y : 0.5f;
                _hoverSection = t < 0.25f ? -1 : t > 0.75f ? 1 : 0;
                _hoverValid = true;
            }

            private void FinishDrag(Vector2 atPosition)
            {
                var dragged = _dragItem;
                _dragging = false;
                _pendingDrag = false;
                _dragItem = null;
                QueueRedraw();
                if (dragged == null || !_hoverValid) return;

                var wrapper = ItemFromOrdinal?.Invoke((long)dragged.GetMetadata(0));
                if (wrapper == null) return;

                if (_hoverItem == null)
                {
                    ItemDropped?.Invoke(wrapper, null, TreeDropPosition.Root);
                    return;
                }

                var target = ItemFromOrdinal?.Invoke((long)_hoverItem.GetMetadata(0));
                if (target == null) return;
                var position = _hoverSection < 0 ? TreeDropPosition.Before
                    : _hoverSection > 0 ? TreeDropPosition.After
                    : TreeDropPosition.Child;
                ItemDropped?.Invoke(wrapper, target, position);
            }

            private void CancelPendingDrag()
            {
                if (_dragging) QueueRedraw();
                _pendingDrag = false;
                _dragging = false;
                _dragItem = null;
                _hoverItem = null;
                _hoverValid = false;
            }

            public override void _Draw()
            {
                if (!_dragging || !_hoverValid) return;

                if (_hoverItem == null)
                    return; // Root drop: no row to mark; the drop just lands.

                var rect = GetItemAreaRect(_hoverItem, 0, -1);
                float bottom = rect.Position.Y + rect.Size.Y;
                // A marker in the tree's own ink rather than an accent pop: a
                // thin full-width band that says where the row is going.
                var color = new Color(0.62f, 0.72f, 0.86f, 0.9f);

                if (_hoverSection == 0)
                {
                    // Child: outline the row it would land inside.
                    DrawRect(rect, color, filled: false, width: 1.5f);
                }
                else
                {
                    float y = _hoverSection < 0 ? rect.Position.Y : bottom;
                    DrawLine(
                        new Vector2(rect.Position.X + 1f, y),
                        new Vector2(rect.Position.X + rect.Size.X - 1f, y),
                        color, 1.5f);
                }
            }
        }

        private readonly DragTree _tree;
        private readonly Dictionary<long, GodotTreeItem> _byOrdinal = new();
        private long _nextOrdinal;
        private bool _dropEnabled = true;
        private Action<ITreeItem?>? _selectionChanged;
        private Action<ITreeItem, string>? _renamed;
        private GodotTreeItem? _renaming;

        public GodotTreeView()
        {
            _tree = new DragTree { HideRoot = true };
            // Native Tree consumes gui_input in C++, so item drags and context
            // clicks have to be caught downstream in _Input; that only runs if
            // input processing is on.
            _tree.SetProcessInput(true);
            _tree.ItemSelected += OnItemSelected;
            _tree.ItemEdited += OnItemEdited;
            _tree.RenameCanceled = CancelRename;
            _tree.ItemFromOrdinal = ordinal =>
                _byOrdinal.TryGetValue(ordinal, out var item) ? item : null;
        }

        public object NativeControl => _tree;

        /// <summary>Test hook: the live Godot tree, for input probes.</summary>
        public Tree NativeTree => _tree;

        public bool Visible
        {
            get => _tree.Visible;
            set => _tree.Visible = value;
        }

        public ITreeItem? SelectedItem
        {
            get
            {
                var selected = _tree.GetSelected();
                return selected == null ? null : Lookup((long)selected.GetMetadata(0));
            }
        }

        public ITreeItem AddItem(ITreeItem? parent, string label, object? userData = null)
        {
            TreeItem godotParent;
            if (parent is GodotTreeItem existing)
                godotParent = existing.Native;
            else
                godotParent = _tree.GetRoot() ?? _tree.CreateItem();

            var item = _tree.CreateItem(godotParent);
            var ordinal = _nextOrdinal++;
            item.SetMetadata(0, ordinal);
            var wrapper = new GodotTreeItem(item, userData) { BaseLabel = label };
            _byOrdinal[ordinal] = wrapper;
            return wrapper;
        }

        public bool RemoveItem(ITreeItem item)
        {
            if (item is not GodotTreeItem wrapper) return false;
            RemoveSubtreeFromMap(wrapper.Native);
            wrapper.Native.Free();
            return true;
        }

        public void Clear()
        {
            CancelRename();
            _tree.Clear();
            _byOrdinal.Clear();
        }

        public void SetSelected(ITreeItem? item)
        {
            if (item == null)
            {
                _tree.DeselectAll();
                return;
            }
            if (item is GodotTreeItem wrapper)
                _tree.SetSelected(wrapper.Native, 0);
        }

        public void ScrollTo(ITreeItem item)
        {
            if (item is not GodotTreeItem wrapper) return;
            var node = wrapper.Native.GetParent();
            while (node != null)
            {
                node.Collapsed = false;
                node = node.GetParent();
            }
            _tree.ScrollToItem(wrapper.Native);
        }

        public void SetOnSelectionChanged(Action<ITreeItem?> onSelectionChanged)
            => _selectionChanged = onSelectionChanged;

        public void BeginRename(ITreeItem item)
        {
            if (item is not GodotTreeItem wrapper) return;
            if (_renaming != null)
                _renaming.Native.SetEditable(0, false);
            _renaming = wrapper;
            wrapper.Native.SetText(0, wrapper.BaseLabel);
            wrapper.Native.SetEditable(0, true);
            _tree.SetSelected(wrapper.Native, 0);
            _tree.EditSelected();
        }

        public void SetOnRenamed(Action<ITreeItem, string> onRenamed)
            => _renamed = onRenamed;

        public void SetDropEnabled(bool enabled)
            => _dropEnabled = enabled;

        public void SetOnItemDropped(Action<ITreeItem, ITreeItem?, TreeDropPosition> onItemDropped)
        {
            _tree.ItemDropped = onItemDropped;
            _tree.DragEnabled = () => _dropEnabled;
            // DragTree drives the gesture itself (see _GuiInput); Godot's
            // DropModeFlags stay Disabled so hovering never paints an indicator.
        }

        public void SetOnContextMenuRequested(Func<ITreeItem, IContextMenu?> onContextMenuRequested)
            => _tree.ContextMenuRequested = onContextMenuRequested;

        private GodotTreeItem? Lookup(long ordinal)
            => _byOrdinal.TryGetValue(ordinal, out var item) ? item : null;

        private void OnItemSelected()
        {
            var selected = _tree.GetSelected();
            _selectionChanged?.Invoke(selected == null ? null : Lookup((long)selected.GetMetadata(0)));
        }

        private void CancelRename()
        {
            if (_renaming == null) return;
            _renaming.Native.SetEditable(0, false);
            _renaming.Native.SetText(0, _renaming.ComposeText());
            _renaming = null;
        }

        private void OnItemEdited()
        {
            var item = _tree.GetEdited();
            if (item == null) return;
            var wrapper = Lookup((long)item.GetMetadata(0));
            if (wrapper == null || !ReferenceEquals(wrapper, _renaming)) return;
            var text = item.GetText(0).Trim();
            item.SetEditable(0, false);
            _renaming = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                wrapper.BaseLabel = text;
                _renamed?.Invoke(wrapper, text);
            }
            else
            {
                item.SetText(0, wrapper.ComposeText());
            }
        }

        private void RemoveSubtreeFromMap(TreeItem item)
        {
            foreach (var child in item.GetChildren())
            {
                RemoveSubtreeFromMap(child);
                _byOrdinal.Remove((long)child.GetMetadata(0));
            }
        }
    }

    /// <summary>
    /// Item handle. The icon key maps to a glyph prefix on the label so the
    /// tree reads clearly in any host theme without editor-only icon atlases.
    /// </summary>
    internal sealed class GodotTreeItem : ITreeItem
    {
        private static readonly Dictionary<string, string> Glyphs = new()
        {
            ["element"] = "\u25c6",
            ["camera"] = "\u25b2",
            ["light"] = "\u2600",
            ["script"] = "\u2318",
            ["scene"] = "\u25a1",
            ["folder"] = "\u25b8",
            ["asset"] = "\u25c8",
            ["mesh"] = "\u2b23",
            ["texture"] = "\u25a3",
            ["audio"] = "\u266b",
            ["data"] = "\u25a6",
        };

        private readonly TreeItem _item;
        private string _baseLabel = "";
        private string? _iconKey;

        public GodotTreeItem(TreeItem item, object? userData)
        {
            _item = item;
            UserData = userData;
        }

        public object? UserData { get; }

        internal TreeItem Native => _item;

        public string BaseLabel
        {
            get => _baseLabel;
            set
            {
                _baseLabel = value;
                _item.SetText(0, ComposeText());
            }
        }

        public string Label
        {
            get => _baseLabel;
            set => BaseLabel = value;
        }

        public string? IconKey
        {
            get => _iconKey;
            set
            {
                _iconKey = value;
                _item.SetText(0, ComposeText());
            }
        }

        public bool Expanded
        {
            get => !_item.Collapsed;
            set => _item.Collapsed = !value;
        }

        public bool Visible
        {
            get => _item.Visible;
            set => _item.Visible = value;
        }

        internal string ComposeText()
            => _iconKey != null && Glyphs.TryGetValue(_iconKey, out var glyph)
                ? $"{glyph} {_baseLabel}"
                : _baseLabel;
    }
}
