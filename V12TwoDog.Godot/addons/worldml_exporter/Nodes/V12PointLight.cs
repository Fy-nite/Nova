using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Omnidirectional point light with configurable color, range, and energy.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/lightbulb.svg")]
public partial class V12PointLight : Node3D, IV12ComponentNode
{
    private const string GizmoName = "_V12Gizmo";
    private MeshInstance3D _gizmo;

    private float _colorR = 1f;
    private float _colorG = 1f;
    private float _colorB = 1f;
    private float _range = 10f;
    private float _energy = 1f;

    [ExportGroup("Color")]
    [Export(PropertyHint.Range, "0,1")]
    public float ColorR
    {
        get => _colorR;
        set { _colorR = value; UpdateGizmo(); }
    }
    [Export(PropertyHint.Range, "0,1")]
    public float ColorG
    {
        get => _colorG;
        set { _colorG = value; UpdateGizmo(); }
    }
    [Export(PropertyHint.Range, "0,1")]
    public float ColorB
    {
        get => _colorB;
        set { _colorB = value; UpdateGizmo(); }
    }

    [ExportGroup("Settings")]
    [Export] public float Range
    {
        get => _range;
        set { _range = value; }
    }
    [Export] public float Energy
    {
        get => _energy;
        set { _energy = value; }
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            UpdateGizmo();
        else
            QueueFree();
    }

    private void UpdateGizmo()
    {
        if (_gizmo == null || !IsInstanceValid(_gizmo))
        {
            if (!Engine.IsEditorHint()) return;
            _gizmo = new MeshInstance3D();
            _gizmo.Name = GizmoName;
            _gizmo.Mesh = new SphereMesh { Radius = 0.25f };
            var mat = new StandardMaterial3D();
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.EmissionEnabled = true;
            mat.EmissionEnergyMultiplier = 2.0f;
            _gizmo.MaterialOverride = mat;
            AddChild(_gizmo);
        }

        var mat2 = (StandardMaterial3D)_gizmo.MaterialOverride;
        mat2.AlbedoColor = new Color(_colorR, _colorG, _colorB, 0.7f);
        mat2.Emission = new Color(_colorR, _colorG, _colorB);
    }

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<PointLightComponent" +
               $" ColorR=\"{_colorR:F3}\" ColorG=\"{_colorG:F3}\" ColorB=\"{_colorB:F3}\"" +
               $" Range=\"{_range:F1}\"" +
               $" Energy=\"{_energy:F2}\"" +
               $" />\n";
    }
}
