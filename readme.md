# Nova

Welcome to Nova (codenamed "V12TwoDog").

Nova is Finite's in-development renderer and frontend for the **V12 game engine**. It uses **Godot 4.7** as its rendering, input, audio, physics, and XR backend, while V12 provides an engine-agnostic ECS runtime, networking, physics, scripting, and world format (WorldML).

## Technology Stack

| Technology | Usage |
|---|---|
| C# / .NET 10 | All engine and game code |
| Godot 4.7 (C#) | Rendering, input, XR, audio, physics backend, editor |
| BepuPhysics 2.5 | Engine-agnostic physics simulation |
| MoonSharp 2.0 | Lua scripting runtime |
| LiteNetLib 1.1 | UDP networking transport |
| MongoDB.Bson | Network message serialization (BSON) |
| AssimpNetter 6.0 | 3D model import |
| Dear ImGui (GDExtension) | Debug/inspector UI in Godot |
| OpenXR | VR headset support |
| 2dog | Headless Godot embedding for .NET |

## Folder Structure

```
Nova/
├── Docs/                   # High-level project documentation
├── libs/
│   ├── V12/                # V12 engine core (git submodule)
│   └── V12.Basic/          # Standard gameplay components/systems (git submodule)
├── Nova.Shared/            # Godot-specific adapter layer
├── Nova.SDK/               # WorldML exporter SDK for Godot editor
├── V12TwoDog.2dog/         # Headless CLI entry point
└── V12TwoDog.Godot/        # Godot project frontend
```

## Architecture Overview

```
Godot main.tscn
  └── RootLoop.cs (_Ready)
        ├── Creates GameRoot (engine core)
        ├── Creates Renderer (Godot scene builder)
        ├── Registers: Audio, Physics, Input, XR, UI
        ├── Loads Game Paks from gamepaks/ directory
        ├── Sets up networking (client to localhost:7777)
        └── Starts V12 worker thread (60Hz game logic)
```

The main loop runs on two threads:

- **V12 Worker Thread (60Hz):** Runs `GameRoot.Update()` (all ECS systems), processes network messages, captures frame snapshots.
- **Godot Main Thread:** Consumes frame snapshots, builds/updates Godot scene nodes, processes input, manages audio and physics.

Frame snapshots (`FrameSnapshot`) are passed from the worker thread to the main thread via a concurrent queue. The `Renderer` class translates these snapshots into Godot `Node3D` objects (meshes, lights, cameras, labels, sprites).

## Getting Started

### Prerequisites

- .NET 10 SDK
- Godot 4.7 with C# support

### Building

```bash
dotnet build
```

### Running (Godot Editor)

Open the `V12TwoDog.Godot/` folder in Godot 4.7 and run the main scene.

### Running (Headless)

```bash
dotnet run --project V12TwoDog.2dog
```

Press `Q` to exit.

## Further Documentation

- [`Docs/`](Docs/) - High-level renderer documentation
- [`libs/V12/Docs/`](libs/V12/Docs/) - V12 engine guides (networking, dirty tracking, ECS queries, inspector, WorldML)
- [`libs/V12.Basic/readme.md`](libs/V12.Basic/readme.md) - Basic components and systems reference
- [`Nova.Shared/readme.md`](Nova.Shared/readme.md) - Godot adapter layer documentation
- [`Nova.SDK/readme.md`](Nova.SDK/readme.md) - WorldML exporter SDK documentation
- [`V12TwoDog.2dog/readme.md`](V12TwoDog.2dog/readme.md) - Headless CLI documentation
- [`V12TwoDog.Godot/readme.md`](V12TwoDog.Godot/readme.md) - Godot project documentation

nova is licenced under MIT with a exception for V12 licencing