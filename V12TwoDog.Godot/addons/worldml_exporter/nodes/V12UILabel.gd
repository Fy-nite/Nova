## V12UILabel  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a world-space <UILabelComponent>.
## Maps to a Label3D child in the Godot scene.
## Alternatively, the exporter auto-detects Label3D nodes directly.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12UILabel
extends Node


@export var text: String = ""

@export_group("Appearance")
## Font size in pixels. Controls sharpness of rendered text.
@export var font_size: float = 48.0
## World-space size of one pixel. World height = font_size × pixel_size.
@export var pixel_size: float = 0.005

@export_group("Color")
@export var color_r: float = 1.0
@export var color_g: float = 1.0
@export var color_b: float = 1.0
@export var color_a: float = 1.0

@export_group("Billboard")
enum BillboardMode { DISABLED = 0, ENABLED = 1, Y_AXIS = 2 }
@export var billboard: BillboardMode = BillboardMode.Y_AXIS
## Vertical world-unit offset above the element's origin.
@export var offset_y: float = 1.5


func _v12_component_xml(ind: String) -> String:
	const BILLBOARD_NAMES = {
		BillboardMode.DISABLED: "Disabled",
		BillboardMode.ENABLED:  "Enabled",
		BillboardMode.Y_AXIS:   "YAxis",
	}
	var bb_str: String = BILLBOARD_NAMES.get(billboard, "YAxis")
	return (
		"%s\t<UILabelComponent"
		+ " text=\"%s\""
		+ " fontSize=\"%.1f\""
		+ " pixelSize=\"%.5f\""
		+ " colorR=\"%.3f\" colorG=\"%.3f\" colorB=\"%.3f\" colorA=\"%.3f\""
		+ " billboard=\"%s\""
		+ " offsetY=\"%.3f\""
		+ " />\n"
	) % [ind, text.xml_escape(), font_size, pixel_size,
		color_r, color_g, color_b, color_a,
		bb_str, offset_y]
