## V12DirectionalLight  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a <GenericLightComponent> with
## LightType = Directional during WorldML export.
## For omni / spot lights, use OmniLight3D / SpotLight3D nodes which are
## auto-detected by the exporter.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12DirectionalLight
extends Node


@export_group("Color")
@export_range(0.0, 1.0) var color_r: float = 1.0
@export_range(0.0, 1.0) var color_g: float = 1.0
@export_range(0.0, 1.0) var color_b: float = 1.0
## Brightness multiplier.
@export var energy: float = 1.0
@export var shadow_enabled: bool = true


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<GenericLightComponent"
		+ " colorR=\"%.3f\" colorG=\"%.3f\" colorB=\"%.3f\""
		+ " energy=\"%.3f\""
		+ " shadowEnabled=\"%s\""
		+ " />\n"
	) % [ind, color_r, color_g, color_b, energy, shadow_enabled]
