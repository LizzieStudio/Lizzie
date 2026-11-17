shader_type spatial;
render_mode cull_back, diffuse_burley;

// pushed from TableSurface.cs on every resize
uniform vec2 table_size_cm = vec2(100.0, 100.0);

// The Grid
uniform float grid_spacing_cm = 5;
uniform float line_width_px = 1;

// Colors
uniform vec3 paper_color : source_color = vec3(0.15, 0.15, 0.35); // blue
uniform vec3 line_color : source_color = vec3(0.25, 0.25, 0.45); // lighter blue

float grid(vec2 coord, float width) {
	vec2 d = abs(fract(coord - 0.5) - 0.5);
	vec2 w = fwidth(coord) * width;
	vec2 l = vec2(1.0) - smoothstep(vec2(0.0), w, d);
	return max(l.x, l.y);
}

void fragment() {
	vec2 cell_fit_cm = table_size_cm / round(table_size_cm / grid_spacing_cm);

	// the UV goes from 0 to 1 and gives us our position on the PlaneMesh
	// multiplying by the table_size_cm gives us our position in cm
	vec2 cm = UV * table_size_cm;

	float minor = grid(cm / cell_fit_cm, line_width_px);
	
	ALBEDO    = mix(paper_color, line_color,   minor * 0.75);
	ROUGHNESS = 0.9;
	METALLIC  = 0.0;
}
