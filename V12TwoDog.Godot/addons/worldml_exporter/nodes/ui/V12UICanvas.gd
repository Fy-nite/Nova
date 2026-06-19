## V12UICanvas  (data-attachment node)
## Add this as a CHILD of a Node3D or CanvasLayer to attach a <CanvasComponent>.
## Maps to a Godot CanvasLayer node.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UICanvas
extends Node


func _v12_component_xml(ind: String) -> String:
	return "%s\t<CanvasComponent />\n" % ind
