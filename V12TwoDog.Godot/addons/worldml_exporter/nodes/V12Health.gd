## V12Health  (data-attachment node)
## Add this as a CHILD of any Node3D scene element to attach a <HealthComponent>
## to that element during WorldML export.
## Because it extends Node (not Node3D), it is merged into the parent element
## rather than becoming its own <Element> in the exported XML.
@tool
class_name V12Health
extends Node


@export var max_health: float = 100.0
@export var is_invincible: bool = false


func _v12_component_xml(ind: String) -> String:
	return "%s\t<HealthComponent maxHealth=\"%.3f\" isInvincible=\"%s\" />\n" \
		% [ind, max_health, is_invincible]
