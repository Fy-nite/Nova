## V12UIHLayout  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <HLayoutComponent>.
## Maps to a Godot HBoxContainer in the 2D UI tree.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UIHLayout
extends Node


## Pixels (or world-units) of spacing between children.
@export var spacing: float = 4.0
## Uniform padding applied inside all four edges.
@export var padding: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<HLayoutComponent spacing=\"%.3f\" padding=\"%.3f\" />\n"
	) % [ind, spacing, padding]
