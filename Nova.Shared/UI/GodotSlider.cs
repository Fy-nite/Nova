using System;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotSlider : ISlider
    {
        private readonly HSlider _slider;

        public GodotSlider(HSlider slider)
        {
            _slider = slider;
        }

        public float Min
        {
            get => (float)_slider.MinValue;
            set => _slider.MinValue = value;
        }

        public float Max
        {
            get => (float)_slider.MaxValue;
            set => _slider.MaxValue = value;
        }

        public float Step
        {
            get => (float)_slider.Step;
            set => _slider.Step = value;
        }

        public float Value
        {
            get => (float)_slider.Value;
            set => _slider.Value = value;
        }

        public bool Enabled
        {
            get => !_slider.Editable;
            set => _slider.Editable = value;
        }

        public object NativeControl => _slider;

        public bool Visible
        {
            get => _slider.Visible;
            set => _slider.Visible = value;
        }

        public void SetOnChanged(Action<float> onChanged)
        {
            _slider.ValueChanged += (double value) => onChanged((float)value);
        }
    }
}
