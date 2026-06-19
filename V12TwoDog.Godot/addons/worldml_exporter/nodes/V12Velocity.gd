## V12Velocity  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a <VelocityComponent>.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12Velocity
extends Node


@export_group("Linear Velocity")
@export var vel_x: float = 0.0
@export var vel_y: float = 0.0
@export var vel_z: float = 0.0

@export_group("Angular Velocity (deg/s)")
@export var ang_x: float = 0.0
@export var ang_y: float = 0.0
@export var ang_z: float = 0.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<VelocityComponent"
		+ " velX=\"%.4f\" velY=\"%.4f\" velZ=\"%.4f\""
		+ " angX=\"%.4f\" angY=\"%.4f\" angZ=\"%.4f\""
		+ " />\n"
	) % [ind, vel_x, vel_y, vel_z, ang_x, ang_y, ang_z]
