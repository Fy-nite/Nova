using System;
using System.Collections.Generic;
using Godot;
using V12;
using V12.Core;
using V12.Core.GamePak;

namespace V12TwoDog
{
    /// <summary>
    /// ImGui-based launcher window that lists discovered game paks and lets the
    /// user pick one to launch. Follows the same pattern as NetworkInspector.
    /// </summary>
    /// TODO: update and fix the onlayout to support a proper launcher setup
    public class GamepakLauncher
    {
        private readonly GameRoot _root;
        private readonly GamepackLoader _loader;
        private int _selectedIndex = -1;
        private bool _visible = true;

        /// <summary>Raised when the user clicks "Launch" on a game pak.</summary>
        public event Action<IV12Gamepack> OnLaunch;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        public GamepakLauncher(GameRoot root, GamepackLoader loader)
        {
            _root = root;
            _loader = loader;
        }

        public void OnLayout()
        {
            if (!_visible) return;
        }
        //    if (ImGui.Begin("Game Pak Launcher"))
        //    {
        //        var paks = _loader.Gamepaks;

        //        if (paks.Count == 0)
        //        {
        //            ImGui.Text("No game paks found.");
        //            ImGui.Separator();
        //            ImGui.Text("Place game pak .dll files in the 'gamepaks/' directory.");
        //        }
        //        else
        //        {
        //            ImGui.Text($"Found {paks.Count} game pak(s):");
        //            ImGui.Separator();

        //            for (int i = 0; i < paks.Count; i++)
        //            {
        //                var pak = paks[i];
        //                bool isSelected = (_selectedIndex == i);

        //                if (ImGui.Selectable($"{pak.Name}##{i}", isSelected))
        //                    _selectedIndex = i;

        //                if (ImGui.IsItemHovered())
        //                    ImGui.SetTooltip(pak.GetType().FullName ?? "");
        //            }

        //            ImGui.Separator();

        //            if (_selectedIndex >= 0 && _selectedIndex < paks.Count)
        //            {
        //                var pak = paks[_selectedIndex];
        //                ImGui.Text($"Selected: {pak.Name}");
        //                ImGui.Text($"Type: {pak.GetType().FullName}");

        //                if (ImGui.Button("Launch"))
        //                {
        //                    GD.Print($"[Launcher] Launching '{pak.Name}'...");
        //                    _visible = false;
        //                    OnLaunch?.Invoke(pak);
        //                }
        //            }
        //            else
        //            {
        //                ImGui.TextDisabled("Select a game pak to launch");
        //            }
        //        }
        //    }
        //    ImGui.End();
        //}
    }
}
