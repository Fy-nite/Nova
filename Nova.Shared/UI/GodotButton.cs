using System;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotButton : IButton
    {
        private Button _button;

        public GodotButton(Button button)
        {
            _button = button;
        }

        public string Text
        {
            get => _button.Text;
            set => _button.Text = value;
        }

        public bool Enabled
        {
            get => !_button.Disabled;
            set => _button.Disabled = !value;
        }

        public object NativeControl => _button;

        public void SetOnClick(Action onClick)
        {
            _button.Pressed += onClick;
        }
    }
}