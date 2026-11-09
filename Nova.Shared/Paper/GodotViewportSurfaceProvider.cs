using System;
using System.Collections.Concurrent;
using Godot;
using V12.Core;
using PVector = global::Prowl.Vector;

namespace V12TwoDog.Paper
{
    /// <summary>
    /// Renders V12 viewport elements into Godot <see cref="SubViewport"/>s.
    ///
    /// This is the dumb half of the viewport split: camera -> texture. It does
    /// not pick, orbit, or draw gizmos. The editor layer consumes the viewport
    /// through <see cref="V12.Core.Rendering.IViewportRegistry"/> and does that
    /// math on top.
    ///
    /// The Paper renderer calls <see cref="GetTexture"/> and
    /// <see cref="SetRect"/> from its own thread; creation, resize and release
    /// are queued and applied on the main thread by <see cref="Pump"/>.
    /// </summary>
    public sealed class GodotViewportSurfaceProvider
    {
        private sealed class Surface
        {
            public SubViewport Viewport = null!;
            public Texture2D Texture = null!;
        }

        private readonly Node _host;
        private readonly Renderer _renderer;
        private readonly ConcurrentDictionary<long, Surface> _surfaces = new();
        private readonly ConcurrentDictionary<long, byte> _createRequests = new();
        private readonly ConcurrentDictionary<long, Vector2I> _sizeRequests = new();
        private readonly ConcurrentDictionary<long, byte> _releaseRequests = new();

        public GodotViewportSurfaceProvider(Node host, Renderer renderer)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        /// <summary>Texture for a viewport element, or null while the main
        /// thread is still creating it. Safe to call from the Paper thread.</summary>
        public object? GetTexture(long viewportId)
        {
            if (_surfaces.TryGetValue(viewportId, out var surface))
                return surface.Texture;

            _createRequests[viewportId] = 0;
            return null;
        }

        /// <summary>Queue a render-target resize for the laid-out viewport rect.</summary>
        public void SetRect(long viewportId, PVector.Rect rect)
        {
            int w = Math.Max(1, (int)rect.Size.X);
            int h = Math.Max(1, (int)rect.Size.Y);
            if (w <= 1 || h <= 1) return;
            _sizeRequests[viewportId] = new Vector2I(w, h);
        }

        /// <summary>Release a viewport that disappeared from the UI frame.</summary>
        public void Release(long viewportId) => _releaseRequests[viewportId] = 0;

        /// <summary>Create a viewport immediately (main thread only).</summary>
        public SubViewport? EnsureSurface(long viewportId)
        {
            if (_surfaces.TryGetValue(viewportId, out var surface))
                return surface.Viewport;

            CreateSurface(viewportId);
            return _surfaces.TryGetValue(viewportId, out surface) ? surface.Viewport : null;
        }

        public SubViewport? GetSceneViewport(long viewportId)
            => _surfaces.TryGetValue(viewportId, out var surface) ? surface.Viewport : null;

        /// <summary>Apply queued creates, resizes and releases. Main thread only.</summary>
        public void Pump()
        {
            foreach (var id in _releaseRequests.Keys)
            {
                if (!_releaseRequests.TryRemove(id, out _)) continue;
                if (_surfaces.TryRemove(id, out var surface))
                {
                    _renderer.UnregisterSceneViewport(id);
                    if (GodotObject.IsInstanceValid(surface.Viewport))
                        surface.Viewport.QueueFree();
                }
            }

            foreach (var id in _createRequests.Keys)
            {
                if (!_createRequests.TryRemove(id, out _)) continue;
                CreateSurface(id);
            }

            foreach (var kv in _sizeRequests)
            {
                if (!_sizeRequests.TryRemove(kv.Key, out var size)) continue;
                if (!_surfaces.TryGetValue(kv.Key, out var surface)) continue;
                if (!GodotObject.IsInstanceValid(surface.Viewport)) continue;
                if (surface.Viewport.Size != size)
                    surface.Viewport.Size = size;
            }
        }

        private void CreateSurface(long viewportId)
        {
            if (_surfaces.ContainsKey(viewportId)) return;

            var sub = new SubViewport
            {
                Name = "Viewport_" + viewportId,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                OwnWorld3D = false,
                HandleInputLocally = false,
            };

            var env = new WorldEnvironment();
            env.Environment = new global::Godot.Environment
            {
                BackgroundMode = global::Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.06f, 0.06f, 0.08f),
                AmbientLightSource = global::Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.58f, 0.65f),
                AmbientLightEnergy = 1.0f,
            };
            sub.AddChild(env);

            var light = new DirectionalLight3D
            {
                RotationDegrees = new Vector3(-50f, -30f, 0f),
                LightEnergy = 1.1f,
                ShadowEnabled = true,
            };
            sub.AddChild(light);

            var fallbackCamera = new Camera3D { Current = true };
            sub.AddChild(fallbackCamera);

            _host.AddChild(sub);
            _renderer.RegisterSceneViewport(viewportId, sub);

            _surfaces[viewportId] = new Surface
            {
                Viewport = sub,
                Texture = sub.GetTexture(),
            };

            GameRoot.Instance?.MarkRenderDirty();
        }
    }
}
