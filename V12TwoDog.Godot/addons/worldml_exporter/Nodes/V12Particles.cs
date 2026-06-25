using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>GPU particle emitter with configurable amount, lifetime, speed, direction, and spread.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/stars.svg")]
public partial class V12Particles : Node3D, IV12ComponentNode
{
    private const string GizmoName = "_V12Gizmo";

    [Export] public int Amount { get; set; } = 32;
    [Export] public float Lifetime { get; set; } = 2.0f;

    [ExportGroup("Speed")]
    [Export] public float SpeedMin { get; set; } = 1.0f;
    [Export] public float SpeedMax { get; set; } = 3.0f;

    [ExportGroup("Direction")]
    [Export] public float DirX { get; set; }
    [Export] public float DirY { get; set; } = 1.0f;
    [Export] public float DirZ { get; set; }
    [Export(PropertyHint.Range, "0,180")]
    public float SpreadAngle { get; set; } = 45.0f;

    [ExportGroup("Emission")]
    [Export] public float EmissionRadius { get; set; }
    [Export] public bool Emitting { get; set; } = true;
    [Export] public bool OneShot { get; set; }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            EnsureGizmo();
        else
            QueueFree();
    }

    private void EnsureGizmo()
    {
        if (HasNode(GizmoName)) return;
        var gizmo = new MeshInstance3D();
        gizmo.Name = GizmoName;
        gizmo.Mesh = new SphereMesh { Radius = 0.2f };
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.8f, 0.2f, 0.9f, 0.6f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.EmissionEnabled = true;
        mat.Emission = new Color(0.8f, 0.2f, 0.9f);
        gizmo.MaterialOverride = mat;
        AddChild(gizmo);

        for (int i = 0; i < 6; i++)
        {
            var dot = new MeshInstance3D();
            dot.Name = $"{GizmoName}_dot{i}";
            dot.Mesh = new SphereMesh { Radius = 0.06f };
            var dotMat = new StandardMaterial3D();
            dotMat.AlbedoColor = new Color(0.6f + i * 0.05f, 0.3f, 1.0f, 0.5f);
            dotMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            dot.MaterialOverride = dotMat;
            var angle = i * Mathf.Pi * 2f / 6f;
            dot.Position = new Vector3(Mathf.Cos(angle) * 0.4f, Mathf.Sin(angle) * 0.4f, 0f);
            AddChild(dot);
        }
    }

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<ParticleEmitterComponent" +
               $" amount=\"{Amount}\"" +
               $" lifetime=\"{Lifetime:F3}\"" +
               $" emissionRadius=\"{EmissionRadius:F4}\"" +
               $" speedMin=\"{SpeedMin:F3}\"" +
               $" speedMax=\"{SpeedMax:F3}\"" +
               $" dirX=\"{DirX:F4}\" dirY=\"{DirY:F4}\" dirZ=\"{DirZ:F4}\"" +
               $" spreadAngle=\"{SpreadAngle:F2}\"" +
               $" emitting=\"{Emitting.ToString().ToLower()}\"" +
               $" oneShot=\"{OneShot.ToString().ToLower()}\"" +
               $" />\n";
    }
}
