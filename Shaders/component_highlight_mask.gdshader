// Draws a highlighted component into the highlight mask:
// its code in red, and its view depth split across green and blue for precision.
// The code is its outline color's palette slot, or one past the palette for none, plus 64 when it's painted.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, fog_disabled;

uniform float code;

void fragment() {
	float depth = -VERTEX.z;
	ALBEDO = vec3(code, floor(depth), fract(depth));
}
