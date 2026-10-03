#[compute]
#version 450

// Draws the selection outline in one compute pass, into the table's color after its transparent objects:
// a constant-width outline around the mask's visible pixels.
// Each workgroup draws one tile, loading the mask under it and around it into shared memory once,
// with each row of that also kept as bits, one per pixel.
// For each pixel, the bits give the nearest mask pixel in each row within reach with two instructions,
// and the nearest of those that shows, with nothing in the scene in front of it, sets the outline.
// That's the nearest visible mask pixel overall,
// except where something unoutlined hides a row's nearest and a farther one in the same row shows.

#define TILE 16
// The widest outline in pixels, so a tile's surroundings fit in shared memory.
// SelectionOutlineEffect.MaxWidth matches it.
#define REACH 8
// The tile and its surroundings on each side, which must fit a row's bits in a uint.
#define SPAN (TILE + 2 * REACH)

layout(local_size_x = TILE, local_size_y = TILE, local_size_z = 1) in;

// Outlined components: their color index in red, their view depth split across green and blue, and alpha where they are.
layout(set = 0, binding = 0) uniform sampler2D mask;
layout(set = 0, binding = 1) uniform sampler2D depth_buffer;
layout(rgba16f, set = 0, binding = 2) uniform restrict image2D color_image;
// The outline colors, indexed by the mask, as given: Godot converts them for display.
layout(set = 0, binding = 3, std140) uniform Palette {
	vec4 colors[32];
} palette;

layout(push_constant, std430) uniform Params {
	// The table camera's, as drawn, to read view depths from its depth buffer.
	mat4 inv_projection;
	vec4 rim_color;
	ivec2 size;
	// The outline's width in pixels, rims included, up to REACH.
	float width;
	// The dark rim's width in pixels, on each side of the color.
	float rim;
	// How far behind the scene a mask pixel can be and still count as visible, in node units.
	float bias;
} params;

// The mask around the tile: each pixel's view depth, or 0 where it's empty, and its color index.
shared vec2 around[SPAN][SPAN];
// Each row of the mask around the tile as bits, set where there's a mask pixel.
shared uint occupied[SPAN];

// The scene's view depth at a pixel.
float scene_depth(ivec2 p) {
	float depth = texelFetch(depth_buffer, p, 0).r;
	vec2 ndc = (vec2(p) + 0.5) / vec2(params.size) * 2.0 - 1.0;
	vec4 view = params.inv_projection * vec4(ndc, depth, 1.0);
	return -view.z / view.w;
}

void main() {
	ivec2 local = ivec2(gl_LocalInvocationID.xy);
	int thread = local.y * TILE + local.x;
	ivec2 corner = ivec2(gl_WorkGroupID.xy) * TILE - REACH;

	if (thread < SPAN) {
		occupied[thread] = 0u;
	}
	barrier();

	for (int i = thread; i < SPAN * SPAN; i += TILE * TILE) {
		ivec2 at = ivec2(i % SPAN, i / SPAN);
		ivec2 p = corner + at;
		vec2 value = vec2(0.0);
		if (all(greaterThanEqual(p, ivec2(0))) && all(lessThan(p, params.size))) {
			vec4 m = texelFetch(mask, p, 0);
			if (m.a >= 0.5) {
				value = vec2(m.g + m.b, m.r);
				atomicOr(occupied[at.y], 1u << uint(at.x));
			}
		}
		around[at.y][at.x] = value;
	}
	barrier();

	ivec2 p = corner + REACH + local;
	int x = local.x + REACH;
	// Outlines never cover the components they outline.
	if (any(greaterThanEqual(p, params.size)) || ((occupied[local.y + REACH] >> x) & 1u) != 0u) {
		return;
	}

	int r = min(int(ceil(params.width)), REACH);
	// The bits within reach of the pixel's column, and those at or before it.
	uint reach = ((2u << (2 * r)) - 1u) << (x - r);
	uint before = (2u << x) - 1u;
	float nearest = 1e9;
	int index = 0;
	for (int y = -r; y <= r; y++) {
		uint bits = occupied[local.y + REACH + y] & reach;
		if (bits == 0u) {
			continue;
		}
		// The nearest mask pixel in the row, preferring the left on a tie, like the row pass.
		uint left = bits & before;
		uint right = bits & ~before;
		int toLeft = left != 0u ? x - findMSB(left) : 1000;
		int toRight = right != 0u ? findLSB(right) - x : 1000;
		int dx = toRight < toLeft ? toRight : -toLeft;
		float d = length(vec2(float(dx), float(y)));
		if (d >= nearest || d > params.width + 0.5) {
			continue;
		}
		vec2 source = around[local.y + REACH + y][x + dx];
		float scene = scene_depth(p + ivec2(dx, y));
		if (source.x <= scene + params.bias + scene * 0.0001) {
			nearest = d;
			index = int(round(source.y));
		}
	}
	if (nearest > params.width + 0.5) {
		return;
	}

	// A dark rim on both sides of the color keeps any color readable on any surface,
	// like a white outline around a white card.
	bool on_rim = nearest <= params.rim + 0.5 || nearest > params.width - params.rim;
	vec3 color = on_rim ? params.rim_color.rgb : palette.colors[clamp(index, 0, 31)].rgb;
	// Fades the outer pixel for a softer edge.
	float alpha = clamp(params.width + 0.5 - nearest, 0.0, 1.0) * (on_rim ? params.rim_color.a : 1.0);
	vec4 screen = imageLoad(color_image, p);
	imageStore(color_image, p, vec4(mix(screen.rgb, color, alpha), screen.a));
}
