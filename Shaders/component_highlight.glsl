#[compute]
#version 450

// Draws ComponentHighlight's highlights over the table, after its transparent objects:
// an outline around outlined components, opaque stripes over painted ones, and see-through stripes over hatched ones.
//
// ComponentHighlight first draws the highlighted components into a mask, from the table camera's view.
// Each mask pixel says which highlights the component drawn there has, and its depth: how far it is in front of the camera.
// Comparing that depth with the table's depth buffer tells whether something on the table hides the component there.
// Where something does, its highlights fade instead of disappearing, so they show through.
//
// Each workgroup draws one square tile of pixels. Outlines are drawn on pixels near their components,
// so the workgroup starts by copying the mask under its tile, plus a margin around it, into shared memory.

// The side of each tile, in pixels.
#define TILE 16
// The margin around each tile, in pixels: how far the outline reaches along a row or column, from OUTLINE_END.
#define REACH 4
// The side of a tile plus its margins.
#define SPAN (TILE + 2 * REACH)

// A mask pixel's code: its outline color's slot in the palette, or NO_OUTLINE for none,
// plus PAINTED when it's painted and HATCHED when it's hatched.
// ComponentHighlight builds the same codes, and ComponentHighlightEffect.PaletteSize matches PALETTE_SIZE.
#define PALETTE_SIZE 32
#define NO_OUTLINE PALETTE_SIZE
#define PAINTED 64
#define HATCHED 128
// The code of a pixel where no highlighted component was drawn.
#define EMPTY -1

const vec3 BLACK = vec3(0.0);

// The outline, by distance in pixels from the nearest pixel of its component:
// a dark rim out to INNER_RIM_END, the outline's color out to COLOR_END, and another dark rim out to OUTLINE_END,
// whose last pixel fades out for a softer edge.
const float INNER_RIM_END = 1.5;
const float COLOR_END = 2.8;
const float OUTLINE_END = 4.3;
// The rims darken whatever's under them this much, which keeps any color readable on any surface,
// like a white outline around a white card.
const float RIM_OPACITY = 0.75;

// Paint and hatching are diagonal stripes, in repeats of one stripe of their color, one of black, and the rest gaps.
// component_paint.gdshader draws the same stripes on hand cards, so hand_card.tscn's materials
// and ComponentHighlight's PaintColor and HatchColor match these.
const int STRIPE_WIDTH = 4;
// Paint: opaque red and black with no gaps, so painted components are easy to find.
const vec3 PAINT_COLOR = vec3(1.0, 0.0, 0.0);
const int PAINT_PERIOD = 2;
// Hatching: half-opaque white and black over two fifths of the component, so it still shows.
const vec3 HATCH_COLOR = vec3(1.0);
const float HATCH_OPACITY = 0.5;
const int HATCH_PERIOD = 5;

// How opaque highlights are where something on the table hides them.
const float HIDDEN_OPACITY = 0.4;

layout(local_size_x = TILE, local_size_y = TILE, local_size_z = 1) in;

// The highlighted components, as ComponentHighlight draws them:
// their code in red, their depth split into whole and fractional parts across green and blue,
// since a half float can't hold it all precisely, and alpha 1 where they were drawn and 0 elsewhere.
layout(set = 0, binding = 0) uniform sampler2D mask;
layout(set = 0, binding = 1) uniform sampler2D depth_buffer;
// The table as drawn so far, which this pass draws over.
layout(rgba16f, set = 0, binding = 2) uniform restrict image2D color_image;
// The outline colors, by palette slot.
layout(set = 0, binding = 3, std140) uniform Palette {
	vec4 colors[PALETTE_SIZE];
} palette;

layout(push_constant, std430) uniform Params {
	// The table camera's projection, reduced to what turns its depth buffer into depths:
	// clip z is x * view z + y, and clip w is z * view z + w.
	vec4 depth_terms;
	// The size of the view, in pixels.
	ivec2 size;
	// How far the stripes have slid, in pixels, so they move over time.
	int shift;
} params;

// The mask under the tile and its margins.
shared int codes[SPAN][SPAN];
shared float depths[SPAN][SPAN];
// How many of those pixels have a highlighted component.
shared uint highlighted;

bool on_screen(ivec2 p) {
	return all(greaterThanEqual(p, ivec2(0))) && all(lessThan(p, params.size));
}

// A code's outline palette slot, or NO_OUTLINE, without its PAINTED and HATCHED flags.
int outline_slot(int code) {
	return code & ~(PAINTED | HATCHED);
}

bool has_outline(int code) {
	return code != EMPTY && outline_slot(code) != NO_OUTLINE;
}

