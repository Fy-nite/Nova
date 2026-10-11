using Godot;
using System;
using System.Collections.Generic;
using V12;
using Engine = twodog.Engine;

namespace V12TwoDog
{
    /// <summary>
    /// Library bootstrap for embedding the V12 runtime (Godot as a library via
    /// 2dog) inside any .NET application.
    ///
    /// <para>
    /// <see cref="Start"/> boots a headless-able Godot instance pointed at the
    /// Godot project (via <c>GodotProjectDir</c> assembly metadata or an
    /// explicit path) and runs its <c>run/main_scene</c> — which is
    /// <c>main.tscn</c> → <c>RootLoop</c> → <see cref="V12Runtime"/>. All the
    /// V12 wiring happens there; your app only owns the frame pump.
    /// </para>
    ///
    /// <example>
    /// <code>
    /// [STAThread]
    /// static void Main(string[] args)
    /// {
    ///     using var host = new V12Host();
    ///     host.Start(args: args);
    ///     while (!host.Iteration())
    ///     {
    ///         // your per-frame logic
    ///     }
    /// }
    /// </code>
    /// </example>
    ///
    /// Notes:
    /// <list type="bullet">
    /// <item>Only one Godot instance may run per process. Dispose fully before restarting.</item>
    /// <item>On Windows the hosting thread should be STA ([STAThread]) for OLE/IME/dialogs.</item>
    /// </list>
    /// </summary>
    public sealed class V12Host : IDisposable
    {
        private Engine? _engine;
        private GodotInstance? _godot;
        private readonly List<IV12Gamepack> _pendingGamepaks = new();

        /// <summary>The live scene tree after <see cref="Start"/>.</summary>
        public SceneTree? Tree => _engine?.Tree;

        /// <summary>True once <see cref="Start"/> succeeded and the main scene is running.</summary>
        public bool IsRunning => _godot != null;

        /// <summary>
        /// Boot Godot and run the main scene.
        /// </summary>
        /// <param name="projectDir">
        /// Directory containing <c>project.godot</c>. When null,
        /// <see cref="Engine.ResolveContent"/> is used: raw project assets in
        /// development (from the <c>GodotProjectDir</c> metadata this host
        /// embeds), or an exe-adjacent <c>.pck</c> in published builds.
        /// </param>
        /// <param name="args">Extra Godot command-line arguments, forwarded unchanged.</param>
        public void Start(string? projectDir = null, params string[] args)
        {
            if (_engine != null)
                throw new InvalidOperationException("V12Host already started. Dispose before restarting.");

            _engine = new Engine("V12TwoDog", projectDir ?? Engine.ResolveContent(), args);
            _godot = _engine.Start(); // runs run/main_scene (main.tscn → RootLoop → V12Runtime)

            // The scene is up and RootLoop has built the runtime — hand over any
            // paks registered before Start() (RootLoop already consumed them via
            // GamepakHandoff, so the runtime de-dupes by name).
            if (ResolveRuntime() is { } runtime)
                foreach (var pak in _pendingGamepaks)
                    runtime.RegisterGamePak(pak);
        }

        /// <summary>
        /// Pump one frame from your own loop. Returns <c>true</c> when Godot
        /// requests quit (e.g. the window closed).
        /// </summary>
        public bool Iteration()
        {
            if (_godot == null)
                throw new InvalidOperationException("V12Host not started. Call Start() first.");
            return _godot.Iteration();
        }

        /// <summary>Block until quit, invoking <paramref name="perFrame"/> once per frame.</summary>
        public void Run(Action? perFrame = null)
        {
            if (_engine == null)
                throw new InvalidOperationException("V12Host not started. Call Start() first.");
            _engine.Run(perFrame);
        }

        public void Dispose()
        {
            _godot?.Dispose();
            _godot = null;
            _engine?.Dispose();
            _engine = null;
        }

        /// <summary>
        /// Hand a game pak to the embedded runtime. Safe before or after
        /// <see cref="Start"/>: before, RootLoop picks it up when it builds the
        /// runtime; after, it is registered on the live runtime.
        /// </summary>
        public void RegisterGamePak(IV12Gamepack pak)
        {
            if (pak == null) return;

            if (!_pendingGamepaks.Contains(pak))
                _pendingGamepaks.Add(pak);

            GamepakHandoff.Register(pak);
            ResolveRuntime()?.RegisterGamePak(pak);
        }

        /// <summary>The live runtime, or null before the scene main loop is up.</summary>
        private V12Runtime? ResolveRuntime()
            => Tree?.CurrentScene as RootLoop is { } loop ? loop.Runtime : null;

    }
}
