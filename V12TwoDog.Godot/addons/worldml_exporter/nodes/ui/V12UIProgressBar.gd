## V12UIProgressBar  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach a <ProgressBarComponent>
## (2D UI variant). Maps to a Godot ProgressBar control.
## Because it extends Node, it is merged into the parent element during export.
##
## Note: For the 3D world-space progress bar, use V12ProgressBarControl instead.
@tool
class_name V12UIProgressBar
extends Node


## Normalised fill amount in [0, 1].
@export_range(0.0, 1.0) var value: float = 0.0
@export var indeterminate: bool = false


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<ProgressBarComponent value=\"%.4f\" indeterminate=\"%s\" />\n"
	) % [ind, value, indeterminate]
