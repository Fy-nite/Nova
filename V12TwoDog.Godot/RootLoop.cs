using Godot;

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
            // Load the Sample Game via direct project reference (see
            // V12TwoDog.Godot.csproj) so it runs when pressing Play in the
            // editor — including on a VR headset — with no gamepak DLL scanning.
            GamepakAssemblies = new[]
            {
                typeof(V12.SampleGame.SampleGamePack).Assembly
            },
            ShowLauncher = false,
            CreateEmptyWorldIfNoPak = false,
        };
        _runtime = V12TwoDog.V12Runtime.Create(this, options);
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
