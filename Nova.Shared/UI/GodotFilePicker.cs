using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    /// <summary>
    /// FileDialog-backed open/save picker. One instance is reused; the armed
    /// result callback fires with the chosen path, or null on cancel.
    /// </summary>
    public class GodotFilePicker : IFilePicker
    {
        private readonly FileDialog _dialog;
        private Action<string?>? _onResult;

        public GodotFilePicker()
        {
            _dialog = new FileDialog
            {
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true
            };
            _dialog.FileSelected += path => Deliver(path);
            _dialog.Canceled += () => Deliver(null);
        }

        public object NativeControl => _dialog;

        public bool Visible
        {
            get => _dialog.Visible;
            set => _dialog.Visible = value;
        }

        public void ShowOpen(string title, string description, string[] extensions, Action<string?> onResult)
            => Show(title, description, extensions, "", FileDialog.FileModeEnum.OpenFile, onResult);

        public void ShowSave(string title, string description, string[] extensions, string suggestedFileName, Action<string?> onResult)
            => Show(title, description, extensions, suggestedFileName, FileDialog.FileModeEnum.SaveFile, onResult);

        public void Cancel()
        {
            if (_dialog.Visible)
            {
                _dialog.Hide();
                Deliver(null);
            }
        }

        private void Show(
            string title,
            string description,
            string[] extensions,
            string suggestedFileName,
            FileDialog.FileModeEnum mode,
            Action<string?> onResult)
        {
            _onResult = onResult;
            _dialog.Title = title;
            _dialog.FileMode = mode;
            _dialog.CurrentFile = suggestedFileName;
            _dialog.ClearFilters();

            if (extensions is { Length: > 0 })
            {
                var patterns = extensions
                    .Select(e => e.StartsWith('.') ? "*" + e : "*." + e)
                    .ToArray();
                _dialog.AddFilter(string.Join(" ", patterns), description);
            }

            if (_dialog.GetParent() == null)
            {
                var tree = Engine.GetMainLoop() as SceneTree;
                tree?.Root.AddChild(_dialog);
            }

            _dialog.PopupCentered(new Vector2I(720, 480));
        }

        private void Deliver(string? path)
        {
            var callback = _onResult;
            _onResult = null;
            callback?.Invoke(path);
        }
    }
}
