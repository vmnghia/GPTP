#include "ui_mask.h"
#include <cstddef>

namespace uimask {

void reset(Mask& mask, u32 width, u32 height) {
	mask.width = width;
	mask.height = height;
	mask.pixels.assign((size_t)width * height, EFFECTS);
}

void addConsoleImage(Mask& mask, const u8* image) {
	const u32 width = mask.width;
	const u32 height = mask.height;
	if (image == NULL || width == 0 || height == 0)
		return;
	const u32 count = width * height;

	//The game view: a flood fill over the transparent pixels from the top row.
	//An explicit stack, since the view of a large screen is millions of pixels.
	std::vector<u8> view(count, 0);
	std::vector<u32> open;	//in the view, neighbours not looked at yet
	for (u32 x = 0; x < width; ++x)
		if (image[x] == 0) {
			view[x] = 1;
			open.push_back(x);
		}
	while (!open.empty()) {
		const u32 at = open.back();
		open.pop_back();
		const u32 x = at % width;
		//A missing neighbour is replaced by the pixel itself, already in the view.
		const u32 neighbours[4] = {
			x > 0 ? at - 1 : at,
			x + 1 < width ? at + 1 : at,
			at >= width ? at - width : at,
			at + width < count ? at + width : at,
		};
		for (u32 i = 0; i < 4; ++i) {
			const u32 next = neighbours[i];
			if (image[next] == 0 && !view[next]) {
				view[next] = 1;
				open.push_back(next);
			}
		}
	}

	for (u32 i = 0; i < count; ++i)
		if (!view[i])
			mask.pixels[i] = UI;
}

void effectsArea(const Mask& mask, std::vector<u8>& effects) {
	effects.resize(mask.pixels.size());
	for (size_t i = 0; i < effects.size(); ++i)
		effects[i] = (u8)(255 - mask.pixels[i]);
}

} //uimask
