using Godot;
using V12TwoDog;

/// <summary>
/// Minimal consumer app proving the library flow:
///
///   consumer exe → V12Host (library) → twodog.Engine → Godot (libgodot)
///       → main.tscn → RootLoop → V12Runtime (all V12 wiring)
///
/// The consumer only owns the frame pump; everything else is embedded.
/// Run with:  dotnet run --project V12.ConsumerApp -- --gamepak=Sample Game
/// (press Q to exit, or close the window)
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Console.WriteLine("Consumer app booting V12 (Godot embedded via 2dog)...");

        using var host = new V12Host();
        host.Start(args: args); // forwards --gamepak=... etc. straight to Godot

        // ── The scene is up; the runtime is reachable through the scene root ──
        if (host.Tree?.CurrentScene is RootLoop rootLoop && rootLoop.Runtime is { } runtime)
        {
            GD.Print($"[Consumer] V12Runtime active. SelectedWorld='{runtime.Root.SelectedWorld?.WorldName}', " +
                     $"gamepaks={runtime.Root.Gamepaks.Gamepaks.Count}, XR={(runtime.XrAvailable ? "on" : "off")}");

            // Same UI path the Godot editor runs — the WorldCanvasSystem lives in
            // V12Runtime, so both hosts build world/screen-space canvases identically.
            var canvasCount = CountNodes(host.Tree?.Root, "UICanvas_");
            GD.Print($"[Consumer] WorldCanvasSystem active: {canvasCount} canvas node(s) in the scene tree.");
        }

        while (!host.Iteration())
        {
            // Exit on Q (gracefully ignore when there's no console to read from).
            try
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                    break;
            }
            catch (InvalidOperationException) { }
        }

        Console.WriteLine("Consumer app shutting down.");
    }

    private static int CountNodes(Node? node, string prefix)
    {
        if (node == null) return 0;
        int count = node.Name.ToString().StartsWith(prefix) ? 1 : 0;
        foreach (var child in node.GetChildren())
            count += CountNodes(child, prefix);
        return count;
    }
}
