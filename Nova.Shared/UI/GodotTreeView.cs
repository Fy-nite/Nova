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
    public class GodotTreeView : ITreeView
    {
        private sealed class DragTree : Tree
        {
            public Func<bool>? DragEnabled;
            public Func<GodotTreeItem, IContextMenu?>? ContextMenuRequested;
            public Action<GodotTreeItem, GodotTreeItem>? ItemDropped;
            public Action? RenameCanceled;
            public Func<long, GodotTreeItem?>? ItemFromOrdinal;

            public override Variant _GetDragData(Vector2 atPosition)
            {
                if (DragEnabled?.Invoke() == false) return default;

                var item = GetItemAtPosition(atPosition);
                if (item == null) return default;
                SetDragPreview(DragPreviewFrom(item));
                return Variant.From((long)item.GetMetadata(0));
            }

            public override bool _CanDropData(Vector2 atPosition, Variant data)
            {
                if (DragEnabled?.Invoke() == false) return false;
                if (data.VariantType != Variant.Type.Int) return false;
                return GetItemAtPosition(atPosition) != null;
            }

            public override void _DropData(Vector2 atPosition, Variant data)
            {
                var target = GetItemAtPosition(atPosition);
                if (target == null || data.VariantType != Variant.Type.Int) return;
                var dragged = ItemFromOrdinal?.Invoke((long)data);
                var dropOn = ItemFromOrdinal?.Invoke((long)target.GetMetadata(0));
                if (dragged != null && dropOn != null)
                    ItemDropped?.Invoke(dragged, dropOn);
            }

            public override void _GuiInput(InputEvent @event)
            {
                if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
                {
                    RenameCanceled?.Invoke();
                    base._GuiInput(@event);
                    return;
                }
                if (@event is InputEventMouseButton mb
                    && mb.ButtonIndex == MouseButton.Right
                    && mb.Pressed)
                {
                    var item = GetItemAtPosition(mb.Position);
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
                    AcceptEvent();
                    return;
                }
                base._GuiInput(@event);
            }

            private static Control DragPreviewFrom(TreeItem item)
            {
                var label = new Label { Text = item.GetText(0) };
                label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
                return label;
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
            _tree.ItemSelected += OnItemSelected;
            _tree.ItemEdited += OnItemEdited;
            _tree.RenameCanceled = CancelRename;
            _tree.ItemFromOrdinal = ordinal =>
                _byOrdinal.TryGetValue(ordinal, out var item) ? item : null;
        }

        public object NativeControl => _tree;

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

        public void SetOnItemDropped(Action<ITreeItem, ITreeItem> onItemDropped)
        {
            _tree.ItemDropped = onItemDropped;
            _tree.DragEnabled = () => _dropEnabled;
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
