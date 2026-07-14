# Nova SDK

A **library for Godot editor plugins** that converts Godot 3D scenes into V12's WorldML XML format or packed `.V12World` archives. This is how content creators author worlds -- build in Godot, then export to run on V12.

## Purpose

Nova SDK provides the tooling to:
1. Walk a Godot scene tree and convert nodes to WorldML XML components
2. Package the exported XML + collected assets into `.V12World` zip archives
3. Register custom V12 component nodes that appear in Godot's "Create New Node" dialog

## Installation

Copy the `Nova.SDK/` folder or the `worldml_exporter/` addon into your Godot project's `addons/` directory.

## Key Files

### `worldml_exporter/`

| File | Purpose |
|---|---|
| `WorldMLExporter.cs` | Core exporter -- recursively walks a Godot scene tree and emits WorldML XML. Handles Node3D, Control, CanvasLayer, WorldEnvironment nodes. Skips physics body nodes (harvests their colliders). Supports custom V12 component nodes via `IV12ComponentNode` interface or `_v12_component_xml()` GDScript method. |
| `V12WorldPacker.cs` | Packages exported XML + collected assets (meshes, textures, audio) into a `.V12World` zip archive. |
| `NodeConverter3D.cs` | Converts Godot 3D nodes to WorldML XML components: transforms, meshes (Box, Sphere, Capsule, Cylinder, Plane, Custom), CSG operations, materials (PBR with textures), lights, cameras, particles, fog, audio, colliders, physics bodies, labels, custom mesh vertex/index data. |
| `NodeConverterUI.cs` | Converts Godot Control nodes to UI WorldML components: Buttons, CheckBoxes, Sliders, SpinBoxes, LineEdits, TextEdits, ProgressBars, Labels, VBox/HBox containers, TextureRects, Panels. |
| `IV12ComponentNode.cs` | Interface for Godot nodes that generate V12 component XML. |
| `README.md` | Comprehensive documentation: installation, quick start, V12 component node reference. |

### `Nodes/` (18 V12 component node types)

Each is a Godot `Node` or `Node3D` subclass that appears in the "Create New Node" dialog:

| Node | Purpose |
|---|---|
| `V12AudioSource` | 3D audio source |
| `V12Collision` | Physics collision shape |
| `V12CustomMesh` | Custom mesh with vertex/index data |
| `V12DirectionalLight` | Directional light |
| `V12Environment` | World environment settings |
| `V12Fog` | Fog volume |
| `V12Health` | Health component |
| `V12Interaction` | Interaction trigger |
| `V12Locomotion` | Player locomotion settings |
| `V12Material` | PBR material |
| `V12MeshAsset` | External 3D model reference (GLTF/GLB/OBJ) |
| `V12Particles` | GPU particle emitter |
| `V12Player` | Player spawn point |
| `V12PointLight` | Point light |
| `V12Script` | Lua script attachment |
| `V12SpawnPoint` | Spawn location marker |
| `V12Tag` | String tag |
| `V12Velocity` | Velocity component |

## Built-in Node Conversion

The exporter automatically converts standard Godot nodes:

| Godot Node | WorldML Component |
|---|---|
| Node3D | TransformComponent |
| MeshInstance3D | MeshComponent + MaterialComponent |
| OmniLight3D | PointLightComponent |
| SpotLight3D | SpotLightComponent |
| DirectionalLight3D | GenericLightComponent |
| Camera3D | CameraComponent |
| CPUParticles3D / GPUParticles3D | ParticleEmitterComponent |
| StaticBody3D / RigidBody3D / CharacterBody3D | ColliderComponent |
| Label3D | LabelComponent |
| AudioStreamPlayer3D | AudioSourceComponent |
| CSG* nodes | MeshComponent (baked triangles) |
| Control nodes | UI components (Button, Label, Slider, etc.) |

## Export Format

The `.V12World` format is a zip archive containing:
- `world.xml` - The WorldML scene description
- Extracted assets (meshes, textures, audio files)

## Dependencies

- **GodotSharp** 4.7.0 + **GodotSharpEditor** 4.7.0
- Project reference: `V12`
