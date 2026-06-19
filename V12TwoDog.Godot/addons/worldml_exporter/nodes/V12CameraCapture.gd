## V12CameraCapture  (data-attachment node)
## Add this as a CHILD of any Node3D to mark it as a render-to-texture camera,
## attaching a <CameraCaptureComponent> during WorldML export.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12CameraCapture
extends Node


@export_range(1.0, 179.0) var fov: float = 75.0
@export var near: float = 0.05
@export var far: float = 1000.0

@export_group("Texture")
@export var texture_width: int = 512
@export var texture_height: int = 512
## Logical name other elements can reference to sample this capture.
@export var texture_name: String = ""


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<CameraCaptureComponent"
		+ " fov=\"%.1f\""
		+ " near=\"%.4f\""
		+ " far=\"%.1f\""
		+ " textureWidth=\"%d\""
		+ " textureHeight=\"%d\""
		+ " textureName=\"%s\""
		+ " />\n"
	) % [ind, fov, near, far, texture_width, texture_height, texture_name]
