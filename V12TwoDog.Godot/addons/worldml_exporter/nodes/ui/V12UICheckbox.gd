## V12UICheckbox  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <CheckboxComponent>.
## Maps to a Godot CheckBox control in the 2D UI tree.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UICheckbox
extends Node


@export var label: String = ""
@export var checked: bool = false


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<CheckboxComponent label=\"%s\" checked=\"%s\" />\n"
	) % [ind, label.xml_escape(), checked]
