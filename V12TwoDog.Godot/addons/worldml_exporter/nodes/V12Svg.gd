## V12Svg  (data-attachment node)
## Add this as a CHILD of any Node3D to attach an <SvgComponent> during WorldML export.
## Renders an SVG vector graphic rasterised to a sprite at runtime.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12Svg
extends Node


## Path to an .svg file. The file content is inlined into the exported XML.
@export_file("*.svg") var svg_file: String = ""

## Inline SVG markup (takes precedence over svg_file when non-empty).
@export_multiline var svg_content: String = ""

@export_group("Size")
@export var width: float = 1.0
@export var height: float = 1.0

@export_group("Tint")
@export_range(0.0, 1.0) var tint_r: float = 1.0
@export_range(0.0, 1.0) var tint_g: float = 1.0
@export_range(0.0, 1.0) var tint_b: float = 1.0
@export_range(0.0, 1.0) var tint_a: float = 1.0


func _v12_component_xml(ind: String) -> String:
	var content = svg_content
	if content == "" and svg_file != "":
		var f = FileAccess.open(svg_file, FileAccess.ModeFlags.READ)
		if f != null:
			content = f.get_as_text()
			f.close()

	var out = "%s\t<Component type=\"SvgComponent\"" % ind
	out += " width=\"%.3f\" height=\"%.3f\"" % [width, height]
	out += " tintR=\"%.3f\" tintG=\"%.3f\" tintB=\"%.3f\" tintA=\"%.3f\"" % [tint_r, tint_g, tint_b, tint_a]

	if content == "":
		out += " />\n"
	else:
		out += ">\n"
		for line in content.split("\n"):
			out += "%s\t\t%s\n" % [ind, line]
		out += "%s\t</Component>\n" % ind

	return out
