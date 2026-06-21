@tool
extends EditorPlugin

var _dock: VBoxContainer

func _enter_tree():
	_dock = VBoxContainer.new()
	var btn = Button.new()
	btn.text = "Export Edited Scene to WorldML (user://exported_world.xml)"
	btn.pressed.connect(_on_export_pressed)
	_dock.add_child(btn)

	add_control_to_bottom_panel(_dock, "WorldML Export")

func _exit_tree():
	remove_control_from_bottom_panel(_dock)
	_dock.free()

func _on_export_pressed():
	var editor = get_editor_interface()
	var root = editor.get_edited_scene_root()
	if root == null:
		_editor_dialog("No edited scene root. Open a scene to export.")
		return

	var xml = _build_world_xml(root)
	var path = "user://exported_world.xml"
	var file = FileAccess.open(path, FileAccess.ModeFlags.WRITE)
	if file == null:
		_editor_dialog("Failed to open file for writing: %s" % path)
		return
	file.store_string(xml)
	file.close()
	_editor_dialog("Exported to: %s" % path)

func _build_world_xml(root: Node) -> String:
	var s = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
	s += "<World name=\"%s\">\n" % root.name
	for child in root.get_children():
		s += _append_element_for_node(child, 1)
	s += "</World>\n"
	return s

