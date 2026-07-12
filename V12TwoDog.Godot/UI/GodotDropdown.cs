using System;
using System.Collections.Generic;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotDropdown : IDropdown
    {
        private OptionButton _optionButton;

        public GodotDropdown(OptionButton optionButton)
        {
            _optionButton = optionButton;
        }

        public int SelectedIndex
        {
            get => _optionButton.Selected;
            set => _optionButton.Selected = value;
        }

        public object NativeControl => _optionButton;

        public void SetOptions(IEnumerable<string> options)
        {
            _optionButton.Clear();
            foreach (var option in options)
                _optionButton.AddItem(option);
        }

        public void SetOnSelected(Action<int> onSelected)
        {
            _optionButton.ItemSelected += (long index) => onSelected((int)index);
        }
    }
}