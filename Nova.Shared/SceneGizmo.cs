using System;
using System.Collections.Generic;
using Godot;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.UI;

namespace V12TwoDog
{
    /// <summary>
    /// Host-side transform gizmo rendered inside a UI scene viewport, with
    /// three modes:
    ///
    /// <list type="bullet">
    /// <item>Translate — world-axis arrows (X red / Y green / Z blue); dragging
    /// an arrow moves the element along that axis.</item>
    /// <item>Rotate — torus rings around the element; dragging a ring rotates
    /// it around that axis.</item>
    /// <item>Scale — axis cubes + a centre cube; dragging an axis cube scales
    /// along that axis, dragging the centre scales uniformly.</item>
    /// </list>
    ///
    /// The V12 world owns the truth: the gizmo writes back into the element's
    /// LocalTransform. While dragging, the target is pinned editor-driven and
    /// its render node is written directly so TRS input renders immediately
    /// instead of being batched through the 60Hz worker snapshot + interp
    /// pipeline.
    /// The gizmo's Godot nodes live directly under the viewport (outside the
    /// renderer's element-node map), so snapshot reconciliation never disturbs
    /// them.
    /// </summary>
    public sealed class SceneGizmo
    {
        private readonly Renderer _renderer;
        private Node3D? _root;
        private readonly Dictionary<int, MeshInstance3D> _handles = new();

        private IWorldElement? _target;
        private GizmoMode _mode = GizmoMode.Translate;
        private int _dragHandle = -1;
        private Camera3D? _dragCamera;

        // Drag start state per mode.
        private System.Numerics.Vector3 _startPosSN;
        private Vector3 _startHit;
        private System.Numerics.Quaternion _startRot;
        private System.Numerics.Vector3 _startScale;
        private float _startAngle;

        private static readonly Color[] AxisColors =
        {
            new(0.95f, 0.28f, 0.28f),
            new(0.35f, 0.85f, 0.4f),
            new(0.35f, 0.6f, 1f)
        };
        private static readonly Vector3[] AxisDirs = { Vector3.Right, Vector3.Up, Vector3.Forward };

        public SceneGizmo(Renderer renderer) => _renderer = renderer;

        public GizmoMode Mode => _mode;
        public bool HasTarget => _target != null;
        public bool IsDragging => _dragHandle >= 0;

        /// <summary>Show the gizmo around <paramref name="el"/> inside
        /// <paramref name="viewport"/>. Pass null to hide.</summary>
        public void SetTarget(IWorldElement? el, SubViewport viewport)
        {
            // A new target aborts any active drag; release the direct-drive pin
            // so the previous target returns to snapshot rendering.
            if (_dragHandle >= 0 && _target != null)
                _renderer.SetEditorDriven(_target.Id, false);
            _dragHandle = -1;
            _target = el;
            if (el == null)
            {
                if (_root != null) _root.Visible = false;
                return;
            }
            EnsureBuilt(viewport);
            if (_root != null) _root.Visible = true;
            SyncPosition();
        }

        /// <summary>Switch gizmo mode and rebuild the handles for it.</summary>
        public void SetMode(GizmoMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            CancelDrag();
            if (_root != null && _target != null && _root.Visible)
                RebuildHandles();
        }

        /// <summary>Per-frame upkeep: follow the target unless the user is dragging.</summary>
        public void Update()
        {
            if (_dragHandle >= 0) return;
            if (_target == null || _root == null || !_root.Visible) return;
            SyncPosition();
        }