func _append_element_for_node(node: Node, indent: int) -> String:
	var out = ""
	var ind = ""
	for i in range(indent):
		ind += "\t"

	# Collision body nodes are not exported as standalone Elements;
	# their CollisionShape3D children are harvested below when processing the parent.
	var cls = node.get_class()
	var skip_classes = ["StaticBody3D", "RigidBody3D", "AnimatableBody3D",
						"CharacterBody3D", "Area3D", "CollisionShape3D", "CollisionPolygon3D"]
	if cls in skip_classes:
		# Still recurse so nested visual nodes aren't lost
		for c in node.get_children():
			out += _append_element_for_node(c, indent)
		return out

	if node is Node3D:
		var n3d = node
		out += "%s<Element name=\"%s\">\n" % [ind, node.name]
		var gp = n3d.global_position
		var rot = n3d.rotation_degrees
		# Emit legacy single-axis rotation (Y) for compatibility, and full
		# Euler rotation components so importers can reconstruct orientation.
		out += "%s\t<TransformComponent x=\"%.3f\" y=\"%.3f\" z=\"%.3f\" rotation=\"%.3f\" rotationX=\"%.3f\" rotationY=\"%.3f\" rotationZ=\"%.3f\" />\n" \
			% [ind, gp.x, gp.y, gp.z, rot.y, rot.x, rot.y, rot.z]

		# ScaleComponent if non-uniform / non-unit
		var sc = n3d.scale
		if not (is_equal_approx(sc.x, 1.0) and is_equal_approx(sc.y, 1.0) and is_equal_approx(sc.z, 1.0)):
			out += "%s\t<ScaleComponent scaleX=\"%.3f\" scaleY=\"%.3f\" scaleZ=\"%.3f\" />\n" \
				% [ind, sc.x, sc.y, sc.z]

		# ── V12 custom-node protocol ─────────────────────────────────────────
		# If this node itself defines _v12_component_xml (e.g. V12SpawnPoint,
		# V12Player) emit its component XML directly into this element.
		if node.has_method("_v12_component_xml"):
			out += node._v12_component_xml(ind)

		# Scan direct children for data-attachment custom nodes (extend Node, not
		# Node3D — e.g. V12Tag, V12Health).  They get merged into THIS element
		# and will be skipped during the child-element recursion below.
		for c in node.get_children():
			if c.has_method("_v12_component_xml") and not (c is Node3D):
				out += c._v12_component_xml(ind)

		# Legacy group-based SpawnPoint fallback (skipped when a V12SpawnPoint
		# custom node is used, since that already emitted the component above).
		if node.is_in_group("SpawnPoint") and not node.has_method("_v12_component_xml"):
			out += "%s\t<SpawnPointComponent />\n" % ind

		# ── Mesh ─────────────────────────────────────────────────────────────
		# CSG primitives (emit named MeshComponent + MeshRenderer wrapper)
		if cls == "CSGBox3D":
			var s = node.size
			out += _mesh_components(ind, "csg_mesh", "Box", s.x, s.y, s.z)
			out += _csg_collider(node, ind, "Box", s.x, s.y, s.z)
		elif cls == "CSGSphere3D":
			var r = node.radius
			out += _mesh_components(ind, "csg_mesh", "Sphere", r * 2.0, r * 2.0, r * 2.0)
			out += _csg_collider(node, ind, "Sphere", r * 2.0, r * 2.0, r * 2.0)
		elif cls == "CSGCylinder3D":
			var cr = node.radius
			var ch = node.height
			out += _mesh_components(ind, "csg_mesh", "Cylinder", cr * 2.0, ch, cr * 2.0)
			out += _csg_collider(node, ind, "Cylinder", cr * 2.0, ch, cr * 2.0)
		elif cls == "CSGCapsule3D":
			var r2 = node.radius
			var h2 = node.height
			out += _mesh_components(ind, "csg_mesh", "Capsule", r2 * 2.0, h2, r2 * 2.0)
			out += _csg_collider(node, ind, "Capsule", r2 * 2.0, h2, r2 * 2.0)
		elif cls == "CSGPlane3D":
			var psz = node.size
			out += "%s\t<ColliderComponent Shape=\"Plane\" Width=\"%.3f\" Height=\"%.3f\" Depth=\"%.3f\" />\n" \
				% [ind, psz.x, 0.0, psz.y]
			out += _csg_collider(node, ind, "Plane", psz.x, 0.0, psz.y)

		# MeshInstance3D -> MeshComponent + optional MaterialComponent
		if node is MeshInstance3D:
			out += _mesh_instance_components(node, ind)

		# ── Collision ─────────────────────────────────────────────────────────
		# Harvest CollisionShape3D from immediate children (physics body children)
		out += _harvest_collider(node, ind)
		
		# RigidBody3D parent -> RigidBodyComponent
		if node.get_parent() != null:
			var p = node.get_parent()
			var pcls = p.get_class()
			if pcls == "RigidBody3D":
				var is_kin = p.get("freeze") == true or p.get("freeze_mode") == 1
				out += "%s\t<RigidBodyComponent mass=\"%.3f\" gravityScale=\"%.3f\" isKinematic=\"%s\" />\n" \
					% [ind, p.mass, p.gravity_scale, is_kin]

		# ── Lights ────────────────────────────────────────────────────────────
		if node is OmniLight3D:
			var l = node
			var lc = l.light_color
			out += "%s\t<GenericLightComponent Type=\"Point\" colorR=\"%.3f\" colorG=\"%.3f\" colorB=\"%.3f\" range=\"%.3f\" energy=\"%.3f\" shadowEnabled=\"%s\" />\n" \
				% [ind, lc.r, lc.g, lc.b, l.omni_range, l.light_energy, l.shadow_enabled]

		if node is SpotLight3D:
			var l = node
			var lc = l.light_color
			out += "%s\t<GenericLightComponent Type=\"Spot\" colorR=\"%.3f\" colorG=\"%.3f\" colorB=\"%.3f\" range=\"%.3f\" energy=\"%.3f\" angle=\"%.2f\" spotSoftness=\"%.3f\" shadowEnabled=\"%s\" />\n" \
				% [ind, lc.r, lc.g, lc.b, l.spot_range, l.light_energy, l.spot_angle, l.spot_angle_attenuation, l.shadow_enabled]

		if node is DirectionalLight3D:
			var l = node
			var lc = l.light_color
			out += "%s\t<GenericLightComponent colorR=\"%.3f\" colorG=\"%.3f\" colorB=\"%.3f\" energy=\"%.3f\" shadowEnabled=\"%s\" />\n" \
				% [ind, lc.r, lc.g, lc.b, l.light_energy, l.shadow_enabled]

		# ── Camera ────────────────────────────────────────────────────────────
		if node is Camera3D:
			var cam = node
			out += "%s\t<CameraComponent fov=\"%.1f\" isCurrent=\"%s\" />\n" \
				% [ind, cam.fov, cam.current]
		
		# ── Particles ─────────────────────────────────────────────────────────
		if node is GPUParticles3D:
			var p = node as GPUParticles3D
			var dir_x := 0.0; var dir_y := 1.0; var dir_z := 0.0
			var spread := 45.0; var speed_min := 1.0; var speed_max := 3.0
			var emit_radius := 0.0
			var pm = p.process_material
			if pm is ParticleProcessMaterial:
				var d: Vector3 = pm.direction.normalized()
				dir_x = d.x; dir_y = d.y; dir_z = d.z
				spread = pm.spread
				speed_min = pm.initial_velocity_min
				speed_max = pm.initial_velocity_max
				if pm.emission_shape == ParticleProcessMaterial.EMISSION_SHAPE_SPHERE:
					emit_radius = pm.emission_sphere_radius
			out += "%s\t<ParticleEmitterComponent amount=\"%d\" lifetime=\"%.3f\" emissionRadius=\"%.4f\" speedMin=\"%.3f\" speedMax=\"%.3f\" dirX=\"%.4f\" dirY=\"%.4f\" dirZ=\"%.4f\" spreadAngle=\"%.2f\" emitting=\"%s\" oneShot=\"%s\" />\n" \
				% [ind, p.amount, p.lifetime, emit_radius,
					speed_min, speed_max, dir_x, dir_y, dir_z,
					spread, p.emitting, p.one_shot]

		# ── Fog Volume ────────────────────────────────────────────────────────
		if cls == "FogVolume":
			var fv = node
			var cr := 1.0; var cg := 1.0; var cb := 1.0; var dens := 0.1
			var fmat = fv.material
			if fmat is FogMaterial:
				cr = fmat.albedo.r; cg = fmat.albedo.g; cb = fmat.albedo.b
				dens = fmat.density
			var fh: float = fv.size.y if "size" in fv else 10.0
			out += "%s\t<FogComponent colorR=\"%.3f\" colorG=\"%.3f\" colorB=\"%.3f\" density=\"%.4f\" fogHeight=\"%.3f\" heightFalloff=\"1.000\" />\n" \
				% [ind, cr, cg, cb, dens, fh]

		# ── Label3D ───────────────────────────────────────────────────────────
		if cls == "Label3D":
			var l3d = node
			var lc: Color = l3d.modulate
			out += "%s\t<UILabelComponent text=\"%s\" fontSize=\"%.1f\" pixelSize=\"%.5f\" colorR=\"%.3f\" colorG=\"%.3f\" colorB=\"%.3f\" colorA=\"%.3f\" billboard=\"%s\" offsetY=\"0.000\" />\n" \
				% [ind, l3d.text.xml_escape(), float(l3d.font_size), l3d.pixel_size,
					lc.r, lc.g, lc.b, lc.a, _billboard_name(l3d.billboard)]

		# ── Audio ─────────────────────────────────────────────────────────────
		if node is AudioStreamPlayer3D:
			var au = node
			var rpath = ""
			if au.stream != null:
				rpath = au.stream.resource_path
			var lin_vol = pow(10.0, au.volume_db / 20.0)
			out += "%s\t<AudioSourceComponent AudioClipPath=\"%s\" Volume=\"%.2f\" Pitch=\"%.3f\" Loop=\"true\" Autoplay=\"%s\" MaxDistance=\"%.2f\" />\n" \
					% [ind, rpath, lin_vol, au.pitch_scale, au.autoplay, au.max_distance]

		# Recurse children (skip collision-only nodes — already harvested above,
		# and skip data-attachment V12 custom nodes — already merged above).
		for c in node.get_children():
			var ccls = c.get_class()
			if ccls in skip_classes:
				continue
			# Data-attachment nodes (extend Node, not Node3D) were already merged
			# into this element's component list above.
			if c.has_method("_v12_component_xml") and not (c is Node3D):
				continue
			out += _append_element_for_node(c, indent + 1)

		out += "%s</Element>\n" % ind
	elif node is Control:
		out += "%s<Element name=\"%s\">\n" % [ind, node.name]
		out += _control_component_xml(node, ind)
		# Merge any V12* data-attachment child nodes
		for c in node.get_children():
			if c.has_method("_v12_component_xml") and not (c is Control) and not (c is Node3D):
				out += c._v12_component_xml(ind)
		# Recurse into child elements
		for c in node.get_children():
			if c.has_method("_v12_component_xml") and not (c is Control) and not (c is Node3D):
				continue
			out += _append_element_for_node(c, indent + 1)
		out += "%s</Element>\n" % ind

	elif node.get_class() == "CanvasLayer":
		out += "%s<Element name=\"%s\">\n" % [ind, node.name]
		out += "%s\t<CanvasComponent />\n" % ind
		for c in node.get_children():
			if c.has_method("_v12_component_xml") and not (c is Node3D):
				out += c._v12_component_xml(ind)
		for c in node.get_children():
			if c.has_method("_v12_component_xml") and not (c is Node3D):
				continue
			out += _append_element_for_node(c, indent + 1)
		out += "%s</Element>\n" % ind

	elif node.get_class() == "WorldEnvironment":
		# Emit an environment element from the WorldEnvironment's Environment resource
		out += "%s<Element name=\"%s\">\n" % [ind, node.name]
		var env = node.environment
		if env != null:
			var sky_col := Color(0.5, 0.6, 0.8)
			var amb_col := Color(0.2, 0.2, 0.25)
			var amb_energy := 1.0
			var skybox_path := ""
			var mode_str := "SolidColor"
			if env.background_mode == Environment.BG_SKY:
				mode_str = "Skybox"
				if env.sky != null and env.sky.sky_material != null:
					skybox_path = env.sky.sky_material.resource_path
			elif env.background_mode == Environment.BG_COLOR:
				sky_col = env.background_color
			amb_col = env.ambient_light_color
			amb_energy = env.ambient_light_energy
			out += "%s\t<EnvironmentComponent mode=\"%s\" skyR=\"%.3f\" skyG=\"%.3f\" skyB=\"%.3f\" ambientR=\"%.3f\" ambientG=\"%.3f\" ambientB=\"%.3f\" ambientEnergy=\"%.3f\" skyboxPath=\"%s\" />\n" \
				% [ind, mode_str, sky_col.r, sky_col.g, sky_col.b,
					amb_col.r, amb_col.g, amb_col.b, amb_energy, skybox_path]
		# Merge V12* attachments
		for c in node.get_children():
			if c.has_method("_v12_component_xml") and not (c is Node3D):
				out += c._v12_component_xml(ind)
		out += "%s</Element>\n" % ind

	else:
		for c in node.get_children():
			out += _append_element_for_node(c, indent)

	return out

