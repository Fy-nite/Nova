using System;
using System.Collections.Generic;
using Godot;
using V12.Core.Interfaces.Physics;
using V12.Components;
using NumVec3 = System.Numerics.Vector3;
using NumQuat = System.Numerics.Quaternion;

namespace V12TwoDog
{
    public class GodotPhysicsBackend : IPhysicsBackend
    {
        private readonly PhysicsServer3DInstance _server;
        private readonly Rid _space;
        private readonly Dictionary<long, GodotPhysicsBody> _bodies = new();
        private long _nextId;

        public GodotPhysicsBackend(World3D world3d)
        {
            _server = PhysicsServer3D.Singleton;
            // Use the main world space — guaranteed to be stepped by Godot's physics
            // tick and has proper collision detection between all body types.
            _space = world3d.GetSpace();
        }

        public IPhysicsBody CreateBody(in PhysicsBodyDesc desc)
        {
            long id = _nextId++;
            var body = new GodotPhysicsBody(id) { GravityScale = desc.GravityScale };

            var bodyRid = _server.BodyCreate();
            _server.BodySetSpace(bodyRid, _space);

            body.IsDynamic = !desc.IsKinematic;
            var mode = desc.IsKinematic ? PhysicsServer3D.BodyMode.Static : PhysicsServer3D.BodyMode.Rigid;
            _server.BodySetMode(bodyRid, mode);

            var shapeRid = CreateShapeRid(desc.Shape, desc.Size);
            // Shape transform is relative to the body — keep it at identity
            _server.BodyAddShape(bodyRid, shapeRid, Transform3D.Identity, disabled: false);

            // Set the body's world transform from the desc
            var origin = new Vector3(desc.Position.X, desc.Position.Y, desc.Position.Z);
            var bodyXform = Transform3D.Identity with { Origin = origin, Basis = Basis.Identity };
            if (desc.Rotation.LengthSquared() > 0f)
            {
                var r = new Quaternion(desc.Rotation.X, desc.Rotation.Y, desc.Rotation.Z, desc.Rotation.W);
                bodyXform = bodyXform with { Basis = new Basis(r) };
            }
            _server.BodySetState(bodyRid, PhysicsServer3D.BodyState.Transform, bodyXform);

            // V12 owns gravity via LocomotionComponent.Gravity applied to velocity
            // in LocomotionSystem.  Disable Godot's built-in gravity so the two
            // don't fight each other.
            _server.BodySetParam(bodyRid, PhysicsServer3D.BodyParameter.GravityScale, 0f);

            // Keep body awake so it responds to velocity changes
            _server.BodySetState(bodyRid, PhysicsServer3D.BodyState.Sleeping, false);

            body.BodyRid = bodyRid;
            body.ShapeRid = shapeRid;
            _bodies[id] = body;
            return body;
        }

        public void DestroyBody(IPhysicsBody body)
        {
            if (body is GodotPhysicsBody gb)
            {
                _bodies.Remove(gb.Id);
                if (gb.ShapeRid.IsValid)
                    _server.FreeRid(gb.ShapeRid);
                if (gb.BodyRid.IsValid)
                    _server.FreeRid(gb.BodyRid);
            }
        }

        public bool Raycast(in Ray ray, out RaycastHit hit)
        {
            hit = default;
            var state = _server.SpaceGetDirectState(_space);
            if (state == null)
                return false;

            var from = new Vector3(ray.Origin.X, ray.Origin.Y, ray.Origin.Z);
            var to = new Vector3(
                ray.Origin.X + ray.Direction.X * ray.MaxDistance,
                ray.Origin.Y + ray.Direction.Y * ray.MaxDistance,
                ray.Origin.Z + ray.Direction.Z * ray.MaxDistance);

            var query = new PhysicsRayQueryParameters3D
            {
                From = from,
                To = to,
                CollideWithBodies = true,
                CollideWithAreas = false
            };

            var result = state.IntersectRay(query);
            if (result.Count == 0)
                return false;

            var pos = (Vector3)result["position"];
            var norm = (Vector3)result["normal"];
            hit = new RaycastHit
            {
                Point = new NumVec3(pos.X, pos.Y, pos.Z),
                Normal = new NumVec3(norm.X, norm.Y, norm.Z),
                Distance = (pos - from).Length()
            };
            return true;
        }

