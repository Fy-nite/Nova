using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotScrollView : IScrollView
    {
        private ScrollContainer _scroll;

        public GodotScrollView(ScrollContainer scroll)
        {
            _scroll = scroll;
        }

        public object NativeControl => _scroll;

        public void SetContent(IWidget content)
        {
            foreach (var child in _scroll.GetChildren())
                _scroll.RemoveChild(child);

            if (content.NativeControl is Node node)
                _scroll.AddChild(node);
        }
    }
}