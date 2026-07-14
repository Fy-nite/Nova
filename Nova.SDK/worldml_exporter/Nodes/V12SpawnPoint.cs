using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Designates a spawn location where the player (or XR rig) will be placed when the world loads.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/location.svg")]
public partial class V12SpawnPoint : Node3D, IV12ComponentNode
{
    private const string GizmoName = "_V12Gizmo";

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
        gizmo.Mesh = new SphereMesh();
        gizmo.Scale = Vector3.One * 0.35f;
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = Colors.Green;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.AlbedoColor = new Color(0, 1, 0, 0.5f);
        gizmo.MaterialOverride = mat;
        AddChild(gizmo);
    }

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<SpawnPointComponent />\n";
    }
}
