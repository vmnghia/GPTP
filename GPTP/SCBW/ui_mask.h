//The mask that keeps ReShade's effects off the UI
//(docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md §4.1). Pure: the
//host test tests/ui_mask_test.bat builds it on its own.
#pragma once
#include "../types.h"
#include <vector>

namespace uimask {

const u8 EFFECTS = 0;	//the game view: effects apply
const u8 UI = 255;		//the frame from before the effects is put back

struct Mask {
	u32 width = 0;
	u32 height = 0;
	std::vector<u8> pixels;	//width * height, row 0 at the top
};

//The mask at that size, all EFFECTS.
void reset(Mask& mask, u32 width, u32 height);

//Marks the console as UI. image is the console image at the mask's size, one
//byte per pixel, 0 = transparent. The game view is the transparent area
//connected (left, right, up, down) to the top row; everything else is UI, a
//transparent hole enclosed by the console included. Only ever sets pixels, so
//further regions can be added before or after. A null image or a zero-sized
//mask changes nothing.
void addConsoleImage(Mask& mask, const u8* image);

} //uimask
