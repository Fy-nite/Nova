using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Override surface appearance: albedo color, PBR parameters, and texture UV tiling/offset.</summary>
[Tool]
[GlobalClass]
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

    [ExportGroup("Texture UV")]
    [Export] public float Uv1OffsetX { get; set; }
    [Export] public float Uv1OffsetY { get; set; }
    [Export] public float Uv1ScaleX { get; set; } = 1.0f;
    [Export] public float Uv1ScaleY { get; set; } = 1.0f;

    public string GetV12ComponentXml(string indent)
    {
        var uvAttrs = "";
        if (Uv1OffsetX != 0f || Uv1OffsetY != 0f)
            uvAttrs += $" Uv1OffsetX=\"{Uv1OffsetX:F3}\" Uv1OffsetY=\"{Uv1OffsetY:F3}\"";
        if (Uv1ScaleX != 1f || Uv1ScaleY != 1f)
            uvAttrs += $" Uv1ScaleX=\"{Uv1ScaleX:F3}\" Uv1ScaleY=\"{Uv1ScaleY:F3}\"";

        return $"{indent}\t<MaterialComponent" +
               $" R=\"{R:F3}\" G=\"{G:F3}\" B=\"{B:F3}\" A=\"{A:F3}\"" +
               $" Metallic=\"{Metallic:F3}\"" +
               $" Roughness=\"{Roughness:F3}\"" +
               uvAttrs +
               $" />\n";
    }
}
