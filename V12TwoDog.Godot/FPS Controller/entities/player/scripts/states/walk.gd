extends State

var _p: Player

func enter(_msg: Dictionary = {}) -> void:
	_p = actor as Player

func physics_update(delta: float) -> void:
	if not _p:
		return
	
	_p.apply_friction(delta)
	_p.handle_movement(_p.walk_speed, _p.acceleration, delta)
	_p.move_and_slide()
	_check_transitions()

func _check_transitions() -> void:
	if not _p.is_on_floor():
		state_machine.transition_to("Air")
		return
	
	if _p.is_jump_requested():
		state_machine.transition_to("Air", {"jump": true})
		return
	
	if Input.is_action_just_pressed("crouch") and _p.can_crouch:
		state_machine.transition_to("Crouch")
		return
	
	if _p.move_direction == Vector3.ZERO:
		state_machine.transition_to("Idle")
		return
	
	if _p.should_enter_run():
		state_machine.transition_to("Run")
