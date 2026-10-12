// Draws grid lines on an image to show its frames.
shader_type canvas_item;

uniform int columns = 1;
uniform int rows = 1;
uniform int count = 1;
uniform vec4 line_color : source_color = vec4(1.0, 0.0, 1.0, 0.9);
uniform float line_width = 1.5;
uniform vec4 unused_color : source_color = vec4(0.5, 0.5, 0.5, 0.75);

void fragment() {
	vec2 grid = UV * vec2(float(columns), float(rows));
	vec2 cell = floor(grid);
	if (cell.y * float(columns) + cell.x >= float(count)) {
		COLOR.rgb = mix(COLOR.rgb, unused_color.rgb, unused_color.a);
	}
	// The distance to the nearest frame edge, in screen pixels.
	vec2 edge = abs(grid - round(grid)) / fwidth(grid);
	if (min(edge.x, edge.y) < line_width * 0.5) {
		COLOR = mix(COLOR, vec4(line_color.rgb, 1.0), line_color.a);
	}
}
