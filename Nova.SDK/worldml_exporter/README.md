# WorldML Exporter

Godot 4 addon that exports 3D scenes into V12's WorldML XML format (or packed `.V12World` archives) for use at runtime.

## Installation

The addon lives under `addons/worldml_exporter/`. Enable it in **Project Settings > Plugins**.

## Quick Start

1. Build a scene with `Node3D` as the root.
2. Add children (CSG nodes, MeshInstance3D, lights, cameras, etc.).
3. Attach **V12*** component nodes as children to tag elements with gameplay data.
4. Click the **Export WorldML** button in the bottom dock (or **Export As…** to pick a path).

Two output formats are produced based on the file extension:
- **`.xml`** — raw WorldML XML file
- **`.V12World`** — zipped archive containing `world.xml` + extracted textures/assets

---

## Scene Layout

Every `Node3D` in the scene becomes a `<WorldML>` `<Element>` with a `<TransformComponent>`. Children are processed recursively.

```text
MyWorld (Node3D)           → <World name="MyWorld">
  ├── Ground (CSGBox3D)    →   <Element name="Ground">
  │   └── V12Material      →     <MaterialComponent ... />
  ├── PlayerSpawn (V12Player) → <Element name="PlayerSpawn">
  │                              <TransformComponent ... />
  │                              <PlayerComponent ... />
  └── Coin (CSGSphere3D)   →   <Element name="Coin">
      ├── V12Collision     →     <ColliderComponent ... />
      └── V12Script        →     <ScriptComponent ... />
```

---

## V12 Component Nodes

All nodes listed below live under the `V12TwoDog.Editor.Nodes` namespace.  
They appear in the **Create New Node** dialog under their class name (search "V12").

Each component node attaches as a **child** of the element it describes. Its exported XML is inlined into the parent element — the node itself is not exported as a separate element.

### Player & Spawning

| Node | Extends | XML Output | Description |
|------|---------|------------|-------------|
| **V12Player** | `Node3D` | `<PlayerComponent>` | Player spawn point — preferred input method (Auto/Desktop/XR), move speed, sprint, jump, VR settings. Exported as a standalone `<Element>` with its own transform. |
| **V12SpawnPoint** | `Node3D` | `<SpawnPointComponent>` | Generic spawn location marker. Exported as a standalone `<Element>`. |

### Physics

| Node | Extends | XML Output | Description |
|------|---------|------------|-------------|
| **V12Collision** | `Node3D` | `<ColliderComponent>` | Physics collision shape — Box, Sphere, Capsule, Cylinder, or Plane. Configurable Width/Height/Depth and trigger mode. Gizmo: orange transparent shape that updates live. |
| **V12Velocity** | `Node` | `<VelocityComponent>` | Linear and angular velocity for moving/spinning elements. |

### Rendering & Appearance

| Node | Extends | XML Output | Description |
|------|---------|------------|-------------|
| **V12Material** | `Node` | `<MaterialComponent>` | Override surface appearance: albedo RGBA, metallic, roughness, and UV tiling/offset. |
| **V12MeshAsset** | `Node` | `<MeshAsset>` | Reference an external GLTF/GLB/OBJ file with local transform offsets. |
| **V12CustomMesh** | `Node` | `<MeshComponent Shape="Custom">` | Define a custom mesh via vertex/index data, or auto-extract from a parent `MeshInstance3D`. |

### Lighting

| Node | Extends | XML Output | Description |
|------|---------|------------|-------------|
| **V12DirectionalLight** | `Node` | `<GenericLightComponent>` | Directional (sun) light with color, energy, and shadow toggle. |
| **V12PointLight** | `Node3D` | `<PointLightComponent>` | Omnidirectional point light — color, range, energy. Gizmo: small glowing sphere that matches the set color. |

### Audio

| Node | Extends | XML Output | Description |
|------|---------|------------|-------------|
| **V12AudioSource** | `Node3D` | `<AudioSourceComponent>` | 3D positional audio source — clip path (.ogg/.mp3/.wav), volume, pitch, loop, autoplay, max distance. Gizmo: small blue sphere. |

### Particles

| Node | Extends | XML Output | Description |
|------|---------|------------|-------------|
| **V12Particles** | `Node3D` | `<ParticleEmitterComponent>` | GPU particle emitter — amount, lifetime, speed range, emission direction, spread angle, emission radius, one-shot mode. Gizmo: purple sphere with orbiting dots. |

