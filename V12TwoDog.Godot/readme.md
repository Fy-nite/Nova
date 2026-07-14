# V12TwoDog.Godot

The **Godot 4.7 project** that serves as the actual runtime frontend for Nova. Contains the Godot scene tree, addons, input mapping, world data, test scenes, and the C# game loop.

## Project Configuration

- **Engine:** Godot 4.7 with C# (.NET)
- **Resolution:** 1280x720, VSync Ad
- **Physics:** 1000 ticks/second, separate physics threads, physics interpolation
- **XR:** Shaders enabled, gyroscope enabled
- **Entry Point:** `main.tscn` with `RootLoop.cs` script

## Main Scene (`main.tscn`)

```
Game (Node3D)
├── WorldEnvironment (procedural sky + SDFGI + glow)
├── DirectionalLight3D (shadow)
├── WorldRoot (Node3D, RootLoop.cs script)
│   └── GenericStuff
└── ImGuiApi
```

## Input Actions

| Action | Keys/Buttons |
|---|---|
| Movement | WASD + Left stick |
| Look | Mouse + Right stick |
| Jump | Space / A button |
| Run | Shift |
| Crouch | Ctrl |
| Interact | R / RT |
| Lean | Q/E |
| Fly | Backslash (toggle), Space (up), Shift (down) |
| Escape | Escape |
| Summon Cube | Numpad - |

## Addons

| Addon | Purpose |
|---|---|
| `dear-imgui-godot` | Dear ImGui integration for debug UI, inspector, launcher |
| `debug_menu` | In-game debug menu overlay |
| `godot_mcp` | MCP (Model Context Protocol) server for AI tool integration |
| `godotopenxrvendors` | OpenXR vendor extensions (headset support) |
| `portals` | Portal rendering system |
| `worldml_exporter` | WorldML exporter plugin (copy of Nova.SDK) |
| `at-icons` | Icon assets |
| `goblend` | Additional addon |

## Subdirectories

### `worldml/`

Contains WorldML world data:
- `world.xml` - A "Prototype" world (room with walls, floor, roof, door, lighting)
- `thing.V12World` - A packed V12World archive (zip)

### `Testing/`

- `portalworld/` - Portal testing scene

### `FPS Controller/`

FPS controller asset/template:
- `common/` - Shared scripts/resources
- `entities/` - Player entity definitions
- `materials/` - Materials
- `stages/` - Level/stage data

### `demo/`

Test scene with `test.tscn`, `test.gd`, and `assets/`.

### `audio/`

Test audio files: `high_beep.wav`, `low_hum.wav`, `test_tone.wav`.

### `prototype_textures/`

Prototype/placeholder textures for testing.

## C# Files

Several `.cs` files in this directory are partial classes or references to implementations in `Nova.Shared`:
- `Renderer.cs (partial)` - Extends the shared Renderer
- `RootLoop.cs`, `NetworkHandler.cs`, `Globals.cs`, etc. - Reference shared implementations via `.uid` files

## Dependencies

- **GodotSharp** 4.7.0 + **GodotSharpEditor** 4.7.0
- Project references: `Nova.Shared`
