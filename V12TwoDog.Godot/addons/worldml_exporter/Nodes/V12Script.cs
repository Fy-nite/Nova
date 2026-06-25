using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Attaches a Lua script to an element. Provide a file path or inline script text.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/script.svg")]
public partial class V12Script : Node, IV12ComponentNode
{
    [Export(PropertyHint.File, "*.lua")]
    public string Source { get; set; } = "";

    [Export(PropertyHint.MultilineText)]
    public string ScriptText { get; set; } = "";

    public string GetV12ComponentXml(string indent)
    {
        if (!string.IsNullOrEmpty(Source))
            return $"{indent}\t<Component type=\"ScriptComponent\" src=\"{Source.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\" />\n";

        if (!string.IsNullOrEmpty(ScriptText))
        {
            var escaped = ScriptText.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
            return $"{indent}\t<Component type=\"ScriptComponent\"><![CDATA[{escaped}]]></Component>\n";
        }

        return "";
    }
}
