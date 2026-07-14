using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotDock : IDock
    {
        private PanelContainer _panel;
        private VBoxContainer _layout;
        private Label _titleLabel;

        public GodotDock(PanelContainer panel, string title, DockPosition position)
        {
            _panel = panel;
            _layout = new VBoxContainer();
            _titleLabel = new Label { Text = title };
            _layout.AddChild(_titleLabel);
            _panel.AddChild(_layout);
        }

        public string Title
        {
            get => _titleLabel.Text;
            set => _titleLabel.Text = value;
        }

        public object NativeControl => _panel;

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