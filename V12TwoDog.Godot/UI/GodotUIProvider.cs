using System;
using System.Collections.Generic;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotUIProvider : IUIProvider
    {
        private Window _rootWindow;

        public GodotUIProvider()
        {
            _rootWindow = new Window();
        }

        public IWindow RootWindow => new GodotWindow(_rootWindow);

        public IWindow CreateWindow(string title, WindowOptions options)
        {
            var window = new Window
            {
                Title = title,
                Size = new Vector2I(options.Width, options.Height),
                Unresizable = !options.Resizable,
                Borderless = options.Borderless
            };
            return new GodotWindow(window);
        }

        public IViewportHost CreateViewportHost()
        {
            var container = new SubViewportContainer();
            return new GodotViewportHost(container);
        }

        public IButton CreateButton(string text, Action onClick)
        {
            var button = new Button { Text = text };
            var godotButton = new GodotButton(button);
            godotButton.SetOnClick(onClick);
            return godotButton;
        }

        public ILabel CreateLabel(string text)
        {
            var label = new Label { Text = text };
            return new GodotLabel(label);
        }

        public ITextField CreateTextField(string placeholder, Action<string> onChanged)
        {
            var lineEdit = new LineEdit { PlaceholderText = placeholder };
            var textField = new GodotTextField(lineEdit);
            textField.SetOnChanged(onChanged);
            return textField;
        }

        public IDropdown CreateDropdown(IEnumerable<string> options, Action<int> onSelected)
        {
            var optionButton = new OptionButton();
            var dropdown = new GodotDropdown(optionButton);
            dropdown.SetOptions(options);
            dropdown.SetOnSelected(onSelected);
            return dropdown;
        }

        public IVBox CreateVBox()
        {
            var vbox = new VBoxContainer();
            return new GodotVBox(vbox);
        }

        public IHBox CreateHBox()
        {
            var hbox = new HBoxContainer();
            return new GodotHBox(hbox);
        }

        public IScrollView CreateScrollView()
        {
            var scroll = new ScrollContainer();
            return new GodotScrollView(scroll);
        }

        public IDock CreateDock(string title, DockPosition position)
        {
            var panel = new PanelContainer();
            return new GodotDock(panel, title, position);
        }

        public IPanel CreatePanel(string name)
        {
            var panel = new PanelContainer();
            return new GodotPanel(panel, name);
        }

        public ITabControl CreateTabControl()
        {
            var tabs = new TabContainer();
            return new GodotTabControl(tabs);
        }

        public IToolbar CreateToolbar(string name)
        {
            var hbox = new HBoxContainer();
            return new GodotToolbar(hbox, name);
        }

        public IModal ShowModal(string title, string content)
        {
            var dialog = new AcceptDialog
            {
                Title = title,
                DialogText = content
            };
            var modal = new GodotModal(dialog);
            modal.Show();
            return modal;
        }

        public void ProcessFrame(double delta)
        {
            // Godot handles its own frame processing
        }
    }
}