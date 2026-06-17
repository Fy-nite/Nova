class_name CameraEffects
extends Node3D

@export_group("Data")
@export var headbob_enabled: bool = true
@export var landing_kick_enabled: bool = true
@export var jump_kick_enabled: bool = true
@export var idle_sway_enabled: bool = true
@export var strafe_tilt_enabled: bool = true
@export var weapon_kick_enabled: bool = true
@export var camera_shake_enabled: bool = true
@export var slide_fov_enabled: bool = true
@export var lean_enabled: bool = true

@export_group("Headbob Settings")
@export var bob_freq: float = 2.4
@export var bob_amp: float = 0.1
@export var bob_speed_multiplier: float = 1.0

@export_group("Landing Settings")
@export var fall_kick_amount: float = 0.3
@export var strong_fall_kick_amount: float = 0.6
@export var fall_kick_rotation: float = 16.0
@export var strong_fall_kick_rotation: float = 22.5
@export var strong_fall_threshold: float = 12.0

@export_group("Jump Settings")
@export var rise_kick_rotation: float = 16.0

@export_group("Idle Sway Settings")
@export var idle_sway_position_amount: Vector2 = Vector2(0.02, 0.015)
@export var idle_sway_rotation_amount: Vector3 = Vector3(0.35, 0.45, 0.2)
@export var idle_sway_speed: float = 1.2

@export_group("Strafe Tilt Settings")
@export var strafe_tilt_angle: float = 4.0
@export var strafe_tilt_smoothness: float = 12.0

@export_group("Weapon Kick Settings")
@export var weapon_kick_position: float = 0.06
@export var weapon_kick_pitch: float = 2.4
@export var weapon_kick_yaw: float = 1.0
@export var weapon_kick_roll: float = 0.75

@export_group("Camera Shake Settings")
@export var shake_rotation_amount: float = 1.8
@export var shake_position_amount: float = 0.04

@export_group("Slide FOV Settings")
@export var slide_fov_bonus: float = 8.0
@export var slide_fov_speed: float = 8.0

@export_group("Lean Settings")
@export var lean_angle: float = 12.0
@export var lean_offset: float = 0.5
@export var lean_smoothness: float = 12.0

@export_group("Smoothness")
@export var smoothness: float = 12.0

var _bob_time: float = 0.0
var _idle_time: float = 0.0
var _base_position: Vector3 = Vector3.ZERO
var _base_rotation: Vector3 = Vector3.ZERO
var _current_position: Vector3 = Vector3.ZERO
var _current_rotation: Vector3 = Vector3.ZERO
var _headbob_offset: Vector3 = Vector3.ZERO
var _idle_position_offset: Vector3 = Vector3.ZERO
var _idle_rotation_offset: Vector3 = Vector3.ZERO
var _kick_position_offset: Vector3 = Vector3.ZERO
var _kick_rotation_offset: Vector3 = Vector3.ZERO
var _shake_position_offset: Vector3 = Vector3.ZERO
var _shake_rotation_offset: Vector3 = Vector3.ZERO
var _target_pos_y: float = 0.0
var _target_rot_x: float = 0.0
var _target_rot_z: float = 0.0
var _shake_power: float = 0.0
var _shake_time_left: float = 0.0
var _shake_duration: float = 0.0
var _rng: RandomNumberGenerator = RandomNumberGenerator.new()
var _slide_active: bool = false
var _default_fov: float = 75.0
var _lean_target: float = 0.0
var _lean_current: float = 0.0

func _ready() -> void:
	_base_position = position
	_base_rotation = rotation
	_current_position = _base_position
	_current_rotation = _base_rotation
	_rng.randomize()
	
	var player: Player = owner as Player
	
	if player and player.camera_controller and player.camera_controller.camera:
		_default_fov = player.camera_controller.camera.fov

