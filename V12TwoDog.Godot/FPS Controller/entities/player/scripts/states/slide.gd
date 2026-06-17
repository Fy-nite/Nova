extends State

var _p: Player
var _slide_direction: Vector3 = Vector3.ZERO
var _slide_time_left: float = 0.0
var _enter_crouch_on_exit: bool = false

func enter(_msg: Dictionary = {}) -> void:
	_p = actor as Player
	_slide_time_left = _p.slide_duration
	_enter_crouch_on_exit = false
	
	var horizontal_velocity: Vector3 = _p.get_horizontal_velocity()
	
	if horizontal_velocity.length() > 0.1:
		_slide_direction = horizontal_velocity.normalized()
	elif _p.move_direction != Vector3.ZERO:
		_slide_direction = _p.move_direction.normalized()
	else:
		_slide_direction = -_p.global_transform.basis.z
	
	_p.camera_animator.play("crouch")
	
	if _p.camera_controller.camera_effects:
		_p.camera_controller.camera_effects.set_slide_active(true)

func exit() -> void:
	if _p.camera_controller.camera_effects:
		_p.camera_controller.camera_effects.set_slide_active(false)
	
	if not _enter_crouch_on_exit and not _p.is_crouch_blocked():
		_p.camera_animator.play("uncrouch")

func physics_update(delta: float) -> void:
	if not _p:
		return
	
	_slide_time_left = max(_slide_time_left - delta, 0.0)
	
	if _p.move_direction != Vector3.ZERO:
		_slide_direction = _slide_direction.slerp(_p.move_direction.normalized(), _p.slide_steer_strength * delta).normalized()
	
	_p.apply_slide_friction(delta)
	_p.handle_slide_movement(_slide_direction, delta)
	_p.move_and_slide()
	_check_transitions()

func _check_transitions() -> void:
	if not _p.is_on_floor():
		state_machine.transition_to("Air")
		return
	
	if _slide_time_left > 0.0 and _p.get_horizontal_speed() > _p.slide_min_speed:
		return
	
	_enter_crouch_on_exit = true
	state_machine.transition_to("Crouch", {"skip_crouch_animation": true})
