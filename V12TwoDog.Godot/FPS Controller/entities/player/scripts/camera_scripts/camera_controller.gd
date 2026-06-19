class_name CameraController
extends Node3D

const SWAY_MAX_ANGLE: float = 0.15
const SWAY_INTENSITY: float = 0.01
const SWAY_SMOOTH: float = 10.0

@export_group("Dependancies")
@export var camera_pitch: Node3D
@export var camera_effects: CameraEffects
@export var camera: Camera3D
@export var hand_node: Node3D

@export_group("Camera Data")
@export var mouse_sensitivity: float = 0.15
@export var pitch_min: float = deg_to_rad(-89.0)
@export var pitch_max: float = deg_to_rad(89.0)
@export var gamepad_sensitivity := 2.0
var mouse_input: bool = false
var rotation_input: float = 0.0
var pitch_input: float = 0.0
var mouse_rotation: Vector3 = Vector3.ZERO
var player_rotation: Vector3 = Vector3.ZERO
var sway_target: Vector3 = Vector3.ZERO
var sway_current: Vector3 = Vector3.ZERO
var can_update: bool = true

func _ready() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion and Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
		var mm := event as InputEventMouseMotion

		rotation_input += -mm.relative.x * mouse_sensitivity
		pitch_input += -mm.relative.y * mouse_sensitivity

		var roll  = clamp(mm.relative.x * SWAY_INTENSITY,
			-SWAY_MAX_ANGLE, SWAY_MAX_ANGLE)
		var pitch = clamp(mm.relative.y * SWAY_INTENSITY,
			-SWAY_MAX_ANGLE, SWAY_MAX_ANGLE)

		sway_target.x = pitch
		sway_target.z = roll
		
func _input(event: InputEvent) -> void:
	if event is InputEventKey and Input.is_action_just_pressed("escape"):
		get_tree().quit()

func _process(delta: float) -> void:
	if hand_node:
		sway_current = sway_current.lerp(sway_target, SWAY_SMOOTH * delta)
		hand_node.rotation = sway_current
		sway_target = sway_target.lerp(Vector3.ZERO, SWAY_SMOOTH * delta)

func _physics_process(delta: float) -> void:
	rotation_input += Input.get_axis("look_right", "look_left") * gamepad_sensitivity
	pitch_input += Input.get_axis("look_down", "look_up") * gamepad_sensitivity

	if can_update:
		update_camera(delta)
func update_camera(delta_time: float) -> void:
	mouse_rotation.x = clamp(mouse_rotation.x + pitch_input * delta_time, pitch_min, pitch_max)
	mouse_rotation.y += rotation_input * delta_time
	player_rotation = Vector3(0.0, mouse_rotation.y, 0.0)
	
	var pitch_only: Vector3 = Vector3(mouse_rotation.x, 0.0, 0.0)
	
	owner.global_transform.basis = Basis.from_euler(player_rotation)
	
	if camera_pitch:
		camera_pitch.transform.basis = Basis.from_euler(pitch_only)
	
	rotation_input = 0.0
	pitch_input = 0.0

#Note guys that I only added this function because it's handy, call it with any node to get a boolean if the camera can see that node or not.
func is_node_visible(node: Node3D) -> bool:
	var point: Vector3 = node.global_transform.origin
	
	if camera.is_position_behind(point):
		return false
	
	var screen_position: Vector2 = camera.unproject_position(point)
	var viewport_size: Vector2 = get_viewport().get_visible_rect().size
	
	return screen_position.x >= 0.0 and screen_position.x <= viewport_size.x and screen_position.y >= 0.0 and screen_position.y <= viewport_size.y
