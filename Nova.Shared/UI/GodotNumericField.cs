using System;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    /// <summary>
    /// Numeric field: LineEdit with commit-on-enter/focus-loss semantics plus
    /// Alt+drag-to-scrub. Fires <see cref="SetOnChanged"/> only when the parsed
    /// value actually changes.
    /// </summary>
    public partial class GodotNumericField : IScrubbableNumericField
    {
        private sealed partial class NumericEdit : LineEdit
        {
            public Action<float>? OnScrub;
            public float ScrubValue;
            private bool _scrubbing;
            private float _scrubStart;
            private Vector2 _pressPos;

            public override void _GuiInput(InputEvent @event)
            {
                if (@event is InputEventMouseButton mb
                    && mb.ButtonIndex == MouseButton.Left
                    && mb.Pressed
                    && (mb.AltPressed || mb.CtrlPressed))
                {
                    _scrubbing = true;
                    _scrubStart = ScrubValue;
                    _pressPos = mb.Position;
                    MouseFilter = MouseFilterEnum.Ignore; // let the drag move
                    AcceptEvent();
                    return;
                }

                if (@event is InputEventMouseMotion mm && _scrubbing)
                {
                    var delta = (mm.Position.X - _pressPos.X) * 0.1f;
                    ScrubValue = _scrubStart + delta;
                    OnScrub?.Invoke(ScrubValue);
                    AcceptEvent();
                    return;
                }

                if (@event is InputEventMouseButton release
                    && release.ButtonIndex == MouseButton.Left
                    && !release.Pressed
                    && _scrubbing)
                {
                    _scrubbing = false;
                    MouseFilter = MouseFilterEnum.Stop;
                    AcceptEvent();
                    return;
                }

                base._GuiInput(@event);
            }
        }

        private readonly NumericEdit _edit;
        private float _value;
        private string _format = "F2";
        private Action<float>? _onChanged;

        public GodotNumericField()
        {
            _edit = new NumericEdit();
            _edit.TextSubmitted += _ => OnCommitted();
            _edit.FocusExited += OnCommitted;
            _edit.OnScrub = value => Commit(ApplySnap(value));
        }

        /// <summary>Control-held scrubs land on SnapStep increments (e.g. 1 m
        /// for a position axis); typed commits are left alone.</summary>
        private float ApplySnap(float value)
        {
            if (SnapStep <= 0f || !Input.IsKeyPressed(Key.Ctrl)) return value;
            return MathF.Round(value / SnapStep) * SnapStep;
        }

        public float Value
        {
            get => _value;
            set => Commit(value);
        }

        public string Format
        {
            get => _format;
            set
            {
                _format = string.IsNullOrEmpty(value) ? "F2" : value;
                RefreshText();
            }
        }

        public bool Enabled
        {
            get => _edit.Editable;
            set => _edit.Editable = value;
        }

        public string ScrubLabel { get; set; } = "";
        public float SnapStep { get; set; }
        public bool RendersScrubLabel => false;

        public object NativeControl => _edit;

        public bool Visible
        {
            get => _edit.Visible;
            set => _edit.Visible = value;
        }

        public void SetOnChanged(Action<float> onChanged)
            => _onChanged = onChanged;

        private void OnCommitted()
        {
            if (float.TryParse(_edit.Text, out var parsed))
                Commit(parsed);
            else
                RefreshText();
        }

        private void Commit(float newValue)
        {
            _value = newValue;
            _edit.ScrubValue = newValue;
            RefreshText();
            _onChanged?.Invoke(_value);
        }

        private void RefreshText()
            => _edit.Text = _value.ToString(_format);
    }
}
