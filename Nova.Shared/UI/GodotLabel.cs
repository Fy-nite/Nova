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

        public float MinWidth
        {
            get => _label.CustomMinimumSize.X;
            set => _label.CustomMinimumSize = new Vector2(value, _label.CustomMinimumSize.Y);
        }

        public object NativeControl => _label;

        public bool Visible
        {
            get => _label.Visible;
            set => _label.Visible = value;
        }
    }
}