## V12MeshAsset  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a <MeshAsset> during WorldML export.
## Loads a 3D mesh from an external file (GLB, GLTF, OBJ) via the V12 runtime.
## The parent element's TransformComponent controls world placement.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12MeshAsset
extends Node


## Path to the 3D model file (res://, user://, or absolute).
@export_file("*.glb;*.gltf;*.obj") var asset_path: String = ""

## Local position offset relative to parent element.
@export var position_x: float = 0.0
@export var position_y: float = 0.0
@export var position_z: float = 0.0

## Local rotation (degrees) relative to parent element.
@export var rotation_x: float = 0.0
@export var rotation_y: float = 0.0
@export var rotation_z: float = 0.0

## Local scale relative to parent element.
@export var scale_x: float = 1.0
@export var scale_y: float = 1.0
@export var scale_z: float = 1.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<Component type=\"MeshAsset\""
		+ " assetPath=\"%s\""
		+ " positionX=\"%.3f\" positionY=\"%.3f\" positionZ=\"%.3f\""
		+ " rotationX=\"%.3f\" rotationY=\"%.3f\" rotationZ=\"%.3f\""
		+ " scaleX=\"%.3f\" scaleY=\"%.3f\" scaleZ=\"%.3f\""
		+ " />\n"
	) % [ind, asset_path.xml_escape(),
		position_x, position_y, position_z,
		rotation_x, rotation_y, rotation_z,
		scale_x, scale_y, scale_z]