## Emit a ColliderComponent when a CSG node has use_collision = true.
## Uses a direct property read — safer than .get() for C++ Godot properties.
func _csg_collider(node: Node, ind: String, shape: String,
					w: float, h: float, d: float) -> String:
	var use_col = false
	if "use_collision" in node:
		use_col = node.use_collision
	if use_col:
		var is_trig = false
		if "collision_layer" in node:
			is_trig = (node.collision_layer == 0 and node.collision_mask == 0)
		return "%s\t<ColliderComponent Shape=\"%s\" Width=\"%.3f\" Height=\"%.3f\" Depth=\"%.3f\" isTrigger=\"%s\" />\n" \
			% [ind, shape, w, h, d, is_trig]
	return ""

## Walk immediate children of `node` looking for a physics body whose first
## CollisionShape3D we can convert to a ColliderComponent.
func _harvest_collider(node: Node, ind: String) -> String:
	var body_classes = ["StaticBody3D", "RigidBody3D", "AnimatableBody3D",
						"CharacterBody3D", "Area3D"]
	# Check children for physics body nodes
	for child in node.get_children():
		if child.get_class() in body_classes:
			var result = _collider_xml_from_body(child, ind)
			if result != "":
				return result
	# Also check parent — typical Godot scenes have the visual node as child
	# of a physics body (e.g. StaticBody3D > MeshInstance3D + CollisionShape3D).
	var parent = node.get_parent()
	if parent != null and parent.get_class() in body_classes:
		return _collider_xml_from_body(parent, ind)
	return ""

