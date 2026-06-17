extends State

var _p: Player

func enter(_msg: Dictionary = {}) -> void:
	_p = actor as Player

func physics_update(delta: float) -> void:
	if not _p:
		return
	
	_p.apply_friction(delta)
	_p.move_and_slide()
	_check_transitions()

func _check_transitions() -> void:
	if not _p.is_on_floor():
		state_machine.transition_to("Air")
	elif _p.is_jump_requested():
		state_machine.transition_to("Air", {"jump": true})
	elif Input.is_action_just_pressed("crouch") and _p.can_crouch:
		state_machine.transition_to("Crouch")
	elif _p.move_direction != Vector3.ZERO:
		if _p.should_enter_run():
			state_machine.transition_to("Run")
		else:
			state_machine.transition_to("Walk")
