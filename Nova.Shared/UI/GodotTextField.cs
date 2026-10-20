using System;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotTextField : ITextField
    {
        private LineEdit _lineEdit;

        public GodotTextField(LineEdit lineEdit)
        {
            _lineEdit = lineEdit;
        }

        public string Text
        {
            get => _lineEdit.Text;
            set => _lineEdit.Text = value;
        }

        public string Placeholder
        {
            get => _lineEdit.PlaceholderText;
            set => _lineEdit.PlaceholderText = value;
        }

        public bool Enabled
        {
            get => _lineEdit.Editable;
            set => _lineEdit.Editable = value;
        }

        public object NativeControl => _lineEdit;

        public bool Visible
        {
            get => _lineEdit.Visible;
            set => _lineEdit.Visible = value;
        }

        public void SetOnChanged(Action<string> onChanged)
        {
            _lineEdit.TextChanged += (string text) => onChanged(text);
        }
    }
}