## Generate ColliderComponent XML from a physics body node (StaticBody3D, etc.)
## by scanning its CollisionShape3D children.
func _collider_xml_from_body(body: Node, ind: String) -> String:
	var is_trigger = body.get_class() == "Area3D"
	for shape_node in body.get_children():
		if not (shape_node is CollisionShape3D):
			continue
		var sh = shape_node.shape
		if sh == null:
			continue
		var shape_cls = sh.get_class()
		var w = 1.0
		var h = 1.0
		var d = 1.0
		var shape_name = "Box"
		if shape_cls == "BoxShape3D":
			var sz = sh.size
			w = sz.x; h = sz.y; d = sz.z
			shape_name = "Box"
		elif shape_cls == "SphereShape3D":
			w = sh.radius * 2.0; h = w; d = w
			shape_name = "Sphere"
		elif shape_cls == "CapsuleShape3D":
			w = sh.radius * 2.0; h = sh.height; d = w
			shape_name = "Capsule"
		elif shape_cls == "CylinderShape3D":
			w = sh.radius * 2.0; h = sh.height; d = w
			shape_name = "Cylinder"
		return "%s\t<ColliderComponent Shape=\"%s\" Width=\"%.3f\" Height=\"%.3f\" Depth=\"%.3f\" isTrigger=\"%s\" />\n" \
			% [ind, shape_name, w, h, d, is_trigger]
	return ""

