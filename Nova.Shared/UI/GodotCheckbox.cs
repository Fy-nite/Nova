using System;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotCheckbox : ICheckbox
    {
        private readonly CheckBox _checkBox;

        public GodotCheckbox(CheckBox checkBox)
        {
            _checkBox = checkBox;
        }

        public string Text
        {
            get => _checkBox.Text;
            set => _checkBox.Text = value;
        }

        public bool Checked
        {
            get => _checkBox.ButtonPressed;
            set => _checkBox.ButtonPressed = value;
        }

        public bool Enabled
        {
            get => !_checkBox.Disabled;
            set => _checkBox.Disabled = !value;
        }

        public object NativeControl => _checkBox;

        public bool Visible
        {
            get => _checkBox.Visible;
            set => _checkBox.Visible = value;
        }

        public void SetOnChanged(Action<bool> onChanged)
        {
            _checkBox.Toggled += (bool pressed) => onChanged(pressed);
        }
    }
}
