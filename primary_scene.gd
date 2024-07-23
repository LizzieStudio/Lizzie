extends Node3D

@onready var file_dialog = $FileDialog
@onready var sprite_3d = $Sprite3D

# Called when the node enters the scene tree for the first time.
func _ready():
	pass # Replace with function body.


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta):
	pass
	
func _on_file_dialog_file_selected(path):
	print(path)
	var img = Image.load_from_file(path)
	var texture = ImageTexture.create_from_image(img)
	sprite_3d.texture = texture
	sprite_3d.hframes = 10
	sprite_3d.vframes = 7
	sprite_3d.frame_coords = Vector2i(2,2)
	file_dialog.visible = false



