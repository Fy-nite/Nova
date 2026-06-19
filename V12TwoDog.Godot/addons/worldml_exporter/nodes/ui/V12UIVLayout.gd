## V12UIVLayout  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <VLayoutComponent>.
## Maps to a Godot VBoxContainer in the 2D UI tree.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UIVLayout
extends Node


## Pixels (or world-units) of spacing between children.
@export var spacing: float = 4.0
## Uniform padding applied inside all four edges.
@export var padding: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<VLayoutComponent spacing=\"%.3f\" padding=\"%.3f\" />\n"
	) % [ind, spacing, padding]
