## V12UIPanel  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a world-space <UIPanelComponent>.
## Maps to a SubViewport + MeshInstance3D (QuadMesh) combination in the Godot scene.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12UIPanel
extends Node


@export var title: String = ""
@export var body: String = ""

@export_group("Size")
## Panel width in world units.
@export var width: float = 1.0
## Panel height in world units.
@export var height: float = 0.5
## Vertical world-unit offset above the element's origin.
@export var offset_y: float = 1.0

@export_group("Background Color")
@export var bg_r: float = 0.08
@export var bg_g: float = 0.08
@export var bg_b: float = 0.12
@export var bg_a: float = 0.90

@export_group("Text Color")
@export var text_r: float = 1.0
@export var text_g: float = 1.0
@export var text_b: float = 1.0
@export var text_a: float = 1.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<UIPanelComponent"
		+ " title=\"%s\""
		+ " body=\"%s\""
		+ " width=\"%.3f\" height=\"%.3f\""
		+ " bgR=\"%.3f\" bgG=\"%.3f\" bgB=\"%.3f\" bgA=\"%.3f\""
		+ " textR=\"%.3f\" textG=\"%.3f\" textB=\"%.3f\" textA=\"%.3f\""
		+ " offsetY=\"%.3f\""
		+ " />\n"
	) % [ind, title.xml_escape(), body.xml_escape(),
		width, height,
		bg_r, bg_g, bg_b, bg_a,
		text_r, text_g, text_b, text_a,
		offset_y]
