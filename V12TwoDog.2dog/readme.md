# V12TwoDog.2dog

A **minimal console entry point** for running V12 outside of the Godot editor. Uses the `2dog` package (a .NET wrapper for headless Godot instances) to launch a Godot runtime.

## Purpose

Provides a way to run the V12 engine without the full Godot editor, useful for headless servers, testing, or CLI-driven workflows.

## How It Works

`Program.cs` creates a `2dog.Engine` instance, starts a headless Godot runtime, and runs an iteration loop until `Q` is pressed:

```csharp
using var engine = new Engine("V12TwoDog", Engine.ResolveProjectDir());
using var godot = engine.Start();

while (!godot.Iteration())
{
    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
        break;
    Update(godot, engine);
}
```

## Running

```bash
dotnet run --project V12TwoDog.2dog
```

Press `Q` to exit.

## Dependencies

- **2dog** 4.7.0.x (NuGet) - Headless Godot embedding
- Project reference: `V12`
- The Godot project directory points to `../V12TwoDog.Godot`

## Configuration

The `V12TwoDog.csproj` sets:
- `TwoDogVariant` = `editor` (uses the editor variant of Godot)
- `GodotProjectDir` = `../V12TwoDog.Godot` (path to the Godot project)
