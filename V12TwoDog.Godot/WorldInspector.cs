#nullable enable
using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using V12.Core;
using V12.Core.Core.Interfaces;
using NumVector3 = System.Numerics.Vector3;
using NumQuaternion = System.Numerics.Quaternion;

namespace V12TwoDog
{
    public class WorldInspector
    {
        private readonly GameRoot _root;
        private IWorldElement? _selectedElement;
        private readonly HashSet<long> _expandedElements = new();
        private readonly HashSet<long> _expandedComponents = new();
        
        private IComponent? _editingComponent;
        private readonly Dictionary<string, object> _pendingEdits = new();
        private bool _hasPendingEdits;

        /// <summary>
        /// Queue of edit actions to be processed on the V12 worker thread.
        /// This avoids races between the Godot main thread (where the inspector runs)
        /// and the V12 simulation thread.
        /// </summary>
        internal static ConcurrentQueue<Action> PendingEditActions = new();

        public WorldInspector(GameRoot root)
        {
            _root = root;
        }

        public void OnLayout()
        {
            if (ImGui.Begin("World Inspector"))
            {
                var world = _root.SelectedWorld;
                if (world == null)
                {
                    ImGui.Text("No world selected");
                    ImGui.End();
                    return;
                }

                ImGui.TextColored(new Color(1, 1, 0), $"World: {world.WorldName}");
                ImGui.Separator();

                ImGui.Text($"Root elements: {world.Root.Count}");
                ImGui.Spacing();

                float halfWidth = ImGui.GetContentRegionAvail().X * 0.5f;
                
                if (ImGui.BeginChild("ElementTree", halfWidth, 0))
                {
                    foreach (var element in world.Root)
                    {
                        RenderElementTree(element, 0);
                    }
                }
                ImGui.EndChild();

                ImGui.SameLine();

                if (ImGui.BeginChild("DetailsPanel", halfWidth, 0))
                {
                    if (_selectedElement != null)
                    {
                        RenderElementDetails();
                    }
                    else
                    {
                        ImGui.Text("No element selected");
                    }
                }
                ImGui.EndChild();
            }
            ImGui.End();
        }

        private void RenderElementDetails()
        {
            ImGui.TextColored(new Color(0, 1, 1), $"Selected: {_selectedElement!.Name}");
            ImGui.Text($"ID: {_selectedElement.Id}");
            
            bool active = _selectedElement.Active;
            bool newActive = ImGui.Checkbox("Active", active);
            if (newActive != active)
            {
                _selectedElement.Active = newActive;
            }

            var lt = _selectedElement.LocalTransform;
            ImGui.Text($"Position: ({lt.Position.X:F2}, {lt.Position.Y:F2}, {lt.Position.Z:F2})");
            ImGui.Text($"Rotation: ({lt.Rotation.X:F2}, {lt.Rotation.Y:F2}, {lt.Rotation.Z:F2}, {lt.Rotation.W:F2})");

            if (_selectedElement.Parent != null)
                ImGui.Text($"Parent: {_selectedElement.Parent.Name}");
            else
                ImGui.Text("Parent: (root)");

            ImGui.Text($"Children: {_selectedElement.Children.Count}");

            ImGui.Separator();
            ImGui.Text("Components:");
            ImGui.Spacing();

            foreach (var comp in _selectedElement.Components)
            {
                RenderComponentEditor(comp);
            }

            if (_hasPendingEdits)
            {
                ImGui.Separator();
                if (ImGui.Button("Apply Changes"))
                {
                    ApplyPendingEdits();
                }
                ImGui.SameLine();
                if (ImGui.Button("Discard"))
                {
                    DiscardPendingEdits();
                }
            }
        }

        private void RenderComponentEditor(IComponent component)
        {
            string compName = component.GetType().Name;
            bool isExpanded = _expandedComponents.Contains(component.GetHashCode());
            bool isEditing = _editingComponent == component;

            if (isEditing)
            {
                ImGui.TextColored(new Color(1, 1, 0), $"▶ {compName}");
            }
            else
            {
                ImGui.Text(isExpanded ? "▼ " + compName : "▶ " + compName);
            }

            if (ImGui.IsItemClicked())
            {
                if (isExpanded)
                    _expandedComponents.Remove(component.GetHashCode());
                else
                    _expandedComponents.Add(component.GetHashCode());
                
                if (isEditing)
                {
                    DiscardPendingEdits();
                }
                else
                {
                    _editingComponent = component;
                    _hasPendingEdits = false;
                    _pendingEdits.Clear();
                }
            }

            if (isExpanded)
            {
                ImGui.Indent();
                
                if (isEditing)
                {
                    RenderEditableProperties(component);
                }
                else
                {
                    RenderReadOnlyProperties(component);
                }
                
                ImGui.Unindent();
            }
        }

        private void RenderReadOnlyProperties(IComponent component)
        {
            var props = component.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!prop.CanRead) continue;
                if (prop.Name == "Name" || prop.Name == "Description" || prop.Name == "Owner") continue;