func _process(delta: float) -> void:
	var player: Player = owner as Player
	
	if not player:
		return
	
	_update_headbob(player, delta)
	_update_idle_sway(player, delta)
	_update_strafe_tilt(player, delta)
	_update_lean(player, delta)
	_update_impulses(delta)
	_update_shake(delta)
	_update_transform(delta)
	_update_fov(player, delta)

func play_fall_kick(impact_velocity: float) -> void:
	if not landing_kick_enabled:
		return
	
	var is_strong: bool = abs(impact_velocity) > strong_fall_threshold
	_target_pos_y = -strong_fall_kick_amount if is_strong else -fall_kick_amount
	_target_rot_x = deg_to_rad(-strong_fall_kick_rotation if is_strong else -fall_kick_rotation)

func play_rise_kick() -> void:
	if not jump_kick_enabled:
		return
	
	_target_rot_x = deg_to_rad(rise_kick_rotation)

func shake(power: float, duration: float) -> void:
	if not camera_shake_enabled:
		return
	
	_shake_power = max(power, 0.0)
	_shake_duration = max(duration, 0.0)
	_shake_time_left = _shake_duration

func play_weapon_kick(strength: float) -> void:
	if not weapon_kick_enabled:
		return
	
	var yaw_sign: float = -1.0 if _rng.randf() < 0.5 else 1.0
	var roll_sign: float = -1.0 if _rng.randf() < 0.5 else 1.0
	
	_kick_position_offset += Vector3(0.0, 0.0, weapon_kick_position * strength)
	_kick_rotation_offset.x += deg_to_rad(-weapon_kick_pitch * strength)
	_kick_rotation_offset.y += deg_to_rad(weapon_kick_yaw * strength * yaw_sign)
	_kick_rotation_offset.z += deg_to_rad(weapon_kick_roll * strength * roll_sign)

func set_slide_active(active: bool) -> void:
	_slide_active = active

func set_lean(value: float) -> void:
	_lean_target = clamp(value, -1.0, 1.0)

func _update_headbob(player: Player, delta: float) -> void:
	if not headbob_enabled:
		_headbob_offset = Vector3.ZERO
		return
	
	var horizontal_speed: float = player.get_horizontal_speed()
	
	if player.is_on_floor() and horizontal_speed > 0.1:
		_bob_time += delta * horizontal_speed * bob_speed_multiplier
		_headbob_offset.y = abs(sin(_bob_time * bob_freq)) * bob_amp

func _update_idle_sway(player: Player, delta: float) -> void:
	if not idle_sway_enabled:
		_idle_position_offset = Vector3.ZERO
		_idle_rotation_offset = Vector3.ZERO
		return
	
	var is_idle: bool = player.is_on_floor() and player.get_horizontal_speed() <= 0.1 and abs(player.velocity.y) <= 0.05
	
	if not is_idle:
		_idle_position_offset = _idle_position_offset.lerp(Vector3.ZERO, smoothness * delta)
		_idle_rotation_offset = _idle_rotation_offset.lerp(Vector3.ZERO, smoothness * delta)
		return
	
	_idle_time += delta * idle_sway_speed
	
	var target_position: Vector3 = Vector3(
		sin(_idle_time * 0.9) * idle_sway_position_amount.x,
		sin(_idle_time * 1.3) * idle_sway_position_amount.y,
		0.0
	)
	
	var target_rotation: Vector3 = Vector3(
		deg_to_rad(sin(_idle_time * 1.1) * idle_sway_rotation_amount.x),
		deg_to_rad(cos(_idle_time * 0.8) * idle_sway_rotation_amount.y),
		deg_to_rad(sin(_idle_time * 0.7) * idle_sway_rotation_amount.z)
	)
	
	_idle_position_offset = _idle_position_offset.lerp(target_position, smoothness * delta)
	_idle_rotation_offset = _idle_rotation_offset.lerp(target_rotation, smoothness * delta)

