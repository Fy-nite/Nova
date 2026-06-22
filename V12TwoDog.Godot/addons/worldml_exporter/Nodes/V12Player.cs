using Godot;

namespace V12TwoDog.Editor.Nodes;

[Tool]
[Icon("res://addons/at-icons/node3d/target.svg")]
public partial class V12Player : Node3D, IV12ComponentNode
{
    private const string GizmoName = "_V12Gizmo";

    public enum InputMode
    {
        Auto = 0,
        Desktop = 1,
        Xr = 2
    }

    [Export] public InputMode PreferredInputMethod { get; set; } = InputMode.Auto;

    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 4.0f;
    [Export] public float SprintMultiplier { get; set; } = 1.9f;
    [Export] public float JumpStrength { get; set; } = 6.0f;

    [ExportGroup("Desktop")]
    [Export] public float LookSensitivity { get; set; } = 1.2f;
    [Export] public bool CanJump { get; set; } = true;

    [ExportGroup("VR")]
    [Export] public bool EnableHandTracking { get; set; } = true;
    [Export] public float VrMoveSpeed { get; set; } = 3.0f;
    [Export] public bool VrSmoothLocomotion { get; set; } = true;

    public override void _Ready()
    {
        if (Engine.IsEditorHint() && !HasNode(GizmoName))
            EnsureGizmo();
    }

    private void EnsureGizmo()
    {
        var gizmo = new MeshInstance3D();
        gizmo.Name = GizmoName;
        gizmo.Mesh = new CapsuleMesh();
        gizmo.Scale = new Vector3(0.4f, 0.9f, 0.4f);
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f, 0.4f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        gizmo.MaterialOverride = mat;
        AddChild(gizmo);
        gizmo.Owner = GetTree()?.EditedSceneRoot;
    }

    public string GetV12ComponentXml(string indent)
    {
        var methodStr = PreferredInputMethod switch
        {
            InputMode.Desktop => "Desktop",
            InputMode.Xr => "XR",
            _ => "Auto"
        };

        return $"{indent}\t<PlayerComponent" +
               $" preferredInputMethod=\"{methodStr}\"" +
               $" moveSpeed=\"{MoveSpeed:F3}\"" +
               $" sprintMultiplier=\"{SprintMultiplier:F3}\"" +
               $" jumpStrength=\"{JumpStrength:F3}\"" +
               $" lookSensitivity=\"{LookSensitivity:F3}\"" +
               $" canJump=\"{CanJump.ToString().ToLower()}\"" +
               $" enableHandTracking=\"{EnableHandTracking.ToString().ToLower()}\"" +
               $" vrMoveSpeed=\"{VrMoveSpeed:F3}\"" +
               $" vrSmoothLocomotion=\"{VrSmoothLocomotion.ToString().ToLower()}\"" +
               $" />\n";
    }
}
