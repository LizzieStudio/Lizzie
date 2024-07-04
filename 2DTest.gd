extends Node2D
@onready var file_dialog = $FileDialog
@onready var sprite = $Sprite2D

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
	sprite.texture = texture
