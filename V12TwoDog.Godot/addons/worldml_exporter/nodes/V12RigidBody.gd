## V12RigidBody  (data-attachment node)
## Add this as a CHILD of any Node3D to override or declare a <RigidBodyComponent>
## without using a RigidBody3D parent node.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12RigidBody
extends Node


@export var mass: float = 1.0
@export var gravity_scale: float = 1.0
@export var linear_damping: float = 0.0
@export var angular_damping: float = 0.0
## If true the body is moved by code, not forces.
@export var is_kinematic: bool = false
@export var can_sleep: bool = true


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<RigidBodyComponent"
		+ " mass=\"%.4f\""
		+ " gravityScale=\"%.4f\""
		+ " linearDamping=\"%.4f\""
		+ " angularDamping=\"%.4f\""
		+ " isKinematic=\"%s\""
		+ " canSleep=\"%s\""
		+ " />\n"
	) % [ind, mass, gravity_scale, linear_damping, angular_damping,
		is_kinematic, can_sleep]
