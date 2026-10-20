using System;
using System.Collections.Generic;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    public class GodotUIProvider : IUIProvider
    {
        private readonly Window _rootWindow;

        public GodotUIProvider()
        {
            _rootWindow = new Window();
        }

        /// <summary>
        /// Bind the provider to an existing window (for example the Godot main
        /// window) instead of creating its own. When an existing window is
        /// supplied the caller is responsible for having it inside the scene
        /// tree; the provider will not add it.
        /// </summary>
        public GodotUIProvider(Window rootWindow)
        {
            _rootWindow = rootWindow ?? throw new ArgumentNullException(nameof(rootWindow));
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

        public ITreeView CreateTreeView()
            => new GodotTreeView();

        public IAssetGrid CreateAssetGrid()
            => new GodotAssetGrid();

        public ICheckbox CreateCheckbox(string text, bool value)
        {
            var checkBox = new CheckBox { Text = text, ButtonPressed = value };
            return new GodotCheckbox(checkBox);
        }

        public IToggle CreateToggle(string text, bool toggled)
        {
            var button = new Button { Text = text, ButtonPressed = toggled };
            return new GodotToggle(button);
        }

        public ISlider CreateSlider(float min, float max, float step, float value)
        {
            var slider = new HSlider
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                Value = value
            };
            return new GodotSlider(slider);
        }

        public INumericField CreateNumericField(float value, string format = "F2")
        {
            var field = new GodotNumericField { Format = format };
            field.Value = value;
            return field;
        }

        public ISplitter CreateSplitter(SplitterOrientation orientation)
            => new GodotSplitter(orientation);

        public IContextMenu CreateContextMenu()
            => new GodotContextMenu();

        public IFilePicker CreateFilePicker()
            => new GodotFilePicker();

        public void ProcessFrame(double delta)
        {
            // Godot handles its own frame processing
        }
    }
}