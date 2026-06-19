## V12SpawnPoint
## Place this node in your scene to mark the default player spawn location.
## The WorldML exporter will emit a <SpawnPointComponent /> inside this element.
## This node must have a unique name so V12 can reference it as an Element.
@tool
class_name V12SpawnPoint
extends Node3D


func _v12_component_xml(ind: String) -> String:
	return "%s\t<SpawnPointComponent />\n" % ind