        public void Step(float deltaTime)
        {
            // Godot's PhysicsServer3D steps all spaces (including custom) automatically
            // during the engine's physics tick. V12 sets initial velocity via
            // BodySetState, and the physics server handles integration, gravity, and
            // collision response asynchronously.
        }

        private Rid CreateShapeRid(MeshShape shape, NumVec3 size)
        {
            Rid rid;
            switch (shape)
            {
                case MeshShape.Sphere:
                    rid = _server.SphereShapeCreate();
                    _server.ShapeSetData(rid, size.X * 0.5f);
                    break;
                case MeshShape.Capsule:
                    rid = _server.CapsuleShapeCreate();
                    _server.ShapeSetData(rid, new global::Godot.Collections.Dictionary
                    {
                        { "radius", size.X * 0.5f },
                        { "height", size.Y }
                    });
                    break;
                case MeshShape.Cylinder:
                    rid = _server.CylinderShapeCreate();
                    _server.ShapeSetData(rid, new global::Godot.Collections.Dictionary
                    {
                        { "radius", size.X * 0.5f },
                        { "height", size.Y }
                    });
                    break;
                case MeshShape.Plane:
                    rid = _server.BoxShapeCreate();
                    _server.ShapeSetData(rid, new Vector3(size.X, 0.1f, size.Z));
                    break;
                case MeshShape.Box:
                default:
                    rid = _server.BoxShapeCreate();
                    _server.ShapeSetData(rid, new Vector3(size.X, size.Y, size.Z));
                    break;
            }
            return rid;
        }
    }

    internal class GodotPhysicsBody : IPhysicsBody
    {
        private readonly PhysicsServer3DInstance _server;

        public long Id { get; }
        public Rid BodyRid { get; set; }
        public Rid ShapeRid { get; set; }
        public float GravityScale { get; set; } = 1f;
        public bool IsDynamic { get; set; } = true;

        public GodotPhysicsBody(long id)
        {
            Id = id;
            _server = PhysicsServer3D.Singleton;
        }

        public NumVec3 Position
        {
            get
            {
                var xform = (Transform3D)_server.BodyGetState(BodyRid, PhysicsServer3D.BodyState.Transform);
                return new NumVec3(xform.Origin.X, xform.Origin.Y, xform.Origin.Z);
            }
            set
            {
                var xform = (Transform3D)_server.BodyGetState(BodyRid, PhysicsServer3D.BodyState.Transform);
                _server.BodySetState(BodyRid, PhysicsServer3D.BodyState.Transform,
                    new Transform3D(xform.Basis, new Vector3(value.X, value.Y, value.Z)));
            }
        }

        public NumQuat Rotation
        {
            get
            {
                var xform = (Transform3D)_server.BodyGetState(BodyRid, PhysicsServer3D.BodyState.Transform);
                var q = xform.Basis.GetRotationQuaternion();
                return new NumQuat(q.X, q.Y, q.Z, q.W);
            }
            set
            {
                var xform = (Transform3D)_server.BodyGetState(BodyRid, PhysicsServer3D.BodyState.Transform);
                var b = new Basis(new Quaternion(value.X, value.Y, value.Z, value.W));
                _server.BodySetState(BodyRid, PhysicsServer3D.BodyState.Transform,
                    new Transform3D(b, xform.Origin));
            }
        }

        public NumVec3 LinearVelocity
        {
            get
            {
                var v = (Vector3)_server.BodyGetState(BodyRid, PhysicsServer3D.BodyState.LinearVelocity);
                return new NumVec3(v.X, v.Y, v.Z);
            }
            set
            {
                _server.BodySetState(BodyRid, PhysicsServer3D.BodyState.LinearVelocity,
                    new Vector3(value.X, value.Y, value.Z));
            }
        }

        public void AddForce(NumVec3 force)
        {
            _server.BodyApplyImpulse(BodyRid, new Vector3(force.X, force.Y, force.Z), null);
        }
    }
}
