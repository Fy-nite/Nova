## V12WorldXmlSource  (data-attachment node)
## Add this as a CHILD of any Node3D to mark it as a hot-reload WorldML XML source.
## The V12 runtime will load and watch the specified file, replacing child elements
## whenever it changes on disk.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12WorldXmlSource
extends Node


## Absolute path, or path relative to the executable, of the WorldML XML file.
@export_file("*.xml") var file_path: String = ""
## When true, a FileSystemWatcher reloads the file automatically on every save.
@export var auto_reload: bool = true


func _v12_component_xml(ind: String) -> String:
	return (
		"%s\t<WorldXmlSourceComponent"
		+ " filePath=\"%s\""
		+ " autoReload=\"%s\""
		+ " />\n"
	) % [ind, file_path.xml_escape(), auto_reload]