        /// <summary>Try to grab a gizmo handle at <paramref name="pos"/>. Returns
        /// true when a drag starts (caller should skip world picking).</summary>
        public bool HandleMouseDown(Vector2 pos, SubViewport viewport)
        {
            if (_target == null || _root == null || !_root.Visible) return false;
            var cam = viewport.GetCamera3D();
            if (cam == null) return false;

            var origin = cam.ProjectRayOrigin(pos);
            var dir = cam.ProjectRayNormal(pos);

            int hit = -1;
            float bestT = float.MaxValue;
            foreach (var kvp in _handles)
            {
                if (GodotObject.IsInstanceValid(kvp.Value)
                    && Renderer.RayMesh(kvp.Value, origin, dir, out float t, out _)
                    && t < bestT)
                {
                    bestT = t;
                    hit = kvp.Key;
                }
            }
            if (hit < 0) return false;

            // Pin the target to direct main-thread driving for the drag: the
            // 60Hz worker snapshot + interpolation pipeline otherwise lags fast
            // TRS input, so objects drag behind the cursor.
            _renderer.SetEditorDriven(_target.Id, true);

            _dragHandle = hit;
            _dragCamera = cam;
            _startPosSN = TargetPosSN();
            var startPos = _startPosSN.ToGodot();
            var camFwd = -cam.GlobalTransform.Basis.Z;
            _startRot = _target.LocalTransform.Rotation;
            _startScale = _target.LocalTransform.Scale;
            int axis = hit % 3;

            switch (_mode)
            {
                case GizmoMode.Translate:
                    _startHit = RayAxisHit(origin, dir, startPos, camFwd);
                    break;
                case GizmoMode.Scale:
                    if (hit == 6)
                    {
                        // Uniform cube: anchor on the camera-forward plane so the
                        // drag delta is camera-relative (toward/away zooms).
                        var fwd = cam.GlobalTransform.Basis.Z;
                        _startHit = AxisPlaneHit(origin, dir, startPos, -fwd);
                    }
                    else
                    {
                        _startHit = RayAxisHit(origin, dir, startPos, camFwd);
                    }
                    break;
                case GizmoMode.Rotate:
                    // Always succeeds: intersects the axis plane, or falls back
                    // to projecting the ray origin onto it (ring edge-on to the
                    // camera). This keeps rotation drags reliable in any view.
                    _startHit = AxisPlaneHit(origin, dir, startPos, AxisDirs[axis]);
                    _startAngle = PlaneAngle(_startHit - startPos, AxisDirs[axis]);
                    break;
            }
            return _dragHandle >= 0;
        }

        /// <summary>Update the drag with the current mouse position.</summary>
        public void HandleMouseMove(Vector2 pos)
        {
            if (_dragHandle < 0 || _dragCamera == null || _target == null) return;
            var origin = _dragCamera.ProjectRayOrigin(pos);
            var dir = _dragCamera.ProjectRayNormal(pos);

            switch (_mode)
            {
                case GizmoMode.Translate:
                    DragTranslate(origin, dir);
                    break;
                case GizmoMode.Scale:
                    DragScale(origin, dir);
                    break;
                case GizmoMode.Rotate:
                    DragRotate(origin, dir);
                    break;
            }

            // Render the change straight onto the target's node — the world owns
            // truth (LocalTransform), but the snapshot pipeline would batch it.
            SyncTargetNode();
        }

        /// <summary>End any active drag.</summary>
        public void HandleMouseUp() => CancelDrag();

        /// <summary>Release all Godot nodes.</summary>
        public void Free()
        {
            if (_dragHandle >= 0 && _target != null)
                _renderer.SetEditorDriven(_target.Id, false);
            if (_root != null && GodotObject.IsInstanceValid(_root))
                _root.QueueFree();
            _root = null;
            _handles.Clear();
            _target = null;
        }

        // ── Drag modes ────────────────────────────────────────────────────

        private void DragTranslate(Vector3 origin, Vector3 dir)
        {
            int axisIdx = _dragHandle % 3;
            var axis = AxisDirs[axisIdx];
            var camFwd = -_dragCamera!.GlobalTransform.Basis.Z;
            var hit = RayAxisHit(origin, dir, _startPosSN.ToGodot(), camFwd);
            float delta = (hit - _startHit).Dot(axis);
            var newPos = _startPosSN + new System.Numerics.Vector3(axis.X, axis.Y, axis.Z) * delta;

            var lt = _target!.LocalTransform;
            _target.LocalTransform = new TRS
            {
                Position = newPos,
                Rotation = lt.Rotation,
                Scale = lt.Scale
            };
            if (_root != null) _root.Position = newPos.ToGodot();
        }

