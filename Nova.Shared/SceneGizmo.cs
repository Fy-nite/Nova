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
    /// three modes (T / R / S): translate — axis arrows, rotate — axis rings,
    /// scale — axis shafts with tip cubes plus a centre cube for uniform
    /// scale.
    ///
    /// The geometry is NOT built here: both this host and the MonoGame host
    /// draw the one canonical model in <see cref="TransformGizmo"/> (V12
    /// core), so every renderer shows and grabs the same gizmo. This class
    /// only projects those segments into the Godot viewport (one
    /// <see cref="ImmediateMesh"/> of world-space lines), screen-space grabs,
    /// and drives the drag.
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
        private MeshInstance3D? _lines;
        private ImmediateMesh? _mesh;
        private SubViewport? _viewport;
        private readonly List<TransformGizmo.Segment> _scratch = new();

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

        // Canonical axes from V12's model, mirrored into Godot's vector type
        // for the drag math. Components are identical — only the type differs.
        private static readonly Vector3[] AxisDirs =
        {
            TransformGizmo.AxisDirs[0].ToGodot(),
            TransformGizmo.AxisDirs[1].ToGodot(),
            TransformGizmo.AxisDirs[2].ToGodot(),
        };

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
            RebuildLines();
        }

        /// <summary>Switch gizmo mode and rebuild the lines for it.</summary>
        public void SetMode(GizmoMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            CancelDrag();
            if (_root != null && _target != null && _root.Visible)
                RebuildLines();
        }

        /// <summary>Per-frame upkeep: rebuild the lines so the gizmo follows
        /// the target (also mid-drag — the camera is fixed during drags, the
        /// pivot moves).</summary>
        public void Update()
        {
            if (_target == null || _root == null || !_root.Visible) return;
            RebuildLines();
        }

        /// <summary>Try to grab a gizmo handle at <paramref name="pos"/>.
        /// Returns true when a drag starts (caller should skip world picking).</summary>
        public bool HandleMouseDown(Vector2 pos, SubViewport viewport)
        {
            if (_target == null || _root == null || !_root.Visible) return false;
            var cam = viewport.GetCamera3D();
            if (cam == null) return false;
            _viewport = viewport;

            // Screen-space test against the model's own segments — exactly
            // what the overlay draws is exactly what the hand grabs.
            BuildSegments(cam);
            int hit = -1;
            float best = TransformGizmo.GrabThresholdPx;
            foreach (var seg in _scratch)
            {
                var a = seg.A.ToGodot();
                var b = seg.B.ToGodot();
                if (cam.IsPositionBehind(a) || cam.IsPositionBehind(b)) continue;
                float d = DistToSegment(pos, cam.UnprojectPosition(a), cam.UnprojectPosition(b));
                if (d >= best) continue;
                best = d;
                hit = seg.Handle;
            }
            if (hit < 0) return false;

            var origin = cam.ProjectRayOrigin(pos);
            var dir = cam.ProjectRayNormal(pos);

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
                    if (hit == TransformGizmo.CenterHandle)
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
            _lines = null;
            _mesh = null;
            _viewport = null;
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
        }

        private void DragScale(Vector3 origin, Vector3 dir)
        {
            var targetPos = _startPosSN.ToGodot();
            var lt = _target!.LocalTransform;

            if (_dragHandle == TransformGizmo.CenterHandle)
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
            var camFwd = _dragCamera!.GlobalTransform.Basis.Z;
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

        // ── Model projection ──────────────────────────────────────────────

        /// <summary>Fill <see cref="_scratch"/> with the canonical gizmo
        /// segments around the target, sized for this camera.</summary>
        private void BuildSegments(Camera3D cam)
        {
            if (_target == null) { _scratch.Clear(); return; }
            var origin = TargetPosSN();
            float dist = cam.GlobalPosition.DistanceTo(origin.ToGodot());
            float size = TransformGizmo.ScreenSizeFromFov(dist, cam.Fov);
            var look = -cam.GlobalTransform.Basis.Z;
            TransformGizmo.Build(_mode, origin, size,
                new System.Numerics.Vector3(look.X, look.Y, look.Z), _scratch);
        }

        /// <summary>Rebuild the line mesh from the model — world-space
        /// segments, one immediate-mode surface, rebuilt whenever the target,
        /// mode or camera distance changes (cheap: a few dozen lines).</summary>
        private void RebuildLines()
        {
            if (_mesh == null || _target == null) return;
            var cam = _viewport?.GetCamera3D();
            if (cam == null) return;
            BuildSegments(cam);

            _mesh.ClearSurfaces();
            if (_scratch.Count == 0) return;
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            foreach (var seg in _scratch)
            {
                var c = new Color(
                    seg.Color.R / 255f,
                    seg.Color.G / 255f,
                    seg.Color.B / 255f,
                    seg.Color.A / 255f);
                _mesh.SurfaceSetColor(c);
                _mesh.SurfaceAddVertex(seg.A.ToGodot());
                _mesh.SurfaceSetColor(c);
                _mesh.SurfaceAddVertex(seg.B.ToGodot());
            }
            _mesh.SurfaceEnd();
        }

        /// <summary>2D distance from a point to a segment (viewport pixels).</summary>
        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            if (len2 < 1e-6f) return p.DistanceTo(a);
            var pa = p - a;
            float t = Mathf.Clamp((pa.X * ab.X + pa.Y * ab.Y) / len2, 0f, 1f);
            return p.DistanceTo(a + ab * t);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private System.Numerics.Vector3 TargetPosSN()
        {
            if (_target == null) return System.Numerics.Vector3.Zero;
            var w = _target.WorldTransform;
            return new System.Numerics.Vector3(w.Translation.X, w.Translation.Y, w.Translation.Z);
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

            // The gizmo stays world-anchored: segments are built in world
            // space around the target, so the node never moves — only the
            // mesh contents rebuild when the target or camera does.
            _mesh = new ImmediateMesh();
            _lines = new MeshInstance3D
            {
                Mesh = _mesh,
                MaterialOverride = LineMaterial(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _root.AddChild(_lines);
            _viewport = viewport;
        }

        /// <summary>Unshaded, vertex-coloured, never depth-tested — the
        /// overlay always sits on top of the scene (MonoGame host parity).</summary>
        private static StandardMaterial3D LineMaterial() => new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
            DisableReceiveShadows = true,
            AlbedoColor = Colors.White,
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
