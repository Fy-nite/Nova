## V12Tag  (data-attachment node)
## Add this as a CHILD of any Node3D scene element to attach a <TagComponent>
## to that element during WorldML export.
## Because it extends Node (not Node3D), it is merged into the parent element
## rather than becoming its own <Element> in the exported XML.
@tool
class_name V12Tag
extends Node


## Comma-separated tag list, e.g. "Enemy,Damageable,Boss"
@export var tags: String = ""


func _v12_component_xml(ind: String) -> String:
	return "%s\t<TagComponent tags=\"%s\" />\n" % [ind, tags]
