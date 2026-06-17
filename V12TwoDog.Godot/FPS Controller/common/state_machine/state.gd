class_name State
extends Node

var state_machine: StateMachine = null
var actor: Node3D = null

func unhandle_input(_event: InputEvent) -> void:
	pass

func handle_input(_event: InputEvent) -> void:
	pass

func update(_delta: float) -> void:
	pass

func physics_update(_delta: float) -> void:
	pass

func enter(_msg: Dictionary = {}) -> void:
	pass

func exit() -> void:
	pass
