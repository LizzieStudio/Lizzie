// This shader optimizes VcToken's render by allowing Godot to draw them all at once.
// We couldn't use a regular material becuase there's no way to use a different part of the sprite sheet.
// Each token uses a different part of the texture through its instance uniforms.
shader_type spatial;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;

// The texture's column count and row count of frames.
uniform ivec2 grid = ivec2(1, 1);

// The frame each face of a token shows.
// The number counts across the rows first before wrapping to the next column.
instance uniform int front_frame = 0;
instance uniform int back_frame = 0;

void vertex() {
	// The front faces up and the back faces down.
	int frame = NORMAL.y > 0.0 ? front_frame : back_frame;
	vec2 cell = vec2(float(frame % grid.x), float(frame / grid.x));
	UV = (UV + cell) / vec2(grid);
}

void fragment() {
	vec4 albedo = texture(albedo_texture, UV);
	ALBEDO = albedo.rgb;
	ALPHA = albedo.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 1.0;
}
