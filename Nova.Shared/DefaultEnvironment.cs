using Godot;

using System;
using System.Collections.Generic;
using V12;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12TwoDog
{
    /// <summary>
    /// Host-level default scene environment for the main viewport: a
    /// <c>WorldEnvironment</c> (sky/ambient) driven by the first
    /// <see cref="EnvironmentComponent"/> found in the worlds, plus a default
    /// directional sun that steps aside as soon as the game provides its own
    /// directional light. Game <c>GenericLightComponent</c> lights keep flowing
    /// through snapshots untouched.
    ///
    /// Games that define neither still get a pleasant day sky + sun rig, so
    /// lighting works with zero setup on either renderer.
    /// </summary>
    public sealed class DefaultEnvironment
    {
        private readonly Node3D _host;

        private WorldEnvironment? _worldEnv;
        private global::Godot.Environment? _env;
        private ProceduralSkyMaterial? _skyMat;
        private PanoramaSkyMaterial? _panoMat;
        private DirectionalLight3D? _sun;

        private int _tick;
        private string _appliedSignature = "";
        private bool _appliedSunVisible;
        private string _appliedPanoPath = "";
        private bool _loggedPanoFallback;

        public DefaultEnvironment(Node3D host)
        {
            _host = host;
        }

        /// <summary>Create the Godot nodes once and apply the current worlds' state.</summary>
        public void Ensure(GameRoot root)
        {
            if (_worldEnv == null)
            {
                _worldEnv = new WorldEnvironment { Name = "V12DefaultEnvironment" };
                _env = new global::Godot.Environment();
                _worldEnv.Environment = _env;
                _host.AddChild(_worldEnv);

                _sun = new DirectionalLight3D
                {
                    Name = "V12DefaultSun",
                    RotationDegrees = new Vector3(-50f, -30f, 0f),
                    LightColor = new Color(1f, 0.96f, 0.88f),
                    LightEnergy = 1.1f,
                    ShadowEnabled = true,
                };
                _host.AddChild(_sun);
            }

            Refresh(root);
        }

        /// <summary>Re-evaluate every ~30 physics ticks (world edits are rare).</summary>
        public void Update(GameRoot root)
        {
            if (_worldEnv == null) return;
            if (++_tick % 30 != 0) return;
            Refresh(root);
        }

        private void Refresh(GameRoot root)
        {
            try
            {
                FindWorldState(root, out var envComp, out bool hasGameDirectional);

                string sig = envComp == null
                    ? "default"
                    : $"{(int)envComp.Mode}|{envComp.SkyR:F3},{envComp.SkyG:F3},{envComp.SkyB:F3}|" +
                      $"{envComp.AmbientR:F3},{envComp.AmbientG:F3},{envComp.AmbientB:F3}|{envComp.AmbientEnergy:F3}|{envComp.SkyboxPath}";

                if (_appliedSignature != sig)
                {
                    _appliedSignature = sig;
                    ApplyEnvironment(envComp);
                }

                bool wantSun = !hasGameDirectional;
                if (_sun != null && _appliedSunVisible != wantSun)
                {
                    _appliedSunVisible = wantSun;
                    _sun.Visible = wantSun;
                }
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[V12DefaultEnvironment] refresh failed: {ex.Message}");
            }
        }

        private void ApplyEnvironment(EnvironmentComponent? envComp)
        {
            if (_env == null) return;

            if (envComp == null)
            {
                // Baked-in default: pleasant day sky + gentle ambient.
                ApplySky(new Color(0.30f, 0.55f, 0.90f), string.Empty);
                _env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
                _env.AmbientLightColor = new Color(0.40f, 0.42f, 0.48f);
                _env.AmbientLightEnergy = 0.9f;
                return;
            }

            var sky = new Color(envComp.SkyR, envComp.SkyG, envComp.SkyB);
            if (envComp.Mode == BackgroundMode.Skybox)
                ApplySky(sky, envComp.SkyboxPath);
            else
            {
                _env.BackgroundMode = global::Godot.Environment.BGMode.Color;
                _env.BackgroundColor = sky;
            }

            _env.AmbientLightSource = global::Godot.Environment.AmbientSource.Color;
            _env.AmbientLightColor = new Color(envComp.AmbientR, envComp.AmbientG, envComp.AmbientB);
            _env.AmbientLightEnergy = Math.Max(0f, envComp.AmbientEnergy);
        }

        private void ApplySky(Color top, string panoPath)
        {
            if (_env == null) return;

            if (!string.IsNullOrEmpty(panoPath) && _appliedPanoPath != panoPath)
            {
                _appliedPanoPath = panoPath;
                try
                {
                    var tex = GD.Load<Texture2D>(panoPath);
                    if (tex != null)
                    {
                        _panoMat ??= new PanoramaSkyMaterial();
                        _panoMat.Panorama = tex;
                        var panoSky = new Sky();
                        panoSky.SkyMaterial = _panoMat;
                        _env.BackgroundMode = global::Godot.Environment.BGMode.Sky;
                        _env.Sky = panoSky;
                        return;
                    }
                }
                catch { }

                if (!_loggedPanoFallback)
                {
                    _loggedPanoFallback = true;
                    GD.PushWarning($"[V12DefaultEnvironment] panorama '{panoPath}' failed to load; gradient fallback.");
                }
            }
            else if (string.IsNullOrEmpty(panoPath))
            {
                _appliedPanoPath = string.Empty;
            }

            // Gradient sky: horizon lifts toward white, ground drops dark.
            _skyMat ??= new ProceduralSkyMaterial();
            _skyMat.SkyTopColor = top;
            var horizon = top.Lerp(Colors.White, 0.45f);
            _skyMat.SkyHorizonColor = horizon;
            _skyMat.GroundBottomColor = new Color(0.10f, 0.12f, 0.16f);
            _skyMat.GroundHorizonColor = horizon.Lerp(new Color(0.10f, 0.12f, 0.16f), 0.4f);
            var gradSky = new Sky();
            gradSky.SkyMaterial = _skyMat;
            _env.BackgroundMode = global::Godot.Environment.BGMode.Sky;
            _env.Sky = gradSky;
        }

        /// <summary>First EnvironmentComponent (selected world first) + whether any
        /// directional light exists. Live world query: environment has no snapshot
        /// feed yet, and this runs at 2 Hz, not per frame.</summary>
        private static void FindWorldState(GameRoot root, out EnvironmentComponent? env, out bool hasGameDirectional)
        {
            env = null;
            hasGameDirectional = false;

            var worlds = new List<World>();
            try
            {
                if (root.SelectedWorld != null)
                    worlds.Add(root.SelectedWorld);
                if (root.PersistentWorld != null && root.PersistentWorld != root.SelectedWorld)
                    worlds.Add(root.PersistentWorld);
            }
            catch
            {
                return;
            }

            foreach (var world in worlds)
            {
                IWorldElement[] roots;
                try
                {
                    roots = world.Root.ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var rootEl in roots)
                {
                    Walk(rootEl, ref env, ref hasGameDirectional);
                    if (env != null && hasGameDirectional) return;
                }
            }
        }

        private static void Walk(IWorldElement el, ref EnvironmentComponent? env, ref bool hasGameDirectional)
        {
            if (el?.Components == null) return;

            if (env == null)
                env = el.GetComponent<EnvironmentComponent>();

            if (!hasGameDirectional)
            {
                var l = el.GetComponent<ILightRenderable>();
                if (l != null && l.Type == LightType.Directional)
                    hasGameDirectional = true;
            }

            if (env != null && hasGameDirectional) return;

            foreach (var child in el.Children.ToArray())
            {
                Walk(child, ref env, ref hasGameDirectional);
                if (env != null && hasGameDirectional) return;
            }
        }
    }
}
