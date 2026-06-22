using Godot;

namespace V12TwoDog.Editor.Nodes;

[Tool]
[Icon("res://addons/at-icons/node3d/sun.svg")]
public partial class V12DirectionalLight : Node, IV12ComponentNode
{
    [ExportGroup("Color")]
    [Export(PropertyHint.Range, "0,1")]
    public float ColorR { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,1")]
    public float ColorG { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,1")]
    public float ColorB { get; set; } = 1.0f;
    [Export] public float Energy { get; set; } = 1.0f;
    [Export] public bool ShadowEnabled { get; set; } = true;

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<GenericLightComponent" +
               $" colorR=\"{ColorR:F3}\" colorG=\"{ColorG:F3}\" colorB=\"{ColorB:F3}\"" +
               $" energy=\"{Energy:F3}\"" +
               $" shadowEnabled=\"{ShadowEnabled.ToString().ToLower()}\"" +
               $" />\n";
    }
}
