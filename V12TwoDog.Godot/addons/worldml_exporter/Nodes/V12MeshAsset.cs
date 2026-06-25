using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>References an external GLTF/GLB/OBJ mesh file and applies local transform offsets.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/box.svg")]
public partial class V12MeshAsset : Node, IV12ComponentNode
{
    [Export(PropertyHint.File, "*.glb,*.gltf,*.obj")]
    public string AssetPath { get; set; } = "";

    [ExportGroup("Local Offset")]
    [Export] public float PositionX { get; set; }
    [Export] public float PositionY { get; set; }
    [Export] public float PositionZ { get; set; }

    [ExportGroup("Local Rotation (degrees)")]
    [Export] public float RotationX { get; set; }
    [Export] public float RotationY { get; set; }
    [Export] public float RotationZ { get; set; }

    [ExportGroup("Local Scale")]
    [Export] public float ScaleX { get; set; } = 1.0f;
    [Export] public float ScaleY { get; set; } = 1.0f;
    [Export] public float ScaleZ { get; set; } = 1.0f;

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<Component type=\"MeshAsset\"" +
               $" assetPath=\"{AssetPath.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")}\"" +
               $" positionX=\"{PositionX:F3}\" positionY=\"{PositionY:F3}\" positionZ=\"{PositionZ:F3}\"" +
               $" rotationX=\"{RotationX:F3}\" rotationY=\"{RotationY:F3}\" rotationZ=\"{RotationZ:F3}\"" +
               $" scaleX=\"{ScaleX:F3}\" scaleY=\"{ScaleY:F3}\" scaleZ=\"{ScaleZ:F3}\"" +
               $" />\n";
    }
}
