using System;
using System.Collections.Generic;
using Godot;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Components;
using V12.Basic.Components;

namespace V12TwoDog
{
    public class DebugGameService : IGameService
    {
        private GameRoot _gameRoot;
        private Node3D _debugSlot;
        private World _currentWorld;
        public bool _needsUpdate = true;

        public void Initialize(GameRoot g)
        {
            _gameRoot = g;
            CheckWorldBinding();
        }

        public void Update(float deltaTime)
        {
            CheckWorldBinding();
            UpdateDebugNodes();
        }

        public void Update(GameRoot gameRoot)
        {
            _gameRoot = gameRoot;
            CheckWorldBinding();
            UpdateDebugNodes();
        }

        private void CheckWorldBinding()
        {
            if (_gameRoot != null && _gameRoot.SelectedWorld != _currentWorld)
            {
                BindWorldEvents(_gameRoot.SelectedWorld);
            }
        }

        private void BindWorldEvents(World world)
        {
            if (_currentWorld != null)
            {
                _currentWorld.ElementAdded -= OnWorldHierarchyChanged;
                _currentWorld.ElementRemoved -= OnWorldHierarchyChanged;
            }

            _currentWorld = world;

            if (_currentWorld != null)
            {
                _currentWorld.ElementAdded += OnWorldHierarchyChanged;
                _currentWorld.ElementRemoved += OnWorldHierarchyChanged;
            }

            _needsUpdate = true;
        }

        private void OnWorldHierarchyChanged(IWorldElement element)
        {
            _needsUpdate = true;
        }

        private void UpdateDebugNodes()
        {
            if (_gameRoot == null) return;

            // Get the Godot SceneTree from the Renderer
            var renderer = _gameRoot.Registry.Get("IRenderer")?.ServiceInstance as V12TwoDog.Renderer;
            if (renderer == null || renderer.root == null || renderer.root.CurrentScene == null)
            {
                return;
            }

            var currentScene = renderer.root.CurrentScene;

            // Find or create the "debug" slot Node3D
            if (!GodotObject.IsInstanceValid(_debugSlot) || _debugSlot.GetParent() == null)
            {
                _debugSlot = currentScene.GetNodeOrNull<Node3D>("debug");
                if (_debugSlot == null)
                {
                    _debugSlot = new Node3D();
                    _debugSlot.Name = "debug";
                    _debugSlot.Position = new Vector3(5f, 0f, 0f);
                    currentScene.AddChild(_debugSlot);
                }
            }

            // Gather component details from the selected world in GameRoot
            var infoLines = new List<string>();
            if (_gameRoot.SelectedWorld != null)
            {
                infoLines.Add($"World: {_gameRoot.SelectedWorld.WorldName}");

                void Traverse(IWorldElement element, string indent)
                {
                    if (element == null) return;
                    
                    infoLines.Add($"{indent}Element: {element.Name} (ID: {element.Id})");
                    foreach (var comp in element.Components)
                    {
                        bool active = comp is ComponentBase cb ? cb.Active : true;
                        infoLines.Add($"{indent}  - Component: {comp.GetType().Name} (Name: {comp.Name}, Active: {active})");

                        if (comp is TransformComponent tc)
                        {
                            infoLines.Add($"{indent}      Pos=({tc.X:F2},{tc.Y:F2},{tc.Z:F2}) RotX={tc.RotationX?.ToString("F2")??"null"} RotY={tc.RotationY?.ToString("F2")??"null"} RotZ={tc.RotationZ?.ToString("F2")??"null"} RY={tc.RY:F4}");
                        }
                        else if (comp is LocomotionComponent lc)
                        {
                            infoLines.Add($"{indent}      Vel=({lc.Velocity.X:F4},{lc.Velocity.Y:F4},{lc.Velocity.Z:F4}) Grounded={lc.IsGrounded} CanJump={lc.CanJump}");
                        }
                        else if (comp is PhysicsBodyComponent pbc)
                        {
                            infoLines.Add($"{indent}      BodyHandle={pbc.BodyHandle.Value} IsKinematic={pbc.IsKinematic}");
                        }
                    }

                    if (element.Children != null)
                    {
                        foreach (var child in element.Children)
                        {
                            Traverse(child, indent + "    ");
                        }
                    }
                }

                foreach (var element in _gameRoot.SelectedWorld.Root)
                {
                    Traverse(element, "  ");
                }
            }
            else
            {
                infoLines.Add("No Selected World");
            }

            // Sync the Node3D children under the "debug" slot
            var existingChildren = _debugSlot.GetChildren();
            for (int i = 0; i < existingChildren.Count; i++)
            {
                _debugSlot.RemoveChild(existingChildren[i]);
                existingChildren[i].QueueFree();
            }

            // Create a Label3D for each line of component info to display inside the "debug" slot
            float yOffset = 0f;
            foreach (var line in infoLines)
            {
                var label = new Label3D();
                // Replace invalid node name characters with underscores
                string safeName = line.Replace("/", "_").Replace(".", "_").Replace(":", "_").Replace("-", "_").Replace(" ", "_").Trim();
                if (string.IsNullOrEmpty(safeName)) safeName = "Line";
                
                label.Name = safeName;
                label.Text = line;
                label.FontSize = 24;
                label.HorizontalAlignment = HorizontalAlignment.Left;
                label.Position = new Vector3(0f, yOffset, 0f);
                _debugSlot.AddChild(label);
                
                yOffset -= 0.3f; // Stack vertically downwards
            }
        }
    }
}
