// Draws an outlined component into the selection mask:
// its outline color's index in red, and its view depth split across green and blue for precision.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, fog_disabled;

uniform float index;

void fragment() {
	float depth = -VERTEX.z;
	ALBEDO = vec3(index, floor(depth), fract(depth));
}
