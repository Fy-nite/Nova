using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotHBox : IHBox
    {
        private HBoxContainer _hbox;
        private bool _expand = true;

        public GodotHBox(HBoxContainer hbox)
        {
            _hbox = hbox;
        }

        public object NativeControl => _hbox;

        public bool Expand
        {
            get => _expand;
            set
            {
                _expand = value;
                // GrowAll runs after the widget layer builds and would
                // re-apply expansion to every box, so record the intent as
                // metadata it checks before touching the flags.
                _hbox.SetMeta(LayoutHints.ExpandMetaKey, value);
                if (value)
                    _hbox.SizeFlagsHorizontal |= Control.SizeFlags.Expand;
                else
                    _hbox.SizeFlagsHorizontal &= ~Control.SizeFlags.Expand;
            }
        }

        public bool Visible
        {
            get => _hbox.Visible;
            set => _hbox.Visible = value;
        }

        public void AddChild(IWidget child)
        {
            if (child.NativeControl is Node node)
                _hbox.AddChild(node);

            // A field added to a row absorbs the row's spare width instead of
            // hugging at its minimum: GrowAll applied this to startup content
            // once, but the Inspector rebuilds rows on every selection change,
            // so the rule has to live where the row is assembled. Labels,
            // buttons and checkboxes stay hugging; the toolbar bypasses this
            // wrapper entirely.
            if (child.NativeControl is LineEdit or OptionButton)
                ((Control)child.NativeControl).SizeFlagsHorizontal |= Control.SizeFlags.Expand;
        }

        public void RemoveChild(IWidget child)
        {
            if (child.NativeControl is Node node)
                _hbox.RemoveChild(node);
        }
    }
}