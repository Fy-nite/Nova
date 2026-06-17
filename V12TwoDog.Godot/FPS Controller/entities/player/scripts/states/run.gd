extends State

var _p: Player

func enter(_msg: Dictionary = {}) -> void:
	_p = actor as Player

func physics_update(delta: float) -> void:
	if not _p:
		return
	
	_p.apply_friction(delta)
	_p.handle_movement(_p.run_speed, _p.acceleration, delta)
	_p.move_and_slide()
	_check_transitions()

func _check_transitions() -> void:
	if not _p.is_on_floor():
		state_machine.transition_to("Air")
		return
	
	if _p.is_jump_requested():
		state_machine.transition_to("Air", {"jump": true})
		return
	
	if Input.is_action_just_pressed("crouch"):
		if _p.can_slide:
			state_machine.transition_to("Slide")
		elif _p.can_crouch:
			state_machine.transition_to("Crouch")
		return
	
	if _p.move_direction == Vector3.ZERO or not _p.is_run_input_pressed():
		state_machine.transition_to("Walk")
		return
	
	if not _p.should_enter_run():
		state_machine.transition_to("Walk")
