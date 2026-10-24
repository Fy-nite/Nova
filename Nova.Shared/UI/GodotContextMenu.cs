using System;
using System.Collections.Generic;
using Godot;
using V12.UI;

namespace V12TwoDog.Godot.UI
{
    /// <summary>
    /// PopupMenu-backed context menu. The popup is parented to the root window
    /// on first show and reclaimed after each close. Submenus are child
    /// PopupMenus; selecting an item anywhere in the chain closes the whole
    /// menu (the root pointer makes submenu picks dismiss the root too).
    /// </summary>
    public class GodotContextMenu : IContextMenu
    {
        private readonly PopupMenu _popup;
        private readonly List<Action> _actions = new();
        private bool _attached;

        /// <summary>Top-level menu of the chain this menu belongs to; null when
        /// this menu IS the root.</summary>
        private GodotContextMenu? _root;

        public GodotContextMenu()
        {
            _popup = new PopupMenu();
            _popup.AllowSearch = true; // type-ahead narrows the visible items
            _popup.IdPressed += OnIdPressed;
            _popup.PopupHide += OnPopupHide;
        }

        public object NativeControl => _popup;

        public bool Visible
        {
            get => _popup.Visible;
            set => _popup.Visible = value;
        }

        public void AddItem(string label, Action onClick, bool enabled = true, string icon = "")
        {
            var id = _actions.Count;
            _actions.Add(onClick);
            _popup.AddItem(label, id);
            if (!enabled)
                _popup.SetItemDisabled(id, true);
        }

        public void AddSeparator()
            => _popup.AddSeparator();

        public void AddSubmenu(string label, IContextMenu submenu, string icon = "")
        {
            if (submenu is not GodotContextMenu sub) return;

            // A submenu popup must live inside this popup for the native
            // hover-flyout behavior; Show() then skips the root adoption.
            if (sub._popup.GetParent() == null)
            {
                sub._popup.Name = $"submenu_{_popup.GetChildCount()}";
                _popup.AddChild(sub._popup);
            }
            sub._attached = true;
            sub._root = _root ?? this;
            _popup.AddSubmenuItem(label, sub._popup.Name);
        }

        public void Show(float screenX, float screenY)
        {
            AdoptToRootWindow();
            _popup.Popup(new Rect2I((int)screenX, (int)screenY, 0, 0));
        }

        public async void ShowAt(IWidget anchor)
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree != null)
            {
                // During _Ready the root window rejects a direct add_child
                // ("busy setting up children"), so adopt through the queue.
                if (_popup.GetParent() == null)
                    tree.Root.CallDeferred(Node.MethodName.AddChild, _popup);

                // A freshly rebuilt Inspector (selection change, or the button
                // click itself re-rendering the view) is sorted on the next
                // frame; reading the anchor rect immediately would return its
                // pre-layout origin and drop the menu at the panel's top-left.
                await _popup.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            if (!GodotObject.IsInstanceValid(_popup)) return;

            Vector2 pos = Vector2.Zero;
            if (anchor.NativeControl is Control control && GodotObject.IsInstanceValid(control))
            {
                var rect = control.GetGlobalRect();
                pos = new Vector2(rect.Position.X, rect.End.Y);
            }
            Show(pos.X, pos.Y);
        }

        private void AdoptToRootWindow()
        {
            if (_attached || _popup.GetParent() != null) return;
            var tree = Engine.GetMainLoop() as SceneTree;
            tree?.Root.AddChild(_popup);
            _attached = true;
        }

        private void OnIdPressed(long id)
        {
            if (id >= 0 && id < _actions.Count)
                _actions[(int)id]?.Invoke();

            // Submenu picks keep the root popup open by default; the whole
            // menu is one action surface, so dismiss the chain.
            _root?._popup.Hide();
        }

        private void OnPopupHide()
        {
            // The shown menu is adopted onto the window and never removed, so free
            // the root (and its submenu children) once it hides to stop detached
            // PopupMenu nodes accumulating across opens. We must NOT clear
            // _actions on hide: on a submenu pick popup_hide fires before
            // id_pressed, and clearing drops the click's callback.
            if (_root == null)
                _popup.QueueFree();
        }
    }
}
