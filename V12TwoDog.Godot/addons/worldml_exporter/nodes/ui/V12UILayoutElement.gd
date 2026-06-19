## V12UILayoutElement  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <LayoutElementComponent>.
## Provides sizing hints to layout systems (HLayout/VLayout).
## Maps to Godot Control size flags and custom minimum size.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UILayoutElement
extends Node


@export_group("Width Hints (negative = unspecified)")
@export var min_width: float = -1.0
@export var preferred_width: float = -1.0
@export var flexible_width: float = 0.0

@export_group("Height Hints (negative = unspecified)")
@export var min_height: float = -1.0
@export var preferred_height: float = -1.0
@export var flexible_height: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<LayoutElementComponent"
		+ " minWidth=\"%.3f\" preferredWidth=\"%.3f\" flexibleWidth=\"%.3f\""
		+ " minHeight=\"%.3f\" preferredHeight=\"%.3f\" flexibleHeight=\"%.3f\""
		+ " />\n"
	) % [ind, min_width, preferred_width, flexible_width,
		min_height, preferred_height, flexible_height]
