class_name Interactable
extends Area3D

@warning_ignore("unused_signal")
signal interacted()

@export var interaction_text : String

func _ready() -> void:
	collision_layer = 4
	collision_mask = 0
