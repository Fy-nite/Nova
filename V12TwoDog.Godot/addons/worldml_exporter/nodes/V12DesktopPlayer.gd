## V12DesktopPlayer  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a <DesktopPlayerComponent>.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12DesktopPlayer
extends Node


@export var is_local_controlled: bool = true

@export_group("Movement")
## Base movement speed in world units per second.
@export var move_speed: float = 4.0
## Scalar applied to move_speed while sprinting.
@export var sprint_multiplier: float = 1.9
## Jump impulse strength applied when jumping.
@export var jump_strength: float = 6.0
@export var can_jump: bool = true

@export_group("Look")
## Mouse look sensitivity multiplier.
@export var look_sensitivity: float = 1.2


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<DesktopPlayerComponent"
		+ " isLocalControlled=\"%s\""
		+ " moveSpeed=\"%.3f\""
		+ " sprintMultiplier=\"%.3f\""
		+ " jumpStrength=\"%.3f\""
		+ " canJump=\"%s\""
		+ " lookSensitivity=\"%.3f\""
		+ " />\n"
	) % [ind, is_local_controlled, move_speed, sprint_multiplier,
		jump_strength, can_jump, look_sensitivity]
