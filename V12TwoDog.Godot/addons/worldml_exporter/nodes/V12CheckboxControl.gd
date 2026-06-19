## V12CheckboxControl  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a world-space <CheckboxControlComponent>.
## Maps to a CheckBox inside a SubViewport + QuadMesh in the Godot scene.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12CheckboxControl
extends Node


@export var label: String = "Checkbox"
@export var checked: bool = false

@export_group("Size & Offset")
@export var width: float = 0.4
@export var height: float = 0.10
@export var offset_x: float = 0.0
@export var offset_y: float = 0.0
@export var offset_z: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<CheckboxControlComponent"
		+ " label=\"%s\""
		+ " checked=\"%s\""
		+ " width=\"%.3f\" height=\"%.3f\""
		+ " offsetX=\"%.3f\" offsetY=\"%.3f\" offsetZ=\"%.3f\""
		+ " />\n"
	) % [ind, label.xml_escape(), checked, width, height, offset_x, offset_y, offset_z]
