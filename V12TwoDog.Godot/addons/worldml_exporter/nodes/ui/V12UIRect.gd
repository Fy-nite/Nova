## V12UIRect  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <RectComponent>.
## Maps to a Godot Panel or ColorRect in the 2D UI tree.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UIRect
extends Node


@export var width: float = 0.0
@export var height: float = 0.0
## Optional CSS-style background color string (e.g. "#1A1A2EFF"). Empty = transparent.
@export var background_color: String = ""
@export var corner_radius: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<RectComponent"
		+ " width=\"%.3f\" height=\"%.3f\""
		+ " backgroundColor=\"%s\""
		+ " cornerRadius=\"%.3f\""
		+ " />\n"
	) % [ind, width, height, background_color.xml_escape(), corner_radius]
