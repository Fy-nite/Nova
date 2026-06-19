## V12UISlider  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <SliderComponent>
## (2D UI variant). Maps to a Godot HSlider control.
## Because it extends Node, it is merged into the parent element during export.
##
## Note: For the 3D world-space slider, use V12SliderControl instead.
@tool
class_name V12UISlider
extends Node


@export var value: float = 0.0
@export var min_value: float = 0.0
@export var max_value: float = 1.0
## Snap increment. 0 = continuous.
@export var step: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<SliderComponent"
		+ " value=\"%.4f\""
		+ " min=\"%.4f\" max=\"%.4f\""
		+ " step=\"%.4f\""
		+ " />\n"
	) % [ind, value, min_value, max_value, step]
