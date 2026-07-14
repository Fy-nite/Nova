using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotHBox : IHBox
    {
        private HBoxContainer _hbox;

        public GodotHBox(HBoxContainer hbox)
        {
            _hbox = hbox;
        }

        public object NativeControl => _hbox;

        public void AddChild(IWidget child)
        {
            if (child.NativeControl is Node node)
                _hbox.AddChild(node);
        }

        public void RemoveChild(IWidget child)
        {
            if (child.NativeControl is Node node)
                _hbox.RemoveChild(node);
        }
    }
}