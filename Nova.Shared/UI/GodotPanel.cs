using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotPanel : IPanel
    {
        private readonly PanelContainer _panel;
        private readonly VBoxContainer _layout;
        private ScrollContainer? _scroll;

        public GodotPanel(PanelContainer panel, string name)
        {
            _panel = panel;
            _layout = new VBoxContainer();
            _panel.AddChild(_layout);
            Name = name;
        }

        public string Name { get; set; }

        /// <summary>Floor width for the PanelContainer. The scroll container
        /// hides content width from the parent (see UseScrollableContent), so
        /// a pane that must stay usable (the inspector) declares its floor
        /// here and the parent SplitContainer clamps the split to it.</summary>
        public float MinWidth
        {
            get => _panel.CustomMinimumSize.X;
            set => _panel.CustomMinimumSize =
                new Vector2(value, _panel.CustomMinimumSize.Y);
        }

        public bool UseScrollableContent
        {
            set
            {
                if (!value || _scroll != null) return;
                // HorizontalScrollMode.Disabled makes Godot's ScrollContainer
                // report its content's full minimum WIDTH as its own minimum,
                // which leaks into the parent SplitContainer and starves the
                // sibling pane. Use Auto so a wide inspector/form can shrink
                // (and scroll) instead of forcing the sibling to its minimum.
                _scroll = new ScrollContainer
                {
                    HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                    VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                };
                _panel.RemoveChild(_layout);
                _panel.AddChild(_scroll);
                _scroll.AddChild(_layout);
                _layout.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _layout.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            }
        }

        public object NativeControl => _panel;

        public bool Visible
        {
            get => _panel.Visible;
            set => _panel.Visible = value;
        }

        public void AddChild(IWidget child)
        {
            if (child.NativeControl is Node node)
                _layout.AddChild(node);
        }

        public void RemoveChild(IWidget child)
        {
            if (child.NativeControl is Node node)
                _layout.RemoveChild(node);
        }
    }
}