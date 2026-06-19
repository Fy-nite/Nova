## V12Label  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <LabelComponent>.
## Maps to a Godot Label control in the 2D UI tree.
## Because it extends Node, it is merged into the parent element during export.
##
## Note: This is the 2D UI LabelComponent. For a world-space 3D label (Label3D),
## use V12UILabel instead.
@tool
class_name V12Label
extends Node


@export var text: String = ""
## Optional font size hint (0 = use frontend default).
@export var font_size: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<LabelComponent text=\"%s\" fontSize=\"%.3f\" />\n"
	) % [ind, text.xml_escape(), font_size]
