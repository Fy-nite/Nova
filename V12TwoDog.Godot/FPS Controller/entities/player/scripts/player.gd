class_name Player
extends CharacterBody3D

const STOP_SPEED: float = 0.1

@export_group("Data")
@export var can_jump: bool = true
@export var can_crouch: bool = true
@export var can_run: bool = true
@export var can_slide: bool = true
@export var can_lean: bool = true
@export var use_jump_buffer: bool = true
@export var use_coyote_time: bool = true

@export_group("Dependencies")
@export var camera_controller: CameraController
@export var crouch_cast: ShapeCast3D
@export var camera_animator: AnimationPlayer
@export var jump_buffer_timer: Timer
@export var coyote_timer: Timer

@export_group("Speed Settings")
@export var walk_speed: float = 5.0
@export var run_speed: float = 8.0
@export var crouch_speed: float = 2.5

@export_group("Physics Forces")
@export var acceleration: float = 8.0
@export var deceleration: float = 12.0
@export var ground_friction: float = 10.0
@export var air_control: float = 0.15

@export_group("Vertical Settings")
@export var jump_velocity: float = 5.5
@export var gravity_multiplier_rising: float = 2.0
@export var gravity_multiplier_falling: float = 3.5

@export_group("Slide Settings")
@export var slide_speed: float = 10.0
@export var slide_duration: float = 0.65
@export var slide_friction: float = 4.0
@export var slide_steer_strength: float = 4.0
@export var slide_min_speed: float = 3.0

var move_direction: Vector3 = Vector3.ZERO

var _current_gravity: float = ProjectSettings.get_setting("physics/3d/default_gravity")
var _was_on_floor: bool = true

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("jump") and can_jump and use_jump_buffer and jump_buffer_timer:
		jump_buffer_timer.start()

func _physics_process(_delta: float) -> void:
	var input_dir: Vector2 = Input.get_vector("strafe_left", "strafe_right", "move_forwards", "move_backwards")
	move_direction = (global_transform.basis * Vector3(input_dir.x, 0.0, input_dir.y)).normalized()
	_handle_timers()
	_was_on_floor = is_on_floor()

func apply_gravity(delta: float) -> void:
	if not is_on_floor():
		var multiplier: float = gravity_multiplier_falling if velocity.y < 0.0 else gravity_multiplier_rising
		velocity.y -= _current_gravity * multiplier * delta

func apply_friction(delta: float) -> void:
	var horizontal_velocity: Vector3 = Vector3(velocity.x, 0.0, velocity.z)
	var speed: float = horizontal_velocity.length()
	
	if speed < STOP_SPEED:
		velocity.x = 0.0
		velocity.z = 0.0
		return
	
	var drop: float = speed * ground_friction * delta
	var new_speed: float = max(speed - drop, 0.0)
	velocity.x *= new_speed / speed
	velocity.z *= new_speed / speed

func apply_slide_friction(delta: float) -> void:
	var horizontal_velocity: Vector3 = Vector3(velocity.x, 0.0, velocity.z)
	var speed: float = horizontal_velocity.length()
	
	if speed < STOP_SPEED:
		velocity.x = 0.0
		velocity.z = 0.0
		return
	
	var drop: float = speed * slide_friction * delta
	var new_speed: float = max(speed - drop, 0.0)
	velocity.x *= new_speed / speed
	velocity.z *= new_speed / speed

func handle_movement(target_speed: float, speed_accel: float, delta: float) -> void:
	var target_velocity: Vector3 = move_direction * target_speed
	var current_horizontal: Vector3 = Vector3(velocity.x, 0.0, velocity.z)
	
	var dot_product: float = current_horizontal.dot(target_velocity)
	var actual_accel: float = speed_accel
	
	if dot_product <= 0.0 and target_velocity != Vector3.ZERO:
		actual_accel = deceleration
	
	var resulting: Vector3 = current_horizontal.lerp(target_velocity, actual_accel * delta)
	velocity.x = resulting.x
	velocity.z = resulting.z

func handle_slide_movement(direction: Vector3, delta: float) -> void:
	var current_horizontal: Vector3 = Vector3(velocity.x, 0.0, velocity.z)
	var target_velocity: Vector3 = direction * slide_speed
	var resulting: Vector3 = current_horizontal.lerp(target_velocity, slide_steer_strength * delta)
	velocity.x = resulting.x
	velocity.z = resulting.z

func can_perform_jump() -> bool:
	if not can_jump:
		return false
	
	if is_on_floor():
		return true
	
	if not use_coyote_time:
		return false
	
	if not coyote_timer:
		return false
	
	return not coyote_timer.is_stopped()

func is_jump_requested() -> bool:
	if not can_jump:
		return false
	
	if use_jump_buffer:
		if not jump_buffer_timer:
			return false
		
		return not jump_buffer_timer.is_stopped()
	
	return Input.is_action_just_pressed("jump")

func consume_jump_request() -> void:
	if jump_buffer_timer:
		jump_buffer_timer.stop()

func is_crouch_blocked() -> bool:
	return crouch_cast.is_colliding()

func get_horizontal_velocity() -> Vector3:
	return Vector3(velocity.x, 0.0, velocity.z)

func get_horizontal_speed() -> float:
	return get_horizontal_velocity().length()

func is_run_input_pressed() -> bool:
	return can_run and Input.is_action_pressed("run")

func should_enter_run() -> bool:
	if not is_run_input_pressed():
		return false
	
	var forward_dot: float = move_direction.dot(-global_transform.basis.z)
	return forward_dot >= -0.01

func _handle_timers() -> void:
	if not use_jump_buffer and jump_buffer_timer:
		jump_buffer_timer.stop()
	
	if not use_coyote_time:
		if coyote_timer:
			coyote_timer.stop()
		
		return
	
	if not coyote_timer:
		return
	
	if _was_on_floor and not is_on_floor() and velocity.y <= 0.0:
		coyote_timer.start()
