extends Node3D

func _ready() -> void:
	# Forces the game window to render editor-style debug gizmos
	var viewport_rid = get_viewport().get_viewport_rid()
	#RenderingServer.viewport_set_debug_draw(viewport_rid, RenderingServer.VIEWPORT_DEBUG_DRAW_SDFGI)

# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass
