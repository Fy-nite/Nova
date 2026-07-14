# Nova.Shared

The **Godot adapter layer** for the V12 engine. This library bridges the engine-agnostic V12 core to the **Godot 4.7 runtime**, providing Godot-specific implementations of rendering, audio, physics, UI, input, networking, and XR support.

## Purpose

V12's core libraries (`V12` and `V12.Basic`) are engine-agnostic. `Nova.Shared` contains the concrete implementations that run inside Godot, translating V12's abstract interfaces into Godot API calls.

## Key Files

| File | Purpose |
|---|---|
| `RootLoop.cs` | **Main entry point.** A Godot `Node3D` that orchestrates the entire engine: creates `GameRoot`, registers all services, sets up networking, loads game paks, and runs the V12 worker thread. The core loop passes frame snapshots from the worker thread to the Godot main thread. |
| `Renderer.cs` | Implements `IRenderer`. Builds and maintains a Godot scene tree from `FrameSnapshot` data. Creates `MeshInstance3D`, `Light3D`, `Camera3D`, `Sprite3D`, and `Label3D` nodes. Handles material/texture loading, custom mesh vertex buffers, and SVG rendering. |
| `NetworkHandler.cs` | Orchestrates network message dispatch. Queues messages from the networking thread and processes them on the V12 worker thread (WorldSync, PlayerSync, Heartbeat, WorldUpdate, PlayerLeave). |
| `WorldSyncHandler.cs` | Handles WorldSync (full world replacement from server), WorldArchive (receive + extract `.V12World` zip), and WorldUpdate (incremental component patching via DirtyTracker). |
| `RemotePlayerManager.cs` | Creates, updates, and removes remote player elements. Interpolates positions with exponential easing (tween speed 12). |
| `InputHandler.cs` | Translates Godot keyboard, mouse, and gamepad input into V12 `InputService` events. Maps WASD, sticks, buttons, fly mode, and debug toggle (F10). |
| `XRTrackingService.cs` | OpenXR integration. Creates `XROrigin3D`, `XRCamera3D`, `XRController3D` nodes. Polls HMD/hand poses and syncs them to V12 ECS elements. Maps VR controller buttons/axes to V12 input events. |
| `GodotAudioPlayer.cs` | 3D positional audio via Godot's audio system. |
| `GodotPhysicsBackend.cs` | Implements `IPhysicsBackend` using Godot's physics engine. |
| `GodotMainThread.cs` | Thread-safe deferred call queue for V12 worker -> Godot main thread communication. |
| `DebugGameService.cs` | Debug overlay showing world hierarchy and component state via `Label3D` nodes. |
| `GamepakLauncher.cs` | ImGui-based launcher UI for selecting game paks. |
| `LaserVisual.cs` | VR laser pointer visual. |
| `PortalBinding.cs` | Portal rendering integration. |
| `Globals.cs` | Simple static helpers (e.g., `GetFPS` delegate). |
| `WorldInspector.cs` | World inspector UI. |
| `NetworkInspector.cs` | Network status inspector UI. |

## Subdirectories

### `Rendering/`

| File | Purpose |
|---|---|
| `GodotRenderTargetFactory.cs` | Creates Godot render targets |
| `RenderTargetGodot.cs` | Godot-specific render target implementation |

### `UI/`

Godot implementations of V12's abstract UI framework (15 files):

| File | Widget |
|---|---|
| `GodotUIProvider.cs` | Factory for all Godot UI widgets |
| `GodotButton.cs` | Button |
| `GodotLabel.cs` | Label |
| `GodotPanel.cs` | Panel |
| `GodotTextField.cs` | Text input |
| `GodotDropdown.cs` | Dropdown |
| `GodotScrollView.cs` | Scroll view |
| `GodotTabControl.cs` | Tab control |
| `GodotToolbar.cs` | Toolbar |
| `GodotModal.cs` | Modal dialog |
| `GodotDock.cs` | Dock panel |
| `GodotWindow.cs` | Window |
| `GodotHBox.cs` | Horizontal layout |
| `GodotVBox.cs` | Vertical layout |
| `GodotViewportHost.cs` | Viewport host |

## Dependencies

- **GodotSharp** 4.7.0 + **GodotSharpEditor** 4.7.0
- Project references: `V12.Basic`, `V12`

## Thread Model

The engine runs on two threads:

1. **V12 Worker Thread** - Runs `GameRoot.Update()` at 60Hz, processes network messages, captures frame snapshots.
2. **Godot Main Thread** - Consumes frame snapshots, updates Godot scene tree, processes input, manages audio and physics.

Frame snapshots are passed between threads via a `ConcurrentQueue<FrameSnapshot>`. Godot API calls from the worker thread are deferred via `GodotMainThread`.