## Emit named MeshComponent + MeshRenderer wrapper (matches Procedurals.cs pattern).
func _mesh_components(ind: String, mesh_name: String, shape: String, w: float, h: float, d: float) -> String:
	return (
		'%s\t<Component type="MeshComponent" name="%s" Shape="%s" Width="%.3f" Height="%.3f" Depth="%.3f" />\n'
		+ '%s\t<Component type="MeshRenderer" Mesh="%s" />\n'
	) % [ind, mesh_name, shape, w, h, d, ind, mesh_name]


## Extract MeshComponent + MeshRenderer + MaterialComponent from a MeshInstance3D.
func _mesh_instance_components(node: MeshInstance3D, ind: String) -> String:
	var out = ""
	var mesh = node.mesh
	if mesh == null:
		return out

	var mcls = mesh.get_class()
	var shape_name = ""
	var w = 1.0; var h = 1.0; var d = 1.0
	if mcls == "BoxMesh":
		var sz = mesh.size
		shape_name = "Box"; w = sz.x; h = sz.y; d = sz.z
	elif mcls == "SphereMesh":
		shape_name = "Sphere"; w = mesh.radius * 2.0; h = mesh.height; d = mesh.radius * 2.0
	elif mcls == "CapsuleMesh":
		shape_name = "Capsule"; w = mesh.radius * 2.0; h = mesh.height; d = mesh.radius * 2.0
	elif mcls == "CylinderMesh":
		shape_name = "Cylinder"; w = mesh.top_radius * 2.0; h = mesh.height; d = mesh.top_radius * 2.0
	elif mcls == "PlaneMesh":
		var sz2 = mesh.size
		shape_name = "Plane"; w = sz2.x; h = 0.0; d = sz2.y

	if shape_name != "":
		out += _mesh_components(ind, "mesh_data", shape_name, w, h, d)

	# MaterialComponent from surface 0 override or mesh material
	var mat = node.get_surface_override_material(0)
	if mat == null and mesh != null:
		mat = mesh.surface_get_material(0) if mesh.get_surface_count() > 0 else null
	if mat is StandardMaterial3D:
		var c = mat.albedo_color
		out += "%s\t<MaterialComponent R=\"%.3f\" G=\"%.3f\" B=\"%.3f\" A=\"%.3f\" Metallic=\"%.3f\" Roughness=\"%.3f\" />\n" \
			% [ind, c.r, c.g, c.b, c.a, mat.metallic, mat.roughness]
	return out

