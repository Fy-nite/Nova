## V12Inspector  (data-attachment node)
## Add this as a CHILD of any Node3D scene element to attach an <InspectorComponent>
## during WorldML export. This marks the element for custom inspector UI rendering.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12Inspector
extends Node


func _v12_component_xml(ind: String) -> String:
	return "%s\t<InspectorComponent />\n" % ind