func _update_strafe_tilt(player: Player, delta: float) -> void:
	var target_roll: float = 0.0
	
	if strafe_tilt_enabled:
		var right_axis: Vector3 = player.global_transform.basis.x
		var strafe_amount: float = player.move_direction.dot(right_axis)
		target_roll = deg_to_rad(-strafe_amount * strafe_tilt_angle)
	
	_target_rot_z = lerp(_target_rot_z, target_roll, strafe_tilt_smoothness * delta)

func _update_lean(player: Player, delta: float) -> void:
	var lean_value: float = 0.0
	
	if lean_enabled and player.can_lean:
		lean_value = Input.get_action_strength("lean_left") - Input.get_action_strength("lean_right")
	
	set_lean(lean_value)
	_lean_current = lerp(_lean_current, _lean_target, lean_smoothness * delta)

func _update_impulses(delta: float) -> void:
	_target_pos_y = lerp(_target_pos_y, 0.0, smoothness * delta)
	_target_rot_x = lerp(_target_rot_x, 0.0, smoothness * delta)
	_kick_position_offset = _kick_position_offset.lerp(Vector3.ZERO, smoothness * delta)
	_kick_rotation_offset = _kick_rotation_offset.lerp(Vector3.ZERO, smoothness * delta)

func _update_shake(delta: float) -> void:
	if not camera_shake_enabled:
		_shake_position_offset = Vector3.ZERO
		_shake_rotation_offset = Vector3.ZERO
		_shake_power = 0.0
		_shake_time_left = 0.0
		_shake_duration = 0.0
		return
	
	if _shake_time_left <= 0.0 or _shake_duration <= 0.0 or _shake_power <= 0.0:
		_shake_position_offset = Vector3.ZERO
		_shake_rotation_offset = Vector3.ZERO
		_shake_time_left = 0.0
		return
	
	_shake_time_left = max(_shake_time_left - delta, 0.0)
	
	var fade: float = _shake_time_left / _shake_duration
	
	_shake_position_offset = Vector3(
		_rng.randf_range(-1.0, 1.0) * shake_position_amount * _shake_power * fade,
		_rng.randf_range(-1.0, 1.0) * shake_position_amount * _shake_power * fade,
		0.0
	)
	
	_shake_rotation_offset = Vector3(
		deg_to_rad(_rng.randf_range(-1.0, 1.0) * shake_rotation_amount * _shake_power * fade),
		deg_to_rad(_rng.randf_range(-1.0, 1.0) * shake_rotation_amount * _shake_power * fade),
		deg_to_rad(_rng.randf_range(-1.0, 1.0) * shake_rotation_amount * _shake_power * fade)
	)

func _update_transform(delta: float) -> void:
	var target_position: Vector3 = _base_position
	target_position += _headbob_offset
	target_position += Vector3(0.0, _target_pos_y, 0.0)
	target_position += _idle_position_offset
	target_position += _kick_position_offset
	target_position += Vector3(_lean_current * -lean_offset, 0.0, 0.0)
	
	var target_rotation: Vector3 = _base_rotation
	target_rotation += Vector3(_target_rot_x, 0.0, _target_rot_z)
	target_rotation += _idle_rotation_offset
	target_rotation += _kick_rotation_offset
	target_rotation.z += deg_to_rad(_lean_current * lean_angle)
	
	_current_position = _current_position.lerp(target_position, smoothness * delta)
	_current_rotation = _current_rotation.lerp(target_rotation, smoothness * delta)
	
	position = _current_position + _shake_position_offset
	rotation = _current_rotation + _shake_rotation_offset

func _update_fov(player: Player, delta: float) -> void:
	if not player.camera_controller or not player.camera_controller.camera:
		return
	
	var camera: Camera3D = player.camera_controller.camera
	var target_fov: float = _default_fov
	
	if slide_fov_enabled and _slide_active:
		target_fov += slide_fov_bonus
	
	camera.fov = lerp(camera.fov, target_fov, slide_fov_speed * delta)