### Environment

| Node | Extends | XML Output | Description |
|------|---------|------------|-------------|
| **V12Environment** | `Node` | `<EnvironmentComponent>` | World environment — sky mode (SolidColor or Skybox), sky/ambient colors, ambient energy, skybox path. |
| **V12Fog** | `Node` | `<FogComponent>` | Fog volume — color, density, height, height falloff. |
| **V12DirectionalLight** | `Node` | `<GenericLightComponent>` | See Lighting section above. |

### Gameplay & Logic

| Node | Extends | XML Output | Description |
|------|---------|------------|-------------|
| **V12Script** | `Node` | `<ScriptComponent>` | Attach a Lua script via file path or inline source text. |
| **V12Tag** | `Node` | `<TagComponent>` | Comma-separated string tags for filtering and categorization. |
| **V12Locomotion** | `Node` | `<LocomotionComponent>` | Player movement controller — walk/sprint/jump speeds, look sensitivity, VR locomotion settings. |
| **V12Interaction** | `Node` | `<InteractionComponent>` | Declares pointing and selecting capabilities for the player. |
| **V12Health** | `Node` | `<HealthComponent>` | Health pool with max HP and optional invincibility. |

---

## Built-in Node Conversion

The exporter automatically handles standard Godot nodes without needing V12 component children:

| Godot Node | Exported As |
|-----------|-------------|
| `CSGBox3D`, `CSGSphere3D`, `CSGCylinder3D`, `CSGCapsule3D`, `CSGPlane3D`, `CSGMesh3D` | `<MeshComponent>` + `<ColliderComponent>` (if `use_collision` is on) |
| `MeshInstance3D` | `<MeshComponent>` from the mesh resource |
| `OmniLight3D` | `<GenericLightComponent Type="Point">` |
| `SpotLight3D` | `<GenericLightComponent Type="Spot">` |
| `DirectionalLight3D` | `<GenericLightComponent>` |
| `Camera3D` | `<CameraComponent>` |
| `GpuParticles3D` | `<ParticleEmitterComponent>` |
| `FogVolume` | `<FogComponent>` |
| `Label3D` | `<UILabelComponent>` |
| `AudioStreamPlayer3D` | `<AudioSourceComponent>` |
| `StaticBody3D` / `RigidBody3D` / `Area3D` w/ `CollisionShape3D` child | `<ColliderComponent>` |
| `RigidBody3D` (parent of element) | `<RigidBodyComponent>` |
| `WorldEnvironment` | `<EnvironmentComponent>` |
| `CanvasLayer` | `<CanvasComponent>` |
| `Control` nodes | UI components via `NodeConverterUI` |

**Material export**: CSG nodes and MeshInstance3D with `StandardMaterial3D` materials export:
- Albedo color, metallic, roughness
- Albedo, normal, metallic, roughness, and emission texture paths (as `v12://` paths when a world name is provided)
- UV1 offset and scale

---

## Export Formats

### XML (`.xml`)

```xml
<?xml version="1.0" encoding="utf-8"?>
<World name="MyScene">
  <Element name="Ground">
    <TransformComponent x="0.000" y="-0.500" z="0.000" rotation="0.000" ... />
    <Component type="MeshComponent" name="csg_mesh" Shape="Box" Width="10.000" Height="1.000" Depth="10.000" />
    <Component type="MeshRenderer" Mesh="csg_mesh" />
    <MaterialComponent R="0.800" G="0.800" B="0.800" A="1.000" Metallic="0.000" Roughness="0.800" />
  </Element>
</World>
```

### V12World Archive (`.V12World`)

A zip archive containing:
- `world.xml` — the exported scene
- `templates/` — template definitions
- Extracted texture files referenced by `v12://` paths

Texture paths in the XML are converted from `res://` to `v12://{worldName}/...` so the runtime loader can resolve them from the extracted temp directory.

---

## Notes

- **Gizmo nodes** (child `MeshInstance3D` nodes named `_V12Gizmo`) are editor-only. They self-destruct at runtime and are excluded from export.
- Nodes in Godot's `StaticBody3D`, `RigidBody3D`, `AnimatableBody3D`, `CharacterBody3D`, `Area3D`, `CollisionShape3D`, and `CollisionPolygon3D` classes are skipped during tree traversal — their collider data is harvested and attached to the parent element instead.
- The `Export WorldML` button re-exports to the last-used path. Use **Export As…** to change the destination.
