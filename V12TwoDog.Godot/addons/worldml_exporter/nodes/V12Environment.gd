## V12Environment  (data-attachment node)
## Add this as a CHILD of any Node3D scene element (or a WorldEnvironment node)
## to attach an <EnvironmentComponent> during WorldML export.
## Because it extends Node (not Node3D), it is merged into the parent element
## rather than becoming its own <Element> in the exported XML.
@tool
class_name V12Environment
extends Node


enum BackgroundMode { SOLID_COLOR = 0, SKYBOX = 1 }

@export var mode: BackgroundMode = BackgroundMode.SOLID_COLOR

@export_group("Sky Color")
@export var sky_r: float = 0.5
@export var sky_g: float = 0.6
@export var sky_b: float = 0.8

@export_group("Ambient Light")
@export var ambient_r: float = 0.2
@export var ambient_g: float = 0.2
@export var ambient_b: float = 0.25
@export var ambient_energy: float = 1.0

@export_group("Skybox")
## Optional res:// path to a panorama / cubemap skybox resource.
@export var skybox_path: String = ""


func _v12_component_xml(ind: String) -> String:
	const MODE_NAMES = {
		BackgroundMode.SOLID_COLOR: "SolidColor",
		BackgroundMode.SKYBOX:      "Skybox",
	}
	var mode_str: String = MODE_NAMES.get(mode, "SolidColor")
	return (
		"%s\t<EnvironmentComponent"
		+ " mode=\"%s\""
		+ " skyR=\"%.3f\" skyG=\"%.3f\" skyB=\"%.3f\""
		+ " ambientR=\"%.3f\" ambientG=\"%.3f\" ambientB=\"%.3f\""
		+ " ambientEnergy=\"%.3f\""
		+ " skyboxPath=\"%s\""
		+ " />\n"
	) % [ind, mode_str, sky_r, sky_g, sky_b,
		ambient_r, ambient_g, ambient_b, ambient_energy,
		skybox_path]
