# V12TwoDog.2dog

The **V12 host library** — embeds the full V12 runtime (Godot as a library via
the `2dog` package) so any .NET application can run V12 without the Godot
editor. This is a **library** now, not a console entry point: reference it from
your own app and boot it with `V12TwoDog.V12Host`.

## Purpose

Provides the reusable bridge between:
- **2dog** (`twodog.Engine`) — Godot as an embeddable .NET library
- **V12TwoDog.Godot** — the Godot game project (`main.tscn` → `RootLoop`)
- **V12Runtime** (in `Nova.Shared`) — all the V12 wiring (GameRoot, renderer,
  audio, physics, XR, input, networking, gamepaks, worker thread)

The consumer only owns the frame pump.

## How It Works

```csharp
[STAThread]
static void Main(string[] args)
{
    using var host = new V12Host();
    host.Start(args: args);          // boots Godot, runs run/main_scene
    while (!host.Iteration()) { }    // your per-frame logic
}
```

`Start()` uses `Engine.ResolveContent()` — raw project assets during
development (from the `GodotProjectDir` metadata this host embeds) or an
exe-adjacent `.pck` in published builds.

## Consuming

Add a project reference:

```xml
<ProjectReference Include="..\V12TwoDog.2dog\V12TwoDog.csproj" />
```

See `V12.ConsumerApp` for a complete minimal consumer:

```bash
dotnet run --project V12.ConsumerApp
```

The Sample Game auto-starts: `RootLoop` loads it via `ProjectReference`
(no `--gamepak` arg or DLL scanning needed). Press `Q` to exit.

## Configuration

The `V12TwoDog.csproj` sets:
- `TwoDogVariant` = `editor` (uses the editor variant of Godot — required for
  the worldml import pipeline)
- `GodotProjectDir` = `../V12TwoDog.Godot` (path to the Godot project)
- `TwoDogRemoveDuplicateGodotAnalyzers` = `true` (host references a
  `Godot.NET.Sdk` game project *and* `2dog.engine`, which both ship
  `Godot.SourceGenerators`)

## Notes

- Only one Godot instance per process. Dispose the host fully before restarting.
- On Windows, host the engine from an STA thread (`[STAThread]`) for OLE
  drag/drop, IME, and native dialogs.
- `PublishSingleFile` / `PublishAot` are unsupported (2dog loads the game
  assembly through hostfxr from on-disk files).

