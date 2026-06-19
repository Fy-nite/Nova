## V12ProgressBarControl  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a world-space <ProgressBarComponent>.
## Maps to a ProgressBar inside a SubViewport + QuadMesh in the Godot scene.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12ProgressBarControl
extends Node


@export var label: String = ""
## Normalised fill amount in [0, 1].
@export_range(0.0, 1.0) var value: float = 0.0
@export var show_percentage: bool = true

@export_group("Size & Offset")
@export var width: float = 0.5
@export var height: float = 0.10
@export var offset_x: float = 0.0
@export var offset_y: float = 0.0
@export var offset_z: float = 0.0

@export_group("Fill Color")
@export var fill_r: float = 0.20
@export var fill_g: float = 0.80
@export var fill_b: float = 0.30
@export var fill_a: float = 1.0

@export_group("Background Color")
@export var bg_r: float = 0.15
@export var bg_g: float = 0.15
@export var bg_b: float = 0.15
@export var bg_a: float = 1.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<ProgressBarComponent"
		+ " label=\"%s\""
		+ " value=\"%.4f\""
		+ " showPercentage=\"%s\""
		+ " width=\"%.3f\" height=\"%.3f\""
		+ " offsetX=\"%.3f\" offsetY=\"%.3f\" offsetZ=\"%.3f\""
		+ " fillR=\"%.3f\" fillG=\"%.3f\" fillB=\"%.3f\" fillA=\"%.3f\""
		+ " bgR=\"%.3f\" bgG=\"%.3f\" bgB=\"%.3f\" bgA=\"%.3f\""
		+ " />\n"
	) % [ind, label.xml_escape(), value, show_percentage,
		width, height, offset_x, offset_y, offset_z,
		fill_r, fill_g, fill_b, fill_a,
		bg_r, bg_g, bg_b, bg_a]
