## V12ButtonControl  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a world-space <ButtonControlComponent>.
## Maps to a Button inside a SubViewport + QuadMesh in the Godot scene.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12ButtonControl
extends Node


@export var label: String = "Button"

@export_group("Size & Offset")
@export var width: float = 0.5
@export var height: float = 0.13
@export var offset_x: float = 0.0
@export var offset_y: float = 0.0
@export var offset_z: float = 0.0

@export_group("Background Color")
@export var bg_r: float = 0.20
@export var bg_g: float = 0.22
@export var bg_b: float = 0.35
@export var bg_a: float = 1.0

@export_group("Text Color")
@export var text_r: float = 1.0
@export var text_g: float = 1.0
@export var text_b: float = 1.0
@export var text_a: float = 1.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<ButtonControlComponent"
		+ " label=\"%s\""
		+ " width=\"%.3f\" height=\"%.3f\""
		+ " offsetX=\"%.3f\" offsetY=\"%.3f\" offsetZ=\"%.3f\""
		+ " bgR=\"%.3f\" bgG=\"%.3f\" bgB=\"%.3f\" bgA=\"%.3f\""
		+ " textR=\"%.3f\" textG=\"%.3f\" textB=\"%.3f\" textA=\"%.3f\""
		+ " />\n"
	) % [ind, label.xml_escape(),
		width, height,
		offset_x, offset_y, offset_z,
		bg_r, bg_g, bg_b, bg_a,
		text_r, text_g, text_b, text_a]
