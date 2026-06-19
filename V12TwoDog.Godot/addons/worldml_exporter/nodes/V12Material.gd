## V12Material  (data-attachment node)
## Add this as a CHILD of any Node3D to override or declare a <MaterialComponent>
## independently of a MeshInstance3D surface material.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12Material
extends Node


@export_group("Albedo Color")
@export var r: float = 1.0
@export var g: float = 1.0
@export var b: float = 1.0
@export var a: float = 1.0

@export_group("PBR")
@export_range(0.0, 1.0) var metallic: float = 0.0
@export_range(0.0, 1.0) var roughness: float = 0.5


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<MaterialComponent"
		+ " R=\"%.3f\" G=\"%.3f\" B=\"%.3f\" A=\"%.3f\""
		+ " Metallic=\"%.3f\""
		+ " Roughness=\"%.3f\""
		+ " />\n"
	) % [ind, r, g, b, a, metallic, roughness]
