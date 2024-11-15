shader_type spatial;
render_mode cull_front;
render_mode unshaded;

uniform sampler2D SCREEN_TEXTURE : hint_screen_texture;
uniform vec4 outline_color : source_color;

void fragment() {
	vec4 pixelatedtext = texture(SCREEN_TEXTURE, SCREEN_UV);
	ALBEDO = outline_color.rgb;
}