        private void DragScale(Vector3 origin, Vector3 dir)
        {
            var targetPos = _startPosSN.ToGodot();
            var lt = _target!.LocalTransform;

            if (_dragHandle == 6)
            {
                // Centre cube → uniform scale. Anchor-style ratio along the
                // camera-forward plane: factor = current/start signed distance
                // from the target, so it tracks the mouse naturally.
                var fwd = _dragCamera!.GlobalTransform.Basis.Z;
                var hit = AxisPlaneHit(origin, dir, targetPos, -fwd);
                float d0 = (_startHit - targetPos).Dot(-fwd);
                float d1 = (hit - targetPos).Dot(-fwd);
                float factor = Mathf.Abs(d0) < 1e-4f ? 1f : d1 / d0;
                factor = Mathf.Clamp(factor, 0.05f, 50f);
                _target.LocalTransform = new TRS
                {
                    Position = lt.Position,
                    Rotation = lt.Rotation,
                    Scale = _startScale * factor
                };
                return;
            }

            int axisIdx = _dragHandle % 3;
            var axis = AxisDirs[axisIdx];
            var camFwd = -_dragCamera!.GlobalTransform.Basis.Z;
            var hit2 = RayAxisHit(origin, dir, targetPos, camFwd);
            // Anchor-style ratio: scale factor = current/start signed distance
            // from the pivot along the axis (Blender-style). Grabbing the end
            // cube (≈0.75 from pivot) and dragging doubles the distance → ×2.
            float sd0 = (_startHit - targetPos).Dot(axis);
            float sd1 = (hit2 - targetPos).Dot(axis);
            float f = Mathf.Abs(sd0) < 1e-4f ? 1f : sd1 / sd0;
            f = Mathf.Clamp(f, 0.05f, 50f);
            var s = _startScale;
            if (axisIdx == 0) s.X *= f;
            else if (axisIdx == 1) s.Y *= f;
            else s.Z *= f;

            _target.LocalTransform = new TRS
            {
                Position = lt.Position,
                Rotation = lt.Rotation,
                Scale = s
            };
        }

