// This shader optimizes VcToken's render by allowing Godot to draw them all at once.
// We couldn't use a regular material becuase there's no way to use a different part of the sprite sheet.
// Each token uses a different part of the texture through its instance uniforms.
// It mostly renders the exact same as before.
shader_type spatial;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;

// The frame of the texture each face shows: offset in xy, scale in zw.
instance uniform vec4 front_rect = vec4(0.0, 0.0, 1.0, 1.0);
instance uniform vec4 back_rect = vec4(0.0, 0.0, 1.0, 1.0);

void vertex() {
	// The front faces up and the back faces down.
	vec4 rect = NORMAL.y > 0.0 ? front_rect : back_rect;
	UV = UV * rect.zw + rect.xy;
}

void fragment() {
	vec4 albedo = texture(albedo_texture, UV);
	ALBEDO = albedo.rgb;
	ALPHA = albedo.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 1.0;
}
