extends CanvasLayer

@export var interaction_label: Label

func _on_interaction_ray_target_changed(new_target: Variant) -> void:
	if new_target != null:
		interaction_label.text = new_target.interaction_text
		interaction_label.show()
	else:
		interaction_label.hide()