        private void DragRotate(Vector3 origin, Vector3 dir)
        {
            var targetPos = _startPosSN.ToGodot();
            var axis = AxisDirs[_dragHandle % 3];
            var hit = AxisPlaneHit(origin, dir, targetPos, axis);

            float angle = PlaneAngle(hit - targetPos, axis) - _startAngle;
            if (Mathf.Abs(angle) < 1e-4f) return;

            var axisSN = new System.Numerics.Vector3(axis.X, axis.Y, axis.Z);
            var rot = System.Numerics.Quaternion.CreateFromAxisAngle(axisSN, angle) * _startRot;
            var lt = _target!.LocalTransform;
            _target.LocalTransform = new TRS
            {
                Position = lt.Position,
                Rotation = rot,
                Scale = lt.Scale
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private System.Numerics.Vector3 TargetPosSN()
        {
            if (_target == null) return System.Numerics.Vector3.Zero;
            var w = _target.WorldTransform;
            return new System.Numerics.Vector3(w.Translation.X, w.Translation.Y, w.Translation.Z);
        }

        private void SyncPosition()
        {
            if (_root == null || _target == null) return;
            _root.Position = TargetPosSN().ToGodot();
        }

        /// <summary>
        /// Write the target element's transform straight onto its render node.
        /// The element's LocalTransform is the source of truth; this skips the
        /// 60Hz worker snapshot + interpolation pipeline so drags feel
        /// immediate (the target is pinned editor-driven for the duration).
        /// </summary>
        private void SyncTargetNode()
        {
            if (_target == null) return;
            var node = _renderer.GetNode(_target.Id);
            if (node == null) return;

            var lt = _target.LocalTransform;
            var rot = lt.Rotation;
            var basis = new Basis(new global::Godot.Quaternion(rot.X, rot.Y, rot.Z, rot.W))
                * Basis.FromScale(new Vector3(lt.Scale.X, lt.Scale.Y, lt.Scale.Z));
            node.Transform = new Transform3D(basis, new Vector3(lt.Position.X, lt.Position.Y, lt.Position.Z));
        }

        private void CancelDrag()
        {
            if (_dragHandle >= 0 && _target != null)
                _renderer.SetEditorDriven(_target.Id, false);
            _dragHandle = -1;
            _dragCamera = null;
        }

        private void EnsureBuilt(SubViewport viewport)
        {
            if (_root != null && GodotObject.IsInstanceValid(_root) && _root.GetParent() == viewport)
                return;
            if (_root != null && GodotObject.IsInstanceValid(_root) && _root.GetParent() != null)
                _root.GetParent().RemoveChild(_root);
            if (_root != null && !GodotObject.IsInstanceValid(_root))
                _root = null;

            _root = new Node3D { Name = "EditorGizmo" };
            _root.Visible = false;
            viewport.AddChild(_root);
            RebuildHandles();
        }

        private void RebuildHandles()
        {
            foreach (var h in _handles.Values)
                if (GodotObject.IsInstanceValid(h)) h.QueueFree();
            _handles.Clear();
            if (_root == null) return;

            switch (_mode)
            {
                case GizmoMode.Translate: BuildTranslateHandles(); break;
                case GizmoMode.Rotate:    BuildRotateHandles(); break;
                case GizmoMode.Scale:     BuildScaleHandles(); break;
            }
        }

        private void BuildTranslateHandles()
        {
            for (int axis = 0; axis < 3; axis++)
            {
                var mat = AxisMaterial(AxisColors[axis]);
                var shaft = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.78f },
                    MaterialOverride = mat
                };
                shaft.Position = AxisDirs[axis] * 0.39f;
                shaft.RotationDegrees = AxisRotDegrees(axis);
                _root!.AddChild(shaft);
                _handles[axis] = shaft;

                var head = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.22f },
                    MaterialOverride = mat
                };
                head.Position = AxisDirs[axis] * 0.95f;
                head.RotationDegrees = AxisRotDegrees(axis);
                _root!.AddChild(head);
                _handles[axis + 3] = head; // 3-5 = heads, axis = axis % 3
            }
            _root!.AddChild(PivotMesh(0.07f));
        }

        private void BuildScaleHandles()
        {
            for (int axis = 0; axis < 3; axis++)
            {
                var mat = AxisMaterial(AxisColors[axis]);
                var shaft = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.02f, Height = 0.6f },
                    MaterialOverride = mat
                };
                shaft.Position = AxisDirs[axis] * 0.3f;
                shaft.RotationDegrees = AxisRotDegrees(axis);
                _root!.AddChild(shaft);
                _handles[axis] = shaft;

                var cube = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.14f) },
                    MaterialOverride = mat
                };
                cube.Position = AxisDirs[axis] * 0.75f;
                _root!.AddChild(cube);
                _handles[axis + 3] = cube; // 3-5 = end cubes, axis = axis % 3
            }
            // Centre cube = uniform scale (handle id 6).
            var center = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.11f, 0.11f, 0.11f) },
                MaterialOverride = AxisMaterial(new Color(0.85f, 0.85f, 0.9f))
            };
            _root!.AddChild(center);
            _handles[6] = center;
        }

        private void BuildRotateHandles()
        {
            for (int axis = 0; axis < 3; axis++)
            {
                var mat = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(AxisColors[axis].R, AxisColors[axis].G, AxisColors[axis].B, 0.85f)
                };
                var ring = new MeshInstance3D
                {
                    Mesh = new TorusMesh { InnerRadius = 0.72f, OuterRadius = 0.8f },
                    MaterialOverride = mat
                };
                ring.RotationDegrees = AxisRotDegrees(axis);
                _root!.AddChild(ring);
                _handles[axis] = ring;
            }
        }

        private static MeshInstance3D PivotMesh(float size)
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.85f, 0.85f, 0.9f, 0.9f)
            };
            return new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(size, size, size) },
                MaterialOverride = mat
            };
        }

        private static StandardMaterial3D AxisMaterial(Color color)
        {
            return new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = color,
                EmissionEnabled = true,
                Emission = color
            };
        }

        /// <summary>Rotation (degrees) mapping a +Y primitive to the axis.</summary>
        private static Vector3 AxisRotDegrees(int axis) => axis switch
        {
            0 => new Vector3(0, 0, -90),   // +Y → +X
            2 => new Vector3(90, 0, 0),    // +Y → +Z
            _ => Vector3.Zero              // +Y stays +Y
        };

        /// <summary>
        /// Intersect the ray with the view plane (perpendicular to the camera's
        /// forward) through <paramref name="point"/>. The hit tracks the mouse:
        /// dragging along the axis's screen projection moves it along the axis
        /// at the foreshortened ratio, and the axis dot in the callers discards
        /// any perpendicular motion. The plane must NOT contain the camera —
        /// a plane through the camera position makes every ray from the camera
        /// meet it at the camera itself (t = 0), so the hit never moves and the
        /// drag does nothing.
        /// </summary>
        private static Vector3 RayAxisHit(Vector3 origin, Vector3 dir, Vector3 point, Vector3 fwd)
        {
            float denom = dir.Dot(fwd);
            if (Mathf.Abs(denom) < 1e-6f) return point;
            float t = (point - origin).Dot(fwd) / denom;
            return origin + dir * t;
        }

        /// <summary>Ray vs plane (point + normal). Returns true when the ray is
        /// not parallel to the plane and hits in front.</summary>
        private static bool RayPlaneHit(Vector3 origin, Vector3 dir, Vector3 planePoint, Vector3 normal, out Vector3 hit)
        {
            hit = Vector3.Zero;
            float denom = normal.Dot(dir);
            if (Mathf.Abs(denom) < 1e-6f) return false;
            float t = (planePoint - origin).Dot(normal) / denom;
            if (t < 0f) return false;
            hit = origin + dir * t;
            return true;
        }

        /// <summary>
        /// Hit of the ray on the plane through <paramref name="planePoint"/>
        /// perpendicular to <paramref name="axis"/>. When the ray is (nearly)
        /// parallel — the ring is edge-on to the camera — falls back to the
        /// projection of the ray origin onto the plane, which is always defined.
        /// Used for rotation drags so they work in any camera orientation.
        /// </summary>
        private static Vector3 AxisPlaneHit(Vector3 origin, Vector3 dir, Vector3 planePoint, Vector3 axis)
        {
            if (RayPlaneHit(origin, dir, planePoint, axis, out var hit))
                return hit;
            return ProjectOntoPlane(origin, planePoint, axis);
        }

        /// <summary>Project <paramref name="p"/> onto the plane through
        /// <paramref name="planePoint"/> perpendicular to <paramref name="normal"/>.</summary>
        private static Vector3 ProjectOntoPlane(Vector3 p, Vector3 planePoint, Vector3 normal)
        {
            return p - (p - planePoint).Dot(normal) * normal;
        }

        /// <summary>Signed angle of a point (in the plane perpendicular to
        /// <paramref name="axis"/>) around the axis, measured from a stable
        /// reference direction in that plane.</summary>
        private static float PlaneAngle(Vector3 p, Vector3 axis)
        {
            var refUp = Mathf.Abs(axis.Dot(Vector3.Up)) > 0.9f ? Vector3.Forward : Vector3.Up;
            var v1 = refUp.Cross(axis).Normalized();
            var v2 = axis.Cross(v1).Normalized();
            return Mathf.Atan2(p.Dot(v2), p.Dot(v1));
        }
    }

    internal static class VectorConversions
    {
        public static Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
    }
}
