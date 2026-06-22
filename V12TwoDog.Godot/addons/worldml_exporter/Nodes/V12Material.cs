using Godot;

namespace V12TwoDog.Editor.Nodes;

[Tool]
[Icon("res://addons/at-icons/node3d/cube.svg")]
public partial class V12Material : Node, IV12ComponentNode
{
    [ExportGroup("Albedo Color")]
    [Export] public float R { get; set; } = 1.0f;
    [Export] public float G { get; set; } = 1.0f;
    [Export] public float B { get; set; } = 1.0f;
    [Export] public float A { get; set; } = 1.0f;

    [ExportGroup("PBR")]
    [Export(PropertyHint.Range, "0,1")]
    public float Metallic { get; set; }
    [Export(PropertyHint.Range, "0,1")]
    public float Roughness { get; set; } = 0.5f;

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<MaterialComponent" +
               $" R=\"{R:F3}\" G=\"{G:F3}\" B=\"{B:F3}\" A=\"{A:F3}\"" +
               $" Metallic=\"{Metallic:F3}\"" +
               $" Roughness=\"{Roughness:F3}\"" +
               $" />\n";
    }
}
