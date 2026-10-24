using System;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    /// <summary>Godot fallback code editor used when the Paper UI provider is not active.</summary>
    public sealed class GodotCodeEditor : ICodeEditor
    {
        private readonly CodeEdit _edit = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(240, 90),
            GuttersDrawLineNumbers = true,
        };
        private Action<string>? _onChanged;
        private Action? _onSave;

        public GodotCodeEditor()
        {
            var mono = new SystemFont { FontNames = new[] { "Cascadia Mono", "Consolas", "Courier New", "monospace" } };
            _edit.AddThemeFontOverride("font", mono);
            _edit.AddThemeFontSizeOverride("font_size", 13);
            _edit.TextChanged += () => _onChanged?.Invoke(_edit.Text);
            _edit.GuiInput += OnGuiInput;
        }

        public object NativeControl => _edit;
        public bool Visible { get => _edit.Visible; set => _edit.Visible = value; }
        public string Text { get => _edit.Text; set => _edit.Text = value ?? ""; }
        public string FilePath { get; set; } = "";
        public bool ReadOnly { get => !_edit.Editable; set => _edit.Editable = !value; }

        public void SetLanguage(string languageId) { }
        public void SetCompilerLanguage(Prowl.OrigamiUI.CodeLanguage language) { }

        public void SetOnChanged(Action<string> onChanged) => _onChanged = onChanged;
        public void SetOnSaveRequested(Action onSave) => _onSave = onSave;

        public void Focus() => _edit.GrabFocus();

        private void OnGuiInput(InputEvent @event)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false } key
                && key.CtrlPressed && key.Keycode == Key.S)
            {
                _onSave?.Invoke();
                _edit.AcceptEvent();
            }
        }
    }
}
