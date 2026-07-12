using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotLabel : ILabel
    {
        private Label _label;

        public GodotLabel(Label label)
        {
            _label = label;
        }

        public string Text
        {
            get => _label.Text;
            set => _label.Text = value;
        }

        public object NativeControl => _label;
    }
}