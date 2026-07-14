using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotToolbar : IToolbar
    {
        private HBoxContainer _hbox;

        public GodotToolbar(HBoxContainer hbox, string name)
        {
            _hbox = hbox;
            Name = name;
        }

        public string Name { get; set; }

        public object NativeControl => _hbox;

        public void AddItem(IWidget item)
        {
            if (item.NativeControl is Node node)
                _hbox.AddChild(node);
        }

        public void RemoveItem(IWidget item)
        {
            if (item.NativeControl is Node node)
                _hbox.RemoveChild(node);
        }
    }
}