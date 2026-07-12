using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotTabControl : ITabControl
    {
        private TabContainer _tabs;

        public GodotTabControl(TabContainer tabs)
        {
            _tabs = tabs;
        }

        public int SelectedTabIndex
        {
            get => _tabs.CurrentTab;
            set => _tabs.CurrentTab = value;
        }

        public object NativeControl => _tabs;

        public void AddTab(string name, IWidget content)
        {
            if (content.NativeControl is Control control)
            {
                control.Name = name;
                _tabs.AddChild(control);
            }
        }

        public void RemoveTab(string name)
        {
            foreach (var child in _tabs.GetChildren())
            {
                if (child.Name == name)
                {
                    _tabs.RemoveChild(child);
                    break;
                }
            }
        }
    }
}