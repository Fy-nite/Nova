## V12SliderControl  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a world-space <SliderControlComponent>.
## Maps to an HSlider inside a SubViewport + QuadMesh in the Godot scene.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12SliderControl
extends Node


@export var label: String = ""

@export_group("Value")
@export var min_value: float = 0.0
@export var max_value: float = 1.0
@export var value: float = 0.0
## Snap increment. 0 = continuous.
@export var step: float = 0.0

@export_group("Size & Offset")
@export var width: float = 0.6
@export var height: float = 0.13
@export var offset_x: float = 0.0
@export var offset_y: float = 0.0
@export var offset_z: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<SliderControlComponent"
		+ " label=\"%s\""
		+ " minValue=\"%.4f\" maxValue=\"%.4f\" value=\"%.4f\" step=\"%.4f\""
		+ " width=\"%.3f\" height=\"%.3f\""
		+ " offsetX=\"%.3f\" offsetY=\"%.3f\" offsetZ=\"%.3f\""
		+ " />\n"
	) % [ind, label.xml_escape(), min_value, max_value, value, step,
		width, height, offset_x, offset_y, offset_z]
