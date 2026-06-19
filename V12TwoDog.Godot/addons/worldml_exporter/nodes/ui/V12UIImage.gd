## V12UIImage  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach an <ImageComponent>.
## Maps to a Godot TextureRect in the 2D UI tree.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UIImage
extends Node


## res:// path to the image texture.
@export var source: String = ""
@export var preserve_aspect: bool = true
## Optional CSS-style tint color string (e.g. "#FF8800FF"). Empty = no tint.
@export var tint: String = ""


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<ImageComponent"
		+ " source=\"%s\""
		+ " preserveAspect=\"%s\""
		+ " tint=\"%s\""
		+ " />\n"
	) % [ind, source.xml_escape(), preserve_aspect, tint.xml_escape()]
