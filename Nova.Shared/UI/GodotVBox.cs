using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotVBox : IVBox
    {
        private VBoxContainer _vbox;
        private bool _expand = true;

        public GodotVBox(VBoxContainer vbox)
        {
            _vbox = vbox;
        }

        public object NativeControl => _vbox;

        public bool Expand
        {
            get => _expand;
            set
            {
                _expand = value;
                // GrowAll runs after the widget layer builds and would
                // re-apply expansion to every box, so record the intent as
                // metadata it checks before touching the flags.
                _vbox.SetMeta(LayoutHints.ExpandMetaKey, value);
                if (value)
                    _vbox.SizeFlagsHorizontal |= Control.SizeFlags.Expand;
                else
                    _vbox.SizeFlagsHorizontal &= ~Control.SizeFlags.Expand;
            }
        }

        public bool Visible
        {
            get => _vbox.Visible;
            set => _vbox.Visible = value;
        }

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