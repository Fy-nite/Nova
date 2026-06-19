## V12Fog  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a <FogComponent> during WorldML export.
## Alternatively, the exporter auto-detects FogVolume nodes directly.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12Fog
extends Node


@export_group("Color")
@export var color_r: float = 0.8
@export var color_g: float = 0.8
@export var color_b: float = 0.8

@export_group("Volume")
## Volumetric density (0 = invisible, 1 = fully opaque).
@export var density: float = 0.1
## World-unit height of the fog volume above the element's origin.
@export var fog_height: float = 10.0
## Controls how sharply the fog fades with altitude. Higher = sharper edge.
@export var height_falloff: float = 1.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<FogComponent"
		+ " colorR=\"%.3f\" colorG=\"%.3f\" colorB=\"%.3f\""
		+ " density=\"%.4f\""
		+ " fogHeight=\"%.3f\""
		+ " heightFalloff=\"%.3f\""
		+ " />\n"
	) % [ind, color_r, color_g, color_b, density, fog_height, height_falloff]
