## V12CustomMesh  (data-attachment node)
## Add this as a CHILD of any Node3D to attach a MeshComponent with inline
## vertex/triangle data during WorldML export.
## When placed under a MeshInstance3D with no manual data entered, it
## automatically extracts the mesh geometry for inline export.
## Because it extends Node (not Node3D), it is merged into the parent element.
@tool
class_name V12CustomMesh
extends Node

## Manual vertex data, one vertex per line:  "x y z"
## Leave empty to auto-extract from a parent MeshInstance3D's mesh.
@export_multiline var vertex_data: String = ""

## Manual triangle index data, one triangle per line:  "a b c"
## Leave empty to auto-extract from a parent MeshInstance3D's mesh.
@export_multiline var index_data: String = ""


func _v12_component_xml(ind: String) -> String:
	var verts: Array[Vector3] = []
	var tris: Array[Vector3i] = []

	if vertex_data.strip_edges().is_empty() or index_data.strip_edges().is_empty():
		var extracted = _extract_from_parent_mesh()
		if extracted.size() >= 2 and not extracted[0].is_empty():
			verts = extracted[0]
			tris = extracted[1]
		else:
			verts = []
			tris = []

	if verts.is_empty():
		verts = _parse_verts(vertex_data)
		tris = _parse_tris(index_data)

	if verts.is_empty():
		return ""

	var out := "%s\t<Component type=\"MeshComponent\" Shape=\"Custom\">\n" % ind
	for v in verts:
		out += "%s\t\t<vert x=\"%s\" y=\"%s\" z=\"%s\" />\n" % [ind, _fmt(v.x), _fmt(v.y), _fmt(v.z)]
	for t in tris:
		out += "%s\t\t<tri a=\"%d\" b=\"%d\" c=\"%d\" />\n" % [ind, t.x, t.y, t.z]
	out += "%s\t</Component>\n" % ind
	return out


## Try to extract vertex/index data from a parent MeshInstance3D's mesh.
func _extract_from_parent_mesh() -> Array:
	var parent = get_parent()
	if parent == null or not (parent is MeshInstance3D):
		return [[], []]
	var mesh = parent.mesh
	if mesh == null:
		return [[], []]

	var verts: PackedVector3Array
	var indices: PackedInt32Array

	# Surface 0 is sufficient for most single-material meshes
	var arrays = mesh.surface_get_arrays(0)
	if arrays == null or arrays.is_empty():
		return [[], []]

	var vert_data = arrays[Mesh.ARRAY_VERTEX]
	if not (vert_data is PackedVector3Array) or vert_data.is_empty():
		return [[], []]
	verts = vert_data

	var idx_data = arrays[Mesh.ARRAY_INDEX]
	if idx_data is PackedInt32Array:
		indices = idx_data
	else:
		indices = PackedInt32Array()

	if indices.is_empty():
		indices = PackedInt32Array()
		for i in verts.size():
			indices.append(i)

	# Convert to the format expected by XML output
	var out_verts: Array[Vector3] = []
	for v in verts:
		out_verts.append(v)

	var out_tris: Array[Vector3i] = []
	var i := 0
	while i + 2 < indices.size():
		out_tris.append(Vector3i(indices[i], indices[i + 1], indices[i + 2]))
		i += 3

	return [out_verts, out_tris]


## Parse multiline "x y z" into an array of Vector3.
func _parse_verts(text: String) -> Array[Vector3]:
	var result: Array[Vector3] = []
	for raw in text.split("\n", false):
		var line = raw.strip_edges()
		if line.is_empty():
			continue
		var parts = line.split(" ", false)
		if parts.size() < 3:
			continue
		var x := float(parts[0])
		var y := float(parts[1])
		var z := float(parts[2])
		result.append(Vector3(x, y, z))
	return result


## Parse multiline "a b c" into an array of Vector3i (each row is a triangle).
func _parse_tris(text: String) -> Array[Vector3i]:
	var result: Array[Vector3i] = []
	for raw in text.split("\n", false):
		var line = raw.strip_edges()
		if line.is_empty():
			continue
		var parts = line.split(" ", false)
		if parts.size() < 3:
			continue
		var a := int(parts[0])
		var b := int(parts[1])
		var c := int(parts[2])
		result.append(Vector3i(a, b, c))
	return result


## Format a float to 6 decimal places without cultural comma issues.
static func _fmt(v: float) -> String:
	return "%.6f" % v
