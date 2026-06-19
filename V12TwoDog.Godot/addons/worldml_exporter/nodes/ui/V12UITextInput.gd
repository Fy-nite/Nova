## V12UITextInput  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <TextInputComponent>
## (2D UI variant). Maps to a Godot LineEdit control.
## Because it extends Node, it is merged into the parent element during export.
##
## Note: For the 3D world-space text input, use V12TextInputControl instead.
@tool
class_name V12UITextInput
extends Node


@export var value: String = ""
@export var placeholder: String = ""


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<TextInputComponent"
		+ " value=\"%s\""
		+ " placeholder=\"%s\""
		+ " />\n"
	) % [ind, value.xml_escape(), placeholder.xml_escape()]
