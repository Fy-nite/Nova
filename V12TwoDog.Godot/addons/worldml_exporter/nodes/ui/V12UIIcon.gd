## V12UIIcon  (data-attachment node)
## Add this as a CHILD of a Node3D or Control element to attach an <IconComponent>.
## Maps to a Godot TextureRect used as an icon glyph.
## Because it extends Node, it is merged into the parent element during export.
@tool
class_name V12UIIcon
extends Node


## Icon name or res:// path to the icon texture.
@export var icon: String = ""
@export var size: float = 16.0


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<IconComponent icon=\"%s\" size=\"%.3f\" />\n"
	) % [ind, icon.xml_escape(), size]
