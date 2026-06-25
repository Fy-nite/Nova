using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Plays a 3D positional audio clip with configurable volume, pitch, looping, and auto-play.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/speaker.svg")]
public partial class V12AudioSource : Node3D, IV12ComponentNode
{
    private const string GizmoName = "_V12Gizmo";

    [Export(PropertyHint.File, "*.ogg,*.mp3,*.wav")]
    public string AudioClipPath { get; set; } = "";

    [ExportGroup("Playback")]
    [Export(PropertyHint.Range, "0,1")]
    public float Volume { get; set; } = 1.0f;
    [Export] public float Pitch { get; set; } = 1.0f;
    [Export] public bool Loop { get; set; }
    [Export] public bool Autoplay { get; set; }
    [Export] public float MaxDistance { get; set; } = 40.0f;

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
        gizmo.Mesh = new SphereMesh { Radius = 0.3f };
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.2f, 0.5f, 1.0f, 0.5f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        gizmo.MaterialOverride = mat;
        AddChild(gizmo);
    }

    public string GetV12ComponentXml(string indent)
    {
        var clip = AudioClipPath.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        return $"{indent}\t<AudioSourceComponent" +
               $" AudioClipPath=\"{clip}\"" +
               $" Volume=\"{Volume:F2}\"" +
               $" Pitch=\"{Pitch:F3}\"" +
               $" Loop=\"{Loop.ToString().ToLower()}\"" +
               $" Autoplay=\"{Autoplay.ToString().ToLower()}\"" +
               $" MaxDistance=\"{MaxDistance:F2}\"" +
               $" />\n";
    }
}
