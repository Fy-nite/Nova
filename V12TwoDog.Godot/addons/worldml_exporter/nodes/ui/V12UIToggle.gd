## V12UIToggle  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <ToggleComponent>.
## Maps to a Godot CheckButton control in the 2D UI tree.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UIToggle
extends Node


@export var label: String = ""
@export var is_on: bool = false


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<ToggleComponent label=\"%s\" isOn=\"%s\" />\n"
	) % [ind, label.xml_escape(), is_on]
