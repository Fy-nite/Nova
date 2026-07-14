using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Adds a fog volume to the scene with configurable color, density, height, and falloff.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/fog.svg")]
public partial class V12Fog : Node, IV12ComponentNode
{
    [ExportGroup("Color")]
    [Export] public float ColorR { get; set; } = 0.8f;
    [Export] public float ColorG { get; set; } = 0.8f;
    [Export] public float ColorB { get; set; } = 0.8f;

    [ExportGroup("Volume")]
    [Export] public float Density { get; set; } = 0.1f;
    [Export] public float FogHeight { get; set; } = 10.0f;
    [Export] public float HeightFalloff { get; set; } = 1.0f;

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<FogComponent" +
               $" colorR=\"{ColorR:F3}\" colorG=\"{ColorG:F3}\" colorB=\"{ColorB:F3}\"" +
               $" density=\"{Density:F4}\"" +
               $" fogHeight=\"{FogHeight:F3}\"" +
               $" heightFalloff=\"{HeightFalloff:F3}\"" +
               $" />\n";
    }
}
