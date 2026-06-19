## V12Interaction  (data-attachment node)
## Add this as a CHILD of any Node3D to attach an <InteractionComponent>.
## Marks the element as a raycast/laser interaction target.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12Interaction
extends Node


## Set true to have this element start as pointed-at (debug / initial state).
@export var is_pointing: bool = false
## Set true to have this element start in a selected state.
@export var is_selecting: bool = false


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<InteractionComponent"
		+ " isPointing=\"%s\""
		+ " isSelecting=\"%s\""
		+ " />\n"
	) % [ind, is_pointing, is_selecting]
