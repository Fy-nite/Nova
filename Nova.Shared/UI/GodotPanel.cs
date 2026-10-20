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

        public bool UseScrollableContent
        {
            set
            {
                if (!value || _scroll != null) return;
                _scroll = new ScrollContainer
                {
                    HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
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