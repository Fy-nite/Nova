## V12PhysicsBody  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a <PhysicsBodyComponent>.
## Marks the element as a physics-simulated body. Use V12RigidBody (child node)
## for rigid-body-specific settings (mass, gravity, etc.).
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12PhysicsBody
extends Node


## If true the body is moved by code, not forces.
@export var is_kinematic: bool = false


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<PhysicsBodyComponent"
		+ " isKinematic=\"%s\""
		+ " />\n"
	) % [ind, is_kinematic]
