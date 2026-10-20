using System;
using System.Collections.Generic;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public sealed class GodotAssetGrid : IAssetGrid
    {
        private readonly ScrollContainer _scroll;
        private readonly GridContainer _grid;
        private readonly Dictionary<Button, object> _items = new();
        private Action<object?>? _selectionChanged;
        private Action<object>? _itemActivated;
        private object? _selected;
        private bool _suppressMousePress;

        public GodotAssetGrid()
        {
            _grid = new GridContainer
            {
                Columns = 6,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };
            _scroll = new ScrollContainer
            {
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            _scroll.AddChild(_grid);
            _scroll.Resized += UpdateColumns;
        }

        public object NativeControl => _scroll;

        public bool Visible
        {
            get => _scroll.Visible;
            set => _scroll.Visible = value;
        }

        public object? SelectedUserData => _selected;

        public void SetItems(IEnumerable<AssetGridEntry> items)
        {
            var selected = _selected;
            foreach (var child in _grid.GetChildren())
            {
                _grid.RemoveChild(child);
                child.QueueFree();
            }
            _items.Clear();
            _selected = null;

            foreach (var entry in items)
            {
                var tile = new Button
                {
                    CustomMinimumSize = new Vector2(112, 88),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    FocusMode = Control.FocusModeEnum.All,
                    ToggleMode = true,
                    Text = $"{GlyphFor(entry.IconKey)}\n{entry.Name}\n{entry.Type.ToUpperInvariant()}",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    Alignment = HorizontalAlignment.Center,
                    VerticalIconAlignment = VerticalAlignment.Top,
                    ClipText = true,
                };
                tile.Pressed += () =>
                {
                    if (_suppressMousePress) return;
                    Select(tile, entry.UserData);
                };
                tile.GuiInput += @event =>
                {
                    if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true })
                    {
                        _suppressMousePress = true;
                        Select(tile, entry.UserData);
                        _itemActivated?.Invoke(entry.UserData);
                        Callable.From(() => _suppressMousePress = false).CallDeferred();
                    }
                };
                _grid.AddChild(tile);
                _items[tile] = entry.UserData;
                if (selected != null && ReferenceEquals(selected, entry.UserData))
                {
                    _selected = selected;
                    tile.SetPressedNoSignal(true);
                }
            }
            UpdateColumns();
            _selectionChanged?.Invoke(_selected);
        }

        public void SetOnSelectionChanged(Action<object?> onSelectionChanged)
            => _selectionChanged = onSelectionChanged;

        public void SetOnItemActivated(Action<object> onItemActivated)
            => _itemActivated = onItemActivated;

        private void Select(Button tile, object item)
        {
            if (ReferenceEquals(_selected, item)) return;
            _selected = item;
            foreach (var button in _items.Keys)
                button.SetPressedNoSignal(ReferenceEquals(button, tile));
            _selectionChanged?.Invoke(item);
        }

        private void UpdateColumns()
        {
            var width = _scroll.Size.X;
            _grid.Columns = Math.Max(1, (int)(width / 120f));
        }

        private static string GlyphFor(string iconKey) => iconKey switch
        {
            "scene" => "◉",
            "asset" => "⬡",
            "mesh" => "◇",
            "texture" => "▧",
            "audio" => "♫",
            "script" => "‹›",
            "data" => "▤",
            _ => "·",
        };
    }
}
