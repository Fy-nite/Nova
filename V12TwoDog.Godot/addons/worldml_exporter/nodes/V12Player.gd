## V12Player
## Drop this node into your scene to define a player spawn point with full
## VR/Desktop settings.  The WorldML exporter emits a <PlayerComponent /> for it.
## The name of this Node3D becomes the WorldML Element name (e.g. "Player").
@tool
class_name V12Player
extends Node3D


## Which input mode the UniversalPlayerController should prefer at startup.
## Auto  – use XR if a headset is detected, otherwise fall back to Desktop.
## Desktop – always keyboard + mouse, even when a headset is connected.
## XR  – require XR; logs a warning and falls back to Desktop if unavailable.
enum InputMode { AUTO = 0, DESKTOP = 1, XR = 2 }

@export var preferred_input_method: InputMode = InputMode.AUTO

## ── Shared movement ──────────────────────────────────────────────────────────
@export var move_speed: float = 4.0        ## Base walk speed in m/s
@export var sprint_multiplier: float = 1.9 ## Multiplier applied while sprinting
@export var jump_strength: float = 6.0     ## Vertical impulse on jump

## ── Desktop ──────────────────────────────────────────────────────────────────
@export var look_sensitivity: float = 1.2  ## Mouse-delta multiplier
@export var can_jump: bool = true

## ── VR ───────────────────────────────────────────────────────────────────────
@export var enable_hand_tracking: bool = true
@export var vr_move_speed: float = 3.0           ## Thumbstick locomotion speed
@export var vr_smooth_locomotion: bool = true    ## false = teleport only


func _v12_component_xml(ind: String) -> String:
	const METHOD_NAMES = {
		InputMode.AUTO:    "Auto",
		InputMode.DESKTOP: "Desktop",
		InputMode.XR:      "XR",
	}
	var method_str: String = METHOD_NAMES.get(preferred_input_method, "Auto")
	return (
		"%s\t<PlayerComponent"
		+ " preferredInputMethod=\"%s\""
		+ " moveSpeed=\"%.3f\""
		+ " sprintMultiplier=\"%.3f\""
		+ " jumpStrength=\"%.3f\""
		+ " lookSensitivity=\"%.3f\""
		+ " canJump=\"%s\""
		+ " enableHandTracking=\"%s\""
		+ " vrMoveSpeed=\"%.3f\""
		+ " vrSmoothLocomotion=\"%s\""
		+ " />\n"
	) % [
		ind,
		method_str,
		move_speed,
		sprint_multiplier,
		jump_strength,
		look_sensitivity,
		can_jump,
		enable_hand_tracking,
		vr_move_speed,
		vr_smooth_locomotion,
	]
