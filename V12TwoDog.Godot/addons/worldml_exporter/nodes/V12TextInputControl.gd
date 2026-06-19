## V12TextInputControl  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a world-space <TextInputControlComponent>.
## Maps to a LineEdit inside a SubViewport + QuadMesh in the Godot scene.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12TextInputControl
extends Node


@export var text: String = ""
@export var placeholder: String = ""
## Optional header label displayed above the input field.
@export var label: String = ""

@export_group("Size & Offset")
@export var width: float = 0.5
@export var height: float = 0.13
@export var offset_x: float = 0.0
@export var offset_y: float = 0.0
@export var offset_z: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<TextInputControlComponent"
		+ " text=\"%s\""
		+ " placeholder=\"%s\""
		+ " label=\"%s\""
		+ " width=\"%.3f\" height=\"%.3f\""
		+ " offsetX=\"%.3f\" offsetY=\"%.3f\" offsetZ=\"%.3f\""
		+ " />\n"
	) % [ind, text.xml_escape(), placeholder.xml_escape(), label.xml_escape(),
		width, height, offset_x, offset_y, offset_z]
