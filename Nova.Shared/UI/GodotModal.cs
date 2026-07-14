using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotModal : IModal
    {
        private AcceptDialog _dialog;

        public GodotModal(AcceptDialog dialog)
        {
            _dialog = dialog;
        }

        public string Title
        {
            get => _dialog.Title;
            set => _dialog.Title = value;
        }

        public string Content
        {
            get => _dialog.DialogText;
            set => _dialog.DialogText = value;
        }

        public object NativeControl => _dialog;

        public void Show()
        {
            _dialog.Popup();
        }

        public void Close()
        {
            _dialog.Hide();
        }
    }
}