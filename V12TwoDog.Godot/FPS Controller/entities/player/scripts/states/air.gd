extends State

var _p: Player
var _impact_vel: float = 0.0

func enter(msg: Dictionary = {}) -> void:
	_p = actor as Player
	_impact_vel = 0.0
	
	if msg.has("jump") or _p.is_jump_requested():
		if _p.can_perform_jump():
			_p.velocity.y = _p.jump_velocity
			_p.consume_jump_request()
			
			if _p.coyote_timer:
				_p.coyote_timer.stop()
	
			if _p.camera_controller and _p.camera_controller.camera_effects:
				_p.camera_controller.camera_effects.play_rise_kick()

func physics_update(delta: float) -> void:
	if not _p:
		return
	
	if _p.velocity.y < 0.0:
		_impact_vel = _p.velocity.y
	
	_p.apply_gravity(delta)
	_p.handle_movement(_p.walk_speed, _p.acceleration * _p.air_control, delta)
	_p.move_and_slide()
	
	if _p.is_on_floor():
		_handle_landing()

func _handle_landing() -> void:
	if _p.camera_controller and _p.camera_controller.camera_effects:
		_p.camera_controller.camera_effects.play_fall_kick(_impact_vel)
	
	if _p.move_direction == Vector3.ZERO:
		state_machine.transition_to("Idle")
	else:
		if _p.should_enter_run():
			state_machine.transition_to("Run")
		else:
			state_machine.transition_to("Walk")
