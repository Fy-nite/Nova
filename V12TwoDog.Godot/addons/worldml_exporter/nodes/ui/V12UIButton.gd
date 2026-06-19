## V12UIButton  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <ButtonComponent>.
## Maps to a Godot Button control in the 2D UI tree.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UIButton
extends Node


@export var label: String = "Button"


func _v12_component_xml(ind: String) -> String:
	return "%s\t<ButtonComponent label=\"%s\" />\n" % [ind, label.xml_escape()]