// The depth of whatever the table drew at a pixel, read from its depth buffer.
float scene_depth(ivec2 p) {
	float d = texelFetch(depth_buffer, p, 0).r;
	vec4 t = params.depth_terms;
	// The depth buffer holds clip z / clip w, so this solves that for view z,
	// negated since the camera looks down -z.
	return (t.y - d * t.w) / (t.x - d * t.z);
}

// Whether a highlighted component at a depth shows at a pixel, with nothing on the table in front of it.
// The table drew the component too, so where it shows, the two depths match up to rounding.
bool shows(float depth, ivec2 p) {
	float scene = scene_depth(p);
	return depth <= scene + 0.01 + scene * 0.0001;
}

// Draws a pixel's part of diagonal stripes over under, at opacity.
vec3 stripes(vec3 under, ivec2 p, vec3 color, int period, float opacity) {
	int stripe = (p.x + p.y + params.shift) / STRIPE_WIDTH % period;
	if (stripe == 0) {
		return mix(under, color, opacity);
	}
	if (stripe == 1) {
		return mix(under, BLACK, opacity);
	}
	return under;
}

void main() {
	ivec2 local = ivec2(gl_LocalInvocationID.xy);
	ivec2 tile_corner = ivec2(gl_WorkGroupID.xy) * TILE;

	// The tile's threads copy the mask under the tile and its margins together, each taking every TILE * TILE-th pixel.
	int thread = local.y * TILE + local.x;
	if (thread == 0) {
		highlighted = 0u;
	}
	barrier();
	for (int i = thread; i < SPAN * SPAN; i += TILE * TILE) {
		ivec2 at = ivec2(i % SPAN, i / SPAN);
		ivec2 p = tile_corner - REACH + at;
		int code = EMPTY;
		float depth = 0.0;
		if (on_screen(p)) {
			vec4 m = texelFetch(mask, p, 0);
			if (m.a >= 0.5) {
				code = int(round(m.r));
				depth = m.g + m.b;
				atomicAdd(highlighted, 1u);
			}
		}
		codes[at.y][at.x] = code;
		depths[at.y][at.x] = depth;
	}
	barrier();

	// Most tiles have nothing highlighted near them.
	if (highlighted == 0u) {
		return;
	}
	ivec2 p = tile_corner + local;
	if (!on_screen(p)) {
		return;
	}
	vec4 table = imageLoad(color_image, p);
	vec3 result = table.rgb;

	// This pixel's place in codes and depths.
	ivec2 here = local + REACH;
	int code = codes[here.y][here.x];

	// Paint and hatching, over the component itself.
	if (code != EMPTY && (code & (PAINTED | HATCHED)) != 0) {
		float opacity = shows(depths[here.y][here.x], p) ? 1.0 : HIDDEN_OPACITY;
		if ((code & PAINTED) != 0) {
			result = stripes(result, p, PAINT_COLOR, PAINT_PERIOD, opacity);
		}
		if ((code & HATCHED) != 0) {
			result = stripes(result, p, HATCH_COLOR, HATCH_PERIOD, HATCH_OPACITY * opacity);
		}
	}

	// The outline goes around its component, never over it,
	// so it's drawn on pixels without one, from the nearest outlined pixel within reach.
	// On a tie, the first found wins: the topmost, then the leftmost.
	if (!has_outline(code)) {
		float nearest = OUTLINE_END + 1.0;
		ivec2 nearest_offset = ivec2(0);
		for (int y = -REACH; y <= REACH; y++) {
			for (int x = -REACH; x <= REACH; x++) {
				float d = length(vec2(x, y));
				if (d < nearest && has_outline(codes[here.y + y][here.x + x])) {
					nearest = d;
					nearest_offset = ivec2(x, y);
				}
			}
		}

		if (nearest <= OUTLINE_END) {
			ivec2 source = here + nearest_offset;
			bool on_rim = nearest <= INNER_RIM_END || nearest > COLOR_END;
			vec3 color = on_rim ? BLACK : palette.colors[outline_slot(codes[source.y][source.x])].rgb;
			float opacity = (on_rim ? RIM_OPACITY : 1.0) * clamp(OUTLINE_END - nearest, 0.0, 1.0);
			// Fades where the outlined pixel it comes from is hidden.
			if (!shows(depths[source.y][source.x], p + nearest_offset)) {
				opacity *= HIDDEN_OPACITY;
			}
			result = mix(result, color, opacity);
		}
	}

	if (result != table.rgb) {
		imageStore(color_image, p, vec4(result, table.a));
	}
}
