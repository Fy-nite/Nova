using Godot;

namespace V12TwoDog.Editor.Nodes;

/// <summary>Adds a physics collision shape (Box/Sphere/Capsule/Cylinder) to an element, with optional trigger mode.</summary>
[Tool]
[GlobalClass]
[Icon("res://addons/at-icons/node3d/shield.svg")]
public partial class V12Collision : Node3D, IV12ComponentNode
{
    private const string GizmoName = "_V12Gizmo";
    private MeshInstance3D _gizmo;

    public enum CollisionShape
    {
        Box,
        Sphere,
        Capsule,
        Cylinder,
        Plane
    }

    private CollisionShape _shape = CollisionShape.Box;
    private float _width = 1.0f;
    private float _height = 1.0f;
    private float _depth = 1.0f;
    private bool _isTrigger;

    [Export] public CollisionShape Shape
    {
        get => _shape;
        set { _shape = value; UpdateGizmo(); }
    }

    [ExportGroup("Dimensions")]
    [Export] public float Width
    {
        get => _width;
        set { _width = value; UpdateGizmo(); }
    }
    [Export] public float Height
    {
        get => _height;
        set { _height = value; UpdateGizmo(); }
    }
    [Export] public float Depth
    {
        get => _depth;
        set { _depth = value; UpdateGizmo(); }
    }

    [ExportGroup("Behavior")]
    [Export] public bool IsTrigger
    {
        get => _isTrigger;
        set { _isTrigger = value; }
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
            var mat = new StandardMaterial3D();
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            _gizmo.MaterialOverride = mat;
            AddChild(_gizmo);
        }

        var mat2 = (StandardMaterial3D)_gizmo.MaterialOverride;
        mat2.AlbedoColor = new Color(1.0f, 0.5f, 0.0f, 0.25f);

        switch (_shape)
        {
            case CollisionShape.Sphere:
                _gizmo.Mesh = new SphereMesh { Radius = _width * 0.5f };
                break;
            case CollisionShape.Capsule:
                _gizmo.Mesh = new CapsuleMesh { Radius = _width * 0.5f, Height = _height };
                break;
            case CollisionShape.Cylinder:
                _gizmo.Mesh = new CylinderMesh { TopRadius = _width * 0.5f, BottomRadius = _width * 0.5f, Height = _height };
                break;
            default:
                _gizmo.Mesh = new BoxMesh { Size = new Vector3(_width, _height, _depth) };
                break;
        }
    }

    public string GetV12ComponentXml(string indent)
    {
        return $"{indent}\t<ColliderComponent" +
               $" Shape=\"{_shape}\"" +
               $" Width=\"{_width:F3}\"" +
               $" Height=\"{_height:F3}\"" +
               $" Depth=\"{_depth:F3}\"" +
               $" IsTrigger=\"{_isTrigger.ToString().ToLower()}\"" +
               $" />\n";
    }
}
