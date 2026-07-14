using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Tags an element with a comma-separated string of identifiers for filtering and categorization.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/tag.svg")]
public partial class V12Tag : Node, IV12ComponentNode
{
    [Export] public string Tags { get; set; } = "";

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<TagComponent tags=\"{Tags.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" />\n";
    }
}
