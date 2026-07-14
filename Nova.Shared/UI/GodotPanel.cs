using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotPanel : IPanel
    {
        private PanelContainer _panel;
        private VBoxContainer _layout;

        public GodotPanel(PanelContainer panel, string name)
        {
            _panel = panel;
            _layout = new VBoxContainer();
            _panel.AddChild(_layout);
            Name = name;
        }

        public string Name { get; set; }

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