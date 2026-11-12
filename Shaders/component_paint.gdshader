// Paints a control in diagonal stripes of its color and then black, followed by gaps, in screen pixels,
// like component_highlight.glsl paints and hatches components on the table.
shader_type canvas_item;

// Matches component_highlight.glsl's STRIPE_WIDTH.
uniform float stripe = 4.0;
// The stripes in each repeat: one of the color, one of black, and the rest gaps.
// Matches component_highlight.glsl's PAINT_PERIOD or HATCH_PERIOD.
uniform int period = 2;
// How fast the stripes move, in pixels per second. HandCard sets it.
uniform float speed;

void fragment() {
	float band = mod(floor((FRAGCOORD.x + FRAGCOORD.y + floor(TIME * speed)) / stripe), float(period));
	if (band >= 2.0) {
		COLOR.a = 0.0;
	} else if (band >= 1.0) {
		COLOR.rgb = vec3(0.0);
	}
}
