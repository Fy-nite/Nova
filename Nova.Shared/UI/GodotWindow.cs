using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotWindow : IWindow
    {
        private Window _window;

        public GodotWindow(Window window)
        {
            _window = window;
        }

        public string Title
        {
            get => _window.Title;
            set => _window.Title = value;
        }

        public int Width
        {
            get => _window.Size.X;
            set => _window.Size = new Vector2I(value, _window.Size.Y);
        }

        public int Height
        {
            get => _window.Size.Y;
            set => _window.Size = new Vector2I(_window.Size.X, value);
        }

        public bool Visible
        {
            get => _window.Visible;
            set => _window.Visible = value;
        }

        public object NativeControl => _window;

        public void Close()
        {
            _window.Hide();
        }
    }
}