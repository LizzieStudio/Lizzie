shader_type spatial;
render_mode cull_back, diffuse_burley;
// add `unshaded` above if you want it perfectly flat and immune to scene lighting

// --- Table geometry (cm), pushed from Table.cs on every resize ---
uniform vec2 table_size = vec2(100.0, 100.0);

// --- Grid (all spacing in real-world cm, so density is size-independent) ---
uniform float cell_cm     = 5.0;   // minor grid spacing
uniform int   major_every = 5;     // a bold line every N cells
uniform float minor_width = 1.0;   // thickness in screen px (anti-aliased)
uniform float major_width = 1.5;

// --- Border frame, inset from the table edge ---
uniform float border_cm    = 6.0;
uniform float border_width = 2.0;

// --- Colors ---
uniform vec3 paper_color  : source_color = vec3(0.106, 0.161, 0.302); // slate-blue
uniform vec3 line_color   : source_color = vec3(0.243, 0.302, 0.451); // faint, lighter blue
uniform vec3 border_color : source_color = vec3(0.310, 0.376, 0.533); // slightly lighter still

// --- Optional hand-made tiling paper texture (leave strength 0 to skip) ---
uniform sampler2D paper_tex : source_color, hint_default_white,
	filter_linear_mipmap_anisotropic, repeat_enable;
uniform float paper_tile_cm  = 40.0; // real-world size of one paper tile
uniform float paper_strength = 0.0;

// Anti-aliased gridlines: coverage 0..1 where `coord` (in cells) nears an integer.
float grid(vec2 coord, float width) {
	vec2 d = abs(fract(coord - 0.5) - 0.5);   // 0 exactly on a line
	vec2 w = fwidth(coord) * width;           // ~constant screen-space thickness
	vec2 l = vec2(1.0) - smoothstep(vec2(0.0), w, d);
	return max(l.x, l.y);
}

void fragment() {
	vec2 cm = UV * table_size;                 // PlaneMesh UV is 0..1 -> cm

	float minor = grid(cm / cell_cm, minor_width);
	float major = grid(cm / (cell_cm * float(major_every)), major_width);

	// inset frame line: cm distance to nearest table edge, then a line at border_cm
	vec2  edge  = min(cm, table_size - cm);
	float ed    = min(edge.x, edge.y);
	float frame = 1.0 - smoothstep(0.0, fwidth(ed) * border_width, abs(ed - border_cm));

	vec3 base = paper_color;
	if (paper_strength > 0.0) {
		vec3 tex = texture(paper_tex, cm / paper_tile_cm).rgb;
		base = mix(paper_color, paper_color * tex, paper_strength);
	}

	vec3 col = base;
	col = mix(col, line_color,   minor * 0.75);
	col = mix(col, line_color,   major * 0.95); // major over minor = bolder
	col = mix(col, border_color, frame);

	ALBEDO    = col;
	ROUGHNESS = 0.9;
	METALLIC  = 0.0;
}
