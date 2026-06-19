## V12ParticleEmitter  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a <ParticleEmitterComponent>.
## Alternatively, the exporter auto-detects GPUParticles3D nodes directly.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12ParticleEmitter
extends Node


@export var amount: int = 32
@export var lifetime: float = 2.0
@export var emission_radius: float = 0.0

@export_group("Speed")
@export var speed_min: float = 1.0
@export var speed_max: float = 3.0

@export_group("Direction")
@export var dir_x: float = 0.0
@export var dir_y: float = 1.0
@export var dir_z: float = 0.0
## Random spread angle in degrees around the direction vector.
@export_range(0.0, 180.0) var spread_angle: float = 45.0

@export_group("Playback")
@export var emitting: bool = true
## If true, emits one burst then stops.
@export var one_shot: bool = false


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<ParticleEmitterComponent"
		+ " amount=\"%d\""
		+ " lifetime=\"%.3f\""
		+ " emissionRadius=\"%.4f\""
		+ " speedMin=\"%.3f\" speedMax=\"%.3f\""
		+ " dirX=\"%.4f\" dirY=\"%.4f\" dirZ=\"%.4f\""
		+ " spreadAngle=\"%.2f\""
		+ " emitting=\"%s\""
		+ " oneShot=\"%s\""
		+ " />\n"
	) % [ind, amount, lifetime, emission_radius,
		speed_min, speed_max,
		dir_x, dir_y, dir_z,
		spread_angle, emitting, one_shot]
