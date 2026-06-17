extends RayCast3D

signal target_changed(new_target)

var target: Interactable : set = _set_target

func _input(event: InputEvent) -> void:
	if event is InputEventKey:
		if Input.is_action_just_pressed("interact") and target:
			target.interacted.emit()

func _physics_process(_delta: float) -> void:
	if is_colliding():
		target = get_collider()
	else:
		target = null

func _set_target(value) -> void:
	target = value
	target_changed.emit(target)
