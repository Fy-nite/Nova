using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotVBox : IVBox
    {
        private VBoxContainer _vbox;

        public GodotVBox(VBoxContainer vbox)
        {
            _vbox = vbox;
        }

        public object NativeControl => _vbox;

        public void AddChild(IWidget child)
        {
            if (child.NativeControl is Node node)
                _vbox.AddChild(node);
        }

        public void RemoveChild(IWidget child)
        {
            if (child.NativeControl is Node node)
                _vbox.RemoveChild(node);
        }
    }
}