func _editor_dialog(text: String) -> void:
	var d = AcceptDialog.new()
	add_child(d)
	d.dialog_text = text
	d.popup_centered(Vector2(400, 120))


## Maps a Godot billboard enum value to the V12 UIBillboardMode string.
func _billboard_name(billboard_mode: int) -> String:
	match billboard_mode:
		1: return "Enabled"
		2: return "YAxis"
		_: return "Disabled"


## Emit V12 component XML for a native Godot 2D Control node.
func _control_component_xml(node: Control, ind: String) -> String:
	var out := ""
	var ccls := node.get_class()

	if ccls == "Button" or ccls == "LinkButton":
		out += "%s\t<ButtonComponent label=\"%s\" />\n" \
			% [ind, node.text.xml_escape()]

	elif ccls == "CheckBox":
		out += "%s\t<CheckboxComponent label=\"%s\" checked=\"%s\" />\n" \
			% [ind, node.text.xml_escape(), node.button_pressed]

	elif ccls == "CheckButton":
		out += "%s\t<ToggleComponent label=\"%s\" isOn=\"%s\" />\n" \
			% [ind, node.text.xml_escape(), node.button_pressed]

	elif ccls == "HSlider" or ccls == "VSlider":
		var range_node = node as Range
		var span: float = maxf(range_node.max_value - range_node.min_value, 0.0001)
		var norm: float = (range_node.value - range_node.min_value) / span
		out += "%s\t<SliderComponent value=\"%.4f\" min=\"%.4f\" max=\"%.4f\" step=\"%.4f\" />\n" \
			% [ind, norm, range_node.min_value, range_node.max_value, range_node.step]

	elif ccls == "SpinBox":
		var sb = node as SpinBox
		out += "%s\t<SliderComponent value=\"%.4f\" min=\"%.4f\" max=\"%.4f\" step=\"%.4f\" />\n" \
			% [ind, sb.value, sb.min_value, sb.max_value, sb.step]

	elif ccls == "LineEdit":
		var le = node as LineEdit
		out += "%s\t<TextInputComponent value=\"%s\" placeholder=\"%s\" />\n" \
			% [ind, le.text.xml_escape(), le.placeholder_text.xml_escape()]

	elif ccls == "TextEdit":
		out += "%s\t<TextInputComponent value=\"%s\" placeholder=\"\" />\n" \
			% [ind, node.text.xml_escape()]

	elif ccls == "ProgressBar":
		var pb = node as ProgressBar
		var span2: float = maxf(pb.max_value - pb.min_value, 0.0001)
		var norm2: float = (pb.value - pb.min_value) / span2
		out += "%s\t<ProgressBarComponent value=\"%.4f\" indeterminate=\"false\" />\n" \
			% [ind, norm2]

	elif ccls == "Label" or ccls == "RichTextLabel":
		out += "%s\t<LabelComponent text=\"%s\" fontSize=\"0.000\" />\n" \
			% [ind, node.text.xml_escape()]

	elif ccls == "VBoxContainer":
		var sep := float(node.get_theme_constant("separation"))
		out += "%s\t<VLayoutComponent spacing=\"%.3f\" padding=\"0.000\" />\n" % [ind, sep]

	elif ccls == "HBoxContainer":
		var sep2 := float(node.get_theme_constant("separation"))
		out += "%s\t<HLayoutComponent spacing=\"%.3f\" padding=\"0.000\" />\n" % [ind, sep2]

	elif ccls == "TextureRect":
		var tr = node as TextureRect
		var src: String = ""
		if tr.texture != null:
			src = tr.texture.resource_path
		out += "%s\t<ImageComponent source=\"%s\" preserveAspect=\"true\" tint=\"\" />\n" \
			% [ind, src]

	elif ccls == "Panel" or ccls == "ColorRect":
		out += "%s\t<RectComponent width=\"%.3f\" height=\"%.3f\" backgroundColor=\"\" cornerRadius=\"0.000\" />\n" \
			% [ind, node.size.x, node.size.y]

	elif ccls == "TextureButton":
		out += "%s\t<ButtonComponent label=\"\" />\n" % ind

	return out
