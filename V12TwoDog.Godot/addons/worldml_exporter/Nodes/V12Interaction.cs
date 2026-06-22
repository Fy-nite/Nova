using Godot;

namespace V12TwoDog.Editor.Nodes;

[Tool]
[Icon("res://addons/at-icons/node3d/hand.svg")]
public partial class V12Interaction : Node, IV12ComponentNode
{
    [Export] public bool IsPointing { get; set; }
    [Export] public bool IsSelecting { get; set; }

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<InteractionComponent" +
               $" isPointing=\"{IsPointing.ToString().ToLower()}\"" +
               $" isSelecting=\"{IsSelecting.ToString().ToLower()}\"" +
               $" />\n";
    }
}
