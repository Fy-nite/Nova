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
            _space = world3d.GetSpace();
        }

        public IPhysicsBody CreateBody(in PhysicsBodyDesc desc)
        {
            long id = _nextId++;
            var descCopy = desc;
            var body = new GodotPhysicsBody(id, descCopy)
            {
                GravityScale = descCopy.GravityScale,
                IsDynamic = !descCopy.IsKinematic
            };

            GodotMainThread.Execute(() =>
            {
                var bodyRid = _server.BodyCreate();
                _server.BodySetSpace(bodyRid, _space);

                var mode = descCopy.IsKinematic ? PhysicsServer3D.BodyMode.Static : PhysicsServer3D.BodyMode.Rigid;
                _server.BodySetMode(bodyRid, mode);

                var shapeRid = CreateShapeRid(descCopy.Shape, descCopy.Size);
                _server.BodyAddShape(bodyRid, shapeRid, Transform3D.Identity, disabled: false);

                var origin = new Vector3(descCopy.Position.X, descCopy.Position.Y, descCopy.Position.Z);
                var bodyXform = Transform3D.Identity with { Origin = origin, Basis = Basis.Identity };
                if (descCopy.Rotation.LengthSquared() > 0f)
                {
                    var r = new Quaternion(descCopy.Rotation.X, descCopy.Rotation.Y, descCopy.Rotation.Z, descCopy.Rotation.W);
                    bodyXform = bodyXform with { Basis = new Basis(r) };
                }
                _server.BodySetState(bodyRid, PhysicsServer3D.BodyState.Transform, bodyXform);

                _server.BodySetState(bodyRid, PhysicsServer3D.BodyState.Sleeping, false);

                body.BodyRid = bodyRid;
                body.ShapeRid = shapeRid;
                body._syncPending = false;
            });

            _bodies[id] = body;
            return body;
        }

        public void DestroyBody(IPhysicsBody body)
        {
            if (body is GodotPhysicsBody gb)
            {
                _bodies.Remove(gb.Id);
                GodotMainThread.Execute(() =>
                {
                    if (gb.ShapeRid.IsValid)
                        _server.FreeRid(gb.ShapeRid);
                    if (gb.BodyRid.IsValid)
                        _server.FreeRid(gb.BodyRid);
                });
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
        }

        public void SyncBodies()
        {
            foreach (var body in _bodies.Values)
                body.Sync();
        }

        /// <summary>
        /// Read the latest positions/velocities from all Godot physics bodies back into
        /// local caches. Called from the main thread AFTER the physics tick so the
        /// cached values reflect collision response and gravity.
        /// </summary>
        public void ReadbackAll()
        {
            foreach (var body in _bodies.Values)
                body.ReadbackFromGodot();
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

        // Local caches — read/written from worker thread, synced to Godot on main thread
        internal volatile bool _syncPending = true;
        private NumVec3 _cachedPosition;
        private NumQuat _cachedRotation;
        private NumVec3 _cachedVelocity;

        public GodotPhysicsBody(long id, PhysicsBodyDesc desc)
        {
            Id = id;
            _server = PhysicsServer3D.Singleton;
            _cachedPosition = desc.Position;
            _cachedRotation = desc.Rotation;
            _cachedVelocity = NumVec3.Zero;
        }

        public NumVec3 Position
        {
            get => _cachedPosition;
            set
            {
                _cachedPosition = value;
                EnqueueSync();
            }
        }

        public NumQuat Rotation
        {
            get => _cachedRotation;
            set
            {
                _cachedRotation = value;
                EnqueueSync();
            }
        }

        public NumVec3 LinearVelocity
        {
            get => _cachedVelocity;
            set
            {
                _cachedVelocity = value;
                EnqueueSync();
            }
        }

        public void AddForce(NumVec3 force)
        {
            GodotMainThread.Execute(() =>
            {
                if (!BodyRid.IsValid) return;
                _server.BodyApplyImpulse(BodyRid, new Vector3(force.X, force.Y, force.Z), null);
            });
        }

        private void EnqueueSync()
        {
            if (_syncPending) return;
            _syncPending = true;
            GodotMainThread.Execute(SyncToGodot);
        }

        /// <summary>
        /// Push local cached state to Godot's PhysicsServer3D (write).
        /// Called from GodotMainThread.FlushPending() on the main thread.
        /// For rigid (dynamic) bodies only velocity is written — the physics tick
        /// integrates it into position.  For kinematic bodies both position and
        /// velocity are written since the element transform drives those.
        /// </summary>
        internal void Sync()
        {
            if (!_syncPending) return;
            _syncPending = false;
            SyncToGodot();
        }
        private void SyncToGodot()
        {
            _syncPending = false;

            if (!BodyRid.IsValid)
                return;

            var vel = new Vector3(_cachedVelocity.X, _cachedVelocity.Y, _cachedVelocity.Z);
            _server.BodySetState(BodyRid, PhysicsServer3D.BodyState.LinearVelocity, vel);

            if (!IsDynamic)
            {
                var xform = (Transform3D)_server.BodyGetState(BodyRid, PhysicsServer3D.BodyState.Transform);
                var newOrigin = new Vector3(_cachedPosition.X, _cachedPosition.Y, _cachedPosition.Z);
                var newBasis = xform.Basis;
                if (_cachedRotation.LengthSquared() > 0f)
                {
                    newBasis = new Basis(new Quaternion(
                        _cachedRotation.X, _cachedRotation.Y, _cachedRotation.Z, _cachedRotation.W));
                }
                _server.BodySetState(BodyRid, PhysicsServer3D.BodyState.Transform,
                    new Transform3D(newBasis, newOrigin));
            }

        }

        /// <summary>
        /// Read the latest state from Godot's PhysicsServer3D back into local caches.
        /// Called from the main thread after Godot's physics tick, separate from writes.
        /// </summary>
        internal void ReadbackFromGodot()
        {
            if (!BodyRid.IsValid)
                return;

            var resultXform = (Transform3D)_server.BodyGetState(BodyRid, PhysicsServer3D.BodyState.Transform);
            _cachedPosition = new NumVec3(resultXform.Origin.X, resultXform.Origin.Y, resultXform.Origin.Z);
            var q = resultXform.Basis.GetRotationQuaternion();
            _cachedRotation = new NumQuat(q.X, q.Y, q.Z, q.W);
            var vel = (Vector3)_server.BodyGetState(BodyRid, PhysicsServer3D.BodyState.LinearVelocity);
            _cachedVelocity = new NumVec3(vel.X, vel.Y, vel.Z);
        }
    }
}
