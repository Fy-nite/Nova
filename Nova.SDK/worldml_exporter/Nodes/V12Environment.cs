using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Configures the world environment: sky color, ambient lighting, and optional skybox.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/globe.svg")]
public partial class V12Environment : Node, IV12ComponentNode
{
    public enum BackgroundMode
    {
        SolidColor = 0,
        Skybox = 1
    }

    [Export] public BackgroundMode Mode { get; set; } = BackgroundMode.SolidColor;

    [ExportGroup("Sky Color")]
    [Export] public float SkyR { get; set; } = 0.5f;
    [Export] public float SkyG { get; set; } = 0.6f;
    [Export] public float SkyB { get; set; } = 0.8f;

    [ExportGroup("Ambient Light")]
    [Export] public float AmbientR { get; set; } = 0.2f;
    [Export] public float AmbientG { get; set; } = 0.2f;
    [Export] public float AmbientB { get; set; } = 0.25f;
    [Export] public float AmbientEnergy { get; set; } = 1.0f;

    [ExportGroup("Skybox")]
    [Export] public string SkyboxPath { get; set; } = "";

    public string GetV12ComponentXml(string indent)
    {
        var modeStr = Mode switch
        {
            BackgroundMode.Skybox => "Skybox",
            _ => "SolidColor"
        };

        return $"{indent}\t<EnvironmentComponent" +
               $" mode=\"{modeStr}\"" +
               $" skyR=\"{SkyR:F3}\" skyG=\"{SkyG:F3}\" skyB=\"{SkyB:F3}\"" +
               $" ambientR=\"{AmbientR:F3}\" ambientG=\"{AmbientG:F3}\" ambientB=\"{AmbientB:F3}\"" +
               $" ambientEnergy=\"{AmbientEnergy:F3}\"" +
               $" skyboxPath=\"{SkyboxPath}\"" +
               $" />\n";
    }
}
