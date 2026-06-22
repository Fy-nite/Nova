using Godot;

namespace V12TwoDog.Editor.Nodes;

[Tool]
[Icon("res://addons/at-icons/node3d/wind.svg")]
public partial class V12Velocity : Node, IV12ComponentNode
{
    [ExportGroup("Linear Velocity")]
    [Export] public float VelX { get; set; }
    [Export] public float VelY { get; set; }
    [Export] public float VelZ { get; set; }

    [ExportGroup("Angular Velocity (deg/s)")]
    [Export] public float AngX { get; set; }
    [Export] public float AngY { get; set; }
    [Export] public float AngZ { get; set; }

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<VelocityComponent" +
               $" velX=\"{VelX:F4}\" velY=\"{VelY:F4}\" velZ=\"{VelZ:F4}\"" +
               $" angX=\"{AngX:F4}\" angY=\"{AngY:F4}\" angZ=\"{AngZ:F4}\"" +
               $" />\n";
    }
}
