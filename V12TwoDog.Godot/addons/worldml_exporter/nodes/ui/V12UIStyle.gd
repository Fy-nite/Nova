## V12UIStyle  (data-attachment node)
## Add this as a CHILD of any Control or Node3D element to attach a <UIStyleComponent>.
## Carries style hints and layout attributes for UI elements.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12UIStyle
extends Node


## Hint for the renderer: "muted", "accent", "title", "danger", "selected", etc.
@export var style_hint: String = ""
## Render the element flat (no background / border).
@export var flat: bool = false


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<UIStyleComponent"
		+ " styleHint=\"%s\""
		+ " flat=\"%s\""
		+ " />\n"
	) % [ind, style_hint.xml_escape(), flat]
