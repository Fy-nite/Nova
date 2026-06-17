extends Node


var application_id: int = 123456789012345678

var client := DiscordClient.new()


func _ready() -> void:
	client.set_application_id(application_id)

	var activity := DiscordActivity.new()
	activity.set_type(DiscordActivityTypes.PLAYING)

	activity.set_details("Tutorial")
	activity.set_state("In Group")

	var timestamps := DiscordActivityTimestamps.new()
	var ten_minutes_ago: int = int(Time.get_unix_time_from_system() - 600)
	timestamps.set_start(ten_minutes_ago * 1000)
	activity.set_timestamps(timestamps)

	var assets := DiscordActivityAssets.new()
	assets.set_large_image("surprise")
	assets.set_large_text("Surprise")
	assets.set_small_image("happy-face")
	assets.set_small_text("Happy face")
	assets.set_invite_cover_image("thumbnail")
	activity.set_assets(assets)

	activity.set_details_url("https://github.com/thiagola92/discord-social-sdk/tree/main")
	activity.set_state_url("https://store.godotengine.org/asset/thiagola92/discord-social-sdk/")

	client.update_rich_presence(activity, _on_rich_presence_updated)


func _process(_delta: float) -> void:
	Discord.run_callbacks()


func _on_rich_presence_updated(result: DiscordClientResult) -> void:
	if result.successful():
		print("✅ Rich presence updated!")
