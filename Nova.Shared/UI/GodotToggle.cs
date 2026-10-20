using System;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotToggle : IToggle
    {
        private readonly Button _button;

        public GodotToggle(Button button)
        {
            _button = button;
            _button.ToggleMode = true;
        }

        public string Text
        {
            get => _button.Text;
            set => _button.Text = value;
        }

        public bool Toggled
        {
            get => _button.ButtonPressed;
            set => _button.ButtonPressed = value;
        }

        public bool Enabled
        {
            get => !_button.Disabled;
            set => _button.Disabled = !value;
        }

        public object NativeControl => _button;

        public bool Visible
        {
            get => _button.Visible;
            set => _button.Visible = value;
        }

        public void SetOnToggled(Action<bool> onToggled)
        {
            _button.Toggled += (bool pressed) => onToggled(pressed);
        }
    }
}
