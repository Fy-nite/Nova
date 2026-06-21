## V12Locomotion  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a <LocomotionComponent>.
## Controls player movement params (speed, jump, gravity, look sensitivity).
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12Locomotion
extends Node


@export_group("Movement")
## Base movement speed in world units per second.
@export var move_speed: float = 2.0
## Scalar applied to move_speed while sprinting.
@export var sprint_multiplier: float = 1.9
## Vertical impulse applied on jump.
@export var jump_strength: float = 3.0
## Gravity magnitude in m/s².
@export var gravity: float = 9.8
@export var can_jump: bool = true

@export_group("Look")
## Mouse look sensitivity multiplier.
@export var look_sensitivity: float = 1.2

@export_group("VR")
## Thumbstick locomotion speed for VR.
@export var vr_move_speed: float = 3.0
## If false, teleport locomotion is used instead.
@export var vr_smooth_locomotion: bool = true
@export var enable_hand_tracking: bool = true


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<LocomotionComponent"
		+ " moveSpeed=\"%.3f\""
		+ " sprintMultiplier=\"%.3f\""
		+ " jumpStrength=\"%.3f\""
		+ " gravity=\"%.3f\""
		+ " canJump=\"%s\""
		+ " lookSensitivity=\"%.3f\""
		+ " vrMoveSpeed=\"%.3f\""
		+ " vrSmoothLocomotion=\"%s\""
		+ " enableHandTracking=\"%s\""
		+ " />\n"
	) % [ind, move_speed, sprint_multiplier, jump_strength, gravity,
		can_jump, look_sensitivity, vr_move_speed,
		vr_smooth_locomotion, enable_hand_tracking]
