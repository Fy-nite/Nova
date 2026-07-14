using Godot;

using V12.Core.Systems;

/// <summary>
/// Renders a laser line from the pickup system's ray origin to its hit point,
/// with a sphere at the hit location.
/// </summary>
public class LaserVisual
{
    private MeshInstance3D _laserLine;
    private MeshInstance3D _laserHit;

    public void Initialize(Node3D parent)
    {
        _laserLine = new MeshInstance3D();
        _laserLine.Name = "LaserLine";
        _laserLine.Mesh = new ImmediateMesh();
        var laserMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0, 1, 0, 0.6f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
        _laserLine.MaterialOverride = laserMat;
        parent.AddChild(_laserLine);

        _laserHit = new MeshInstance3D();
        _laserHit.Name = "LaserHit";
        var hitMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0, 1, 0),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _laserHit.MaterialOverride = hitMat;
        parent.AddChild(_laserHit);
    }

    public void Update(PickupSystem pickup)
    {
        if (pickup == null || !pickup.HasRay) return;

        var origin = new Vector3(pickup.RayOrigin.X, pickup.RayOrigin.Y, pickup.RayOrigin.Z);
        var hit = new Vector3(pickup.RayHitPoint.X, pickup.RayHitPoint.Y, pickup.RayHitPoint.Z);

        if (_laserLine.Mesh is ImmediateMesh im)
        {
            im.ClearSurfaces();
            im.SurfaceBegin(Mesh.PrimitiveType.Lines);
            im.SurfaceAddVertex(origin);
            im.SurfaceAddVertex(hit);
            im.SurfaceEnd();
        }

        var color = pickup.RayHitSomething ? Colors.Green : Colors.Red;
        _laserHit.Visible = pickup.RayHitSomething;
        if (pickup.RayHitSomething)
        {
            if (_laserHit.Mesh is not SphereMesh sphereMesh)
            {
                sphereMesh = new SphereMesh { Radius = 0.08f, Height = 0.16f };
                _laserHit.Mesh = sphereMesh;
            }
            _laserHit.Position = hit;
            if (_laserHit.MaterialOverride is StandardMaterial3D mat)
                mat.AlbedoColor = color;
        }
    }
}
