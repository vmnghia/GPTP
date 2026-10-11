//The pixel walk of Bitmap's lines (Bresenham), in a form that can start at any
//step, so a long line costs only the part of it that is on screen.
//
//Pure: no game memory. tests/line_walk_test.cpp builds it on its own.
#pragma once
#include "../types.h"

namespace linewalk {

//A walk from (x1, y1) to (x2, y2): step 0 is the start, step stepCount() the
//end, and every step moves one pixel along the longer axis.
struct Walk {
	int x1, y1;
	int sx, sy;	//+1 or -1
	int dx, dy;	//the distances, 0 or more
};
Walk walkOf(int x1, int y1, int x2, int y2);
int stepCount(const Walk& walk);
//The pixel at step k (0 to stepCount()) and the walk's error term there.
void pixelAt(const Walk& walk, int k, int* x, int* y, int* err);
//Moves one step on.
void stepOn(const Walk& walk, int* x, int* y, int* err);
//The steps whose pixels are in the rect (its edges included): first to last.
//False if there are none.
bool stepsInside(const Walk& walk, int left, int top, int right, int bottom, int* first, int* last);

//How far an arrowhead's strokes reach from the pixel they start at.
const int ARROW_LENGTH = 3;
//A line of dashes (dash pixels drawn, gap pixels not; none when hideDashes)
//with an arrowhead where each dash starts and ends, drawn into data (width x
//height, one byte a pixel) and clipped to it. Both ends always get a pixel.
//phase shifts the pattern along the line: the caller moves it to animate.
void drawChevronLine(u8* data, int width, int height, int x1, int y1, int x2, int y2, u8 color,
                     int dash, int gap, int phase, bool hideDashes);

} //linewalk
