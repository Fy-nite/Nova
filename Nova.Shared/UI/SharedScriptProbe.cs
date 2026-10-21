using Godot;

namespace V12TwoDog.Godot.UI
{
    /// <summary>
    /// --probe-shared: does native->managed virtual dispatch (_Ready/_Process/
    /// _Input) work for a GodotObject subclass compiled in Nova.Shared, which
    /// builds with plain GodotSharp (no Godot.SourceGenerators)? Driven by
    /// V12Studio.Godot.SharedDispatchProbe; also usable directly from any
    /// host that wants a Nova.Shared-typed dispatch test.
    /// </summary>
    public sealed partial class SharedScriptProbe : Node
    {
        public int ReadyCount;
        public int ProcessCount;
        public int InputCount;
        public string LastInput = "";

        private bool _loggedFirstProcess;

        public override void _Ready()
        {
            ReadyCount++;
            GD.Print("[SharedScriptProbe] _Ready fired");
        }

        public override void _Process(double delta)
        {
            ProcessCount++;
            if (!_loggedFirstProcess)
            {
                _loggedFirstProcess = true;
                GD.Print("[SharedScriptProbe] _Process fired");
            }
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouse)
            {
                InputCount++;
                LastInput = @event.GetType().Name;
                GD.Print($"[SharedScriptProbe] _Input {LastInput}");
            }
        }
    }
}
