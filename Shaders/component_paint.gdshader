// Paints a control in opaque diagonal stripes of its color and black, in screen pixels,
// like component_highlight.glsl paints components on the table.
shader_type canvas_item;

// Matches component_highlight.glsl's STRIPE.
uniform float stripe = 4.0;
// How fast the stripes move, in pixels per second. HandCard sets it.
uniform float speed;

void fragment() {
	if (mod(floor((FRAGCOORD.x + FRAGCOORD.y + floor(TIME * speed)) / stripe), 2.0) >= 1.0) {
		COLOR = vec4(0.0, 0.0, 0.0, 1.0);
	}
}
