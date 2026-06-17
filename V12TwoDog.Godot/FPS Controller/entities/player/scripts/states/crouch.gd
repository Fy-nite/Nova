extends State

var _p: Player
var _wants_to_uncrouch: bool = false

func enter(msg: Dictionary = {}) -> void:
	_p = actor as Player
	_wants_to_uncrouch = false
	
	var skip_crouch_animation: bool = msg.get("skip_crouch_animation", false)
	
	if not skip_crouch_animation:
		_p.camera_animator.play("crouch")

func exit() -> void:
	_p.camera_animator.play("uncrouch")

func unhandle_input(event: InputEvent) -> void:
	if event.is_action_pressed("crouch"):
		_wants_to_uncrouch = true
	
	if event.is_action_pressed("jump") and not _p.is_crouch_blocked():
		_wants_to_uncrouch = true

func physics_update(delta: float) -> void:
	if not _p:
		return
	
	_p.apply_friction(delta)
	_p.handle_movement(_p.crouch_speed, _p.acceleration, delta)
	_p.move_and_slide()
	_check_transitions()

func _check_transitions() -> void:
	if not _p.is_on_floor():
		state_machine.transition_to("Air")
		return
	
	if _wants_to_uncrouch:
		if _p.camera_animator.is_playing() and _p.camera_animator.current_animation == "crouch":
			return
	
		if not _p.is_crouch_blocked():
			if _p.move_direction == Vector3.ZERO:
				state_machine.transition_to("Idle")
			elif _p.should_enter_run():
				state_machine.transition_to("Run")
			else:
				state_machine.transition_to("Walk")
