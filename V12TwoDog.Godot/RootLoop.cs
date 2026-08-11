using Godot;
using System;

/// <summary>
/// Thin scene adapter bound to <c>res://RootLoop.cs</c> by <c>main.tscn</c>.
/// All V12 wiring (GameRoot, renderer, audio, physics, XR, input, networking,
/// gamepaks, worker thread) now lives in <see cref="V12TwoDog.V12Runtime"/>,
/// so any .NET host that can run Godot (2dog hosts, WinForms/WPF apps,
/// headless servers, tests) can embed the same runtime.
/// </summary>
public partial class RootLoop : Node3D
{
    private V12TwoDog.V12Runtime _runtime;

    /// <summary>The V12 runtime created by this scene, once <c>_Ready</c> has run.</summary>
    public V12TwoDog.V12Runtime Runtime => _runtime;

    public override void _Input(Godot.InputEvent @event)
    {
        _runtime?.HandleInputEvent(@event);
    }

    public override void _Ready()
    {
        var options = new V12TwoDog.V12RuntimeOptions
        {
            // Prefer gamepaks shipped by the embedding app itself. A consumer
            // game (e.g. EcoVR in /game) carries its own IV12Gamepack and must
            // NOT load the bundled Sample Game from libs/nova/V12.SampleGame —
            // its entry assembly is used instead. Bare hosts (editor Play, plain
            // 2dog hosts) fall back to the Sample Game so they still get a scene.
            GamepakAssemblies = DiscoverHostGamepaks() ?? new[]
            {
                typeof(V12.SampleGame.SampleGamePack).Assembly
            },
            ShowLauncher = false,
            CreateEmptyWorldIfNoPak = false,
        };
        ApplyNetworkArgs(options);
        _runtime = V12TwoDog.V12Runtime.Create(this, options);
    }

    /// <summary>
    /// Networking mode from the command line (args are forwarded through the 2dog
    /// host into Godot, so they show up in <c>OS.GetCmdlineArgs()</c>):
    /// <list type="bullet">
    /// <item><c>--connect=host[:port]</c> — run as a client of that server; the
    /// in-game multiplayer UI connects on demand (port defaults to 7777).</item>
    /// <item><c>--port=N</c> — listen port when running as a server (default 7777).</item>
    /// </list>
    /// With no args the game runs as a server (listens on <c>--port</c>).
    /// </summary>
    private static void ApplyNetworkArgs(V12TwoDog.V12RuntimeOptions options)
    {
        foreach (var arg in OS.GetCmdlineArgs())
        {
            if (arg.StartsWith("--connect=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg.Substring("--connect=".Length).Trim();
                if (value.Length == 0) continue;

                int sep = value.LastIndexOf(':');
                if (sep > 0 && int.TryParse(value.Substring(sep + 1), out var port) && port > 0 && port < 65536)
                {
                    options.ConnectHost = value.Substring(0, sep);
                    options.NetworkPort = port;
                }
                else
                {
                    options.ConnectHost = value;
                }
            }
            else if (arg.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(arg.Substring("--port=".Length), out var port) && port > 0 && port < 65536)
                    options.NetworkPort = port;
            }
        }
    }

    /// <summary>
    /// Returns the embedding app's own assembly when it defines at least one
    /// <see cref="V12.IV12Gamepack"/> implementation, otherwise null (which
    /// makes the caller fall back to the bundled Sample Game).
    /// </summary>
    private static System.Reflection.Assembly[]? DiscoverHostGamepaks()
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly();
        if (entry == null) return null;
        try
        {
            foreach (var type in entry.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (typeof(V12.IV12Gamepack).IsAssignableFrom(type))
                    return new[] { entry };
            }
        }
        catch
        {
            // Reflection over the entry assembly failed — fall back to the Sample Game.
        }
        return null;
    }

    public override void _PhysicsProcess(double delta)
    {
        _runtime?.ProcessPhysics((float)delta);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            _runtime?.Shutdown();
            _runtime = null;
        }
    }
}