                try
                {
                    var value = prop.GetValue(component);
                    ImGui.Text($"{prop.Name}: {FormatValue(value)}");
                }
                catch
                {
                    ImGui.Text($"{prop.Name}: <error>");
                }
            }
        }

        private void RenderEditableProperties(IComponent component)
        {
            var props = component.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.Name == "Name" || prop.Name == "Description" || prop.Name == "Owner") continue;

                try
                {
                    var currentValue = prop.GetValue(component);
                    string key = $"{component.GetHashCode()}_{prop.Name}";
                    
                    if (!_pendingEdits.ContainsKey(key))
                    {
                        _pendingEdits[key] = currentValue!;
                    }

                    var editValue = _pendingEdits[key];
                    bool changed = false;

                    if (prop.PropertyType == typeof(float))
                    {
                        float val = (float)editValue;
                        float newVal = ImGui.InputFloat(prop.Name, val);
                        if (newVal != val)
                        {
                            _pendingEdits[key] = newVal;
                            changed = true;
                        }
                    }
                    else if (prop.PropertyType == typeof(int))
                    {
                        int val = (int)editValue;
                        int newVal = ImGui.InputInt(prop.Name, val);
                        if (newVal != val)
                        {
                            _pendingEdits[key] = newVal;
                            changed = true;
                        }
                    }
                    else if (prop.PropertyType == typeof(bool))
                    {
                        bool val = (bool)editValue;
                        bool newVal = ImGui.Checkbox(prop.Name, val);
                        if (newVal != val)
                        {
                            _pendingEdits[key] = newVal;
                            changed = true;
                        }
                    }
                    else if (prop.PropertyType == typeof(string))
                    {
                        string val = (string)editValue;
                        string newVal = ImGui.InputText(prop.Name, val, 256, ImGui.InputTextEnterReturnsTrue);
                        if (newVal != val)
                        {
                            _pendingEdits[key] = newVal;
                            changed = true;
                        }
                    }
                    else if (prop.PropertyType == typeof(NumVector3))
                    {
                        var val = (NumVector3)editValue;
                        var godotVal = new Vector3(val.X, val.Y, val.Z);
                        var newGodotVal = ImGui.InputFloat3(prop.Name, godotVal);
                        if (newGodotVal != godotVal)
                        {
                            _pendingEdits[key] = new NumVector3(newGodotVal.X, newGodotVal.Y, newGodotVal.Z);
                            changed = true;
                        }
                    }
                    else if (prop.PropertyType == typeof(NumQuaternion))
                    {
                        var val = (NumQuaternion)editValue;
                        var godotVal = new Vector4(val.X, val.Y, val.Z, val.W);
                        var newGodotVal = ImGui.InputFloat4(prop.Name, godotVal);
                        if (newGodotVal != godotVal)
                        {
                            _pendingEdits[key] = new NumQuaternion(newGodotVal.X, newGodotVal.Y, newGodotVal.Z, newGodotVal.W);
                            changed = true;
                        }
                    }
                    else
                    {
                        ImGui.Text($"{prop.Name}: {FormatValue(currentValue)} (read-only)");
                    }

                    if (changed)
                    {
                        _hasPendingEdits = true;
                    }
                }
                catch
                {
                    ImGui.Text($"{prop.Name}: <error>");
                }
            }
        }

        private void ApplyPendingEdits()
        {
            if (_editingComponent == null) return;

            // Snapshot the pending data before it could be modified
            var component = _editingComponent;
            var pendingCopy = new Dictionary<string, object>(_pendingEdits);

            PendingEditActions.Enqueue(() =>
            {
                foreach (var kvp in pendingCopy)
                {
                    string[] parts = kvp.Key.Split('_');
                    if (parts.Length != 2) continue;

                    string propName = parts[1];
                    var prop = component.GetType().GetProperty(propName);
                    if (prop != null && prop.CanWrite)
                    {
                        try
                        {
                            prop.SetValue(component, kvp.Value);
                        }
                        catch
                        {
                            GD.PrintErr($"Failed to set {propName}");
                        }
                    }
                }
            });

            DiscardPendingEdits();
        }

        private void DiscardPendingEdits()
        {
            _editingComponent = null;
            _pendingEdits.Clear();
            _hasPendingEdits = false;
        }

        private string FormatValue(object? value)
        {
            if (value == null) return "null";
            
            if (value is NumVector3 v3)
                return $"({v3.X:F2}, {v3.Y:F2}, {v3.Z:F2})";
            if (value is NumQuaternion q)
                return $"({q.X:F2}, {q.Y:F2}, {q.Z:F2}, {q.W:F2})";
            if (value is float f)
                return f.ToString("F2");
            
            return value.ToString() ?? "null";
        }

        private void RenderElementTree(IWorldElement element, int depth)
        {
            bool hasChildren = element.Children.Count > 0;
            bool isExpanded = _expandedElements.Contains(element.Id);

            string prefix = hasChildren ? (isExpanded ? "▼ " : "▶ ") : "  ";
            string label = $"{prefix}{element.Name}";

            bool isSelected = _selectedElement == element;
            if (isSelected)
            {
                ImGui.TextColored(new Color(0, 1, 0), label);
            }
            else
            {
                ImGui.Text(label);
            }

            if (ImGui.IsItemClicked())
            {
                _selectedElement = element;
                _editingComponent = null;
                _pendingEdits.Clear();
                _hasPendingEdits = false;
                
                if (hasChildren)
                {
                    if (isExpanded)
                        _expandedElements.Remove(element.Id);
                    else
                        _expandedElements.Add(element.Id);
                }
            }

            if (hasChildren && isExpanded)
            {
                ImGui.Indent();
                foreach (var child in element.Children)
                {
                    RenderElementTree(child, depth + 1);
                }
                ImGui.Unindent();
            }
        }
    }
}
