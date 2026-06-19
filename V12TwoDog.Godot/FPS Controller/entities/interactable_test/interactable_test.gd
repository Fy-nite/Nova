extends Interactable
#
#const orange_material: StandardMaterial3D = preload("res://materials/orange_material.tres")
#const green_material: StandardMaterial3D = preload("res://materials/green_material.tres")
#
#@export var mesh: MeshInstance3D
#
#func _on_interacted() -> void:
	#if mesh.material_override == orange_material:
		#mesh.material_override = green_material
	#else:
		#mesh.material_override = orange_material
