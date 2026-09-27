shader_type spatial;
render_mode cull_front;
render_mode unshaded;

instance uniform vec4 outline_color : source_color = vec4(1.0);

void fragment() {
	ALBEDO = outline_color.rgb;
}