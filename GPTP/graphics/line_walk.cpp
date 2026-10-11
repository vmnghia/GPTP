#include "line_walk.h"
#include <cmath>
#include <cstdlib>

namespace linewalk {

namespace {

const double PI = 3.14159265358979323846264338327950288;

//Steps taken along the shorter axis after k steps of a walk: k * shorter /
//longer, a half rounded up. This is where the stepwise walk gets to.
int shorterSteps(int k, int shorter, int longer) {
	if (longer == 0)
		return 0;
	return (int)((2LL * k * shorter + longer) / (2LL * longer));
}

//Steps taken along x (or y) after k steps of the walk.
int axisSteps(const Walk& walk, bool xAxis, int k) {
	const int along = xAxis ? walk.dx : walk.dy;
	const int other = xAxis ? walk.dy : walk.dx;
	//The longer axis moves on every step (both do when they are equal).
	return along >= other ? k : shorterSteps(k, along, other);
}

//The steps of the walk whose x (or y) is from low to high. A coordinate only
//ever moves one way, so they are one run; false if it is empty.
bool axisRun(const Walk& walk, bool xAxis, int low, int high, int* first, int* last) {
	const int start = xAxis ? walk.x1 : walk.y1;
	const int sign = xAxis ? walk.sx : walk.sy;
	//The coordinate is start + sign * steps: the steps wanted.
	const int least = sign > 0 ? low - start : start - high;
	const int most = sign > 0 ? high - start : start - low;
	const int steps = stepCount(walk);
	if (most < 0 || least > axisSteps(walk, xAxis, steps))
		return false;
	//The first step with at least `least` steps taken along the axis.
	int a = 0, b = steps;
	while (a < b) {
		const int mid = a + (b - a) / 2;
		if (axisSteps(walk, xAxis, mid) >= least)
			b = mid;
		else
			a = mid + 1;
	}
	*first = a;
	//The last step with at most `most`.
	a = 0, b = steps;
	while (a < b) {
		const int mid = a + (b - a + 1) / 2;
		if (axisSteps(walk, xAxis, mid) <= most)
			a = mid;
		else
			b = mid - 1;
	}
	*last = a;
	return *first <= *last;
}

void dot(u8* data, int width, int height, int x, int y, u8 color) {
	if (x < 0 || x >= width || y < 0 || y >= height)
		return;
	data[y * width + x] = color;
}

} //unnamed namespace

Walk walkOf(int x1, int y1, int x2, int y2) {
	Walk walk;
	walk.x1 = x1;
	walk.y1 = y1;
	walk.sx = x1 < x2 ? 1 : -1;
	walk.sy = y1 < y2 ? 1 : -1;
	walk.dx = abs(x2 - x1);
	walk.dy = abs(y2 - y1);
	return walk;
}

int stepCount(const Walk& walk) {
	return walk.dx > walk.dy ? walk.dx : walk.dy;
}

void pixelAt(const Walk& walk, int k, int* x, int* y, int* err) {
	const int stepsX = axisSteps(walk, true, k);
	const int stepsY = axisSteps(walk, false, k);
	*x = walk.x1 + walk.sx * stepsX;
	*y = walk.y1 + walk.sy * stepsY;
	//It starts at dx - dy, loses dy with a step along x and gains dx with one along y.
	*err = walk.dx * (stepsY + 1) - walk.dy * (stepsX + 1);
}

void stepOn(const Walk& walk, int* x, int* y, int* err) {
	const int e2 = *err * 2;
	if (e2 >= -walk.dy) {
		*err -= walk.dy;
		*x += walk.sx;
	}
	if (e2 <= walk.dx) {
		*err += walk.dx;
		*y += walk.sy;
	}
}

bool stepsInside(const Walk& walk, int left, int top, int right, int bottom, int* first, int* last) {
	int firstX, lastX, firstY, lastY;
	if (!axisRun(walk, true, left, right, &firstX, &lastX))
		return false;
	if (!axisRun(walk, false, top, bottom, &firstY, &lastY))
		return false;
	*first = firstX > firstY ? firstX : firstY;
	*last = lastX < lastY ? lastX : lastY;
	return *first <= *last;
}

void drawChevronLine(u8* data, int width, int height, int x1, int y1, int x2, int y2, u8 color,
                     int dash, int gap, int phase, bool hideDashes) {
	const int period = dash + gap;

	dot(data, width, height, x1, y1, color);
	dot(data, width, height, x2, y2, color);

	//Only the steps on the surface, or within an arrowhead's reach of it.
	const Walk walk = walkOf(x1, y1, x2, y2);
	int first, last;
	if (!stepsInside(walk, -ARROW_LENGTH, -ARROW_LENGTH, width - 1 + ARROW_LENGTH,
	                 height - 1 + ARROW_LENGTH, &first, &last))
		return;

	//The arrowhead: two strokes back from the pixel, at 45 degrees either
	//side of the line. They are the same all along it, so their pixels are
	//worked out once, as offsets from the pixel.
	const int degrees = (int)(atan2((double)(y2 - y1), (double)(x2 - x1)) * 180 / PI);
	const double angle = PI * degrees / 180;
	int strokeX[2 * (ARROW_LENGTH + 1)], strokeY[2 * (ARROW_LENGTH + 1)];
	int strokePixels = 0;
	for (int side = -1; side <= 1; side += 2) {
		const Walk stroke = walkOf(0, 0, -lround(ARROW_LENGTH * cos(angle + side * PI / 4)),
		                           -lround(ARROW_LENGTH * sin(angle + side * PI / 4)));
		for (int k = 0; k <= stepCount(stroke); ++k) {
			int err;
			pixelAt(stroke, k, &strokeX[strokePixels], &strokeY[strokePixels], &err);
			++strokePixels;
		}
	}

	int x, y, err;
	pixelAt(walk, first, &x, &y, &err);
	for (int k = first;; ++k) {
		const int count = phase + k;
		if (count % period < dash && !hideDashes)
			dot(data, width, height, x, y, color);
		//Where a dash starts and where it ends, but not on the line's first pixel.
		if (k != 0 && (count % period == 0 || count % period == dash))
			for (int i = 0; i < strokePixels; ++i)
				dot(data, width, height, x + strokeX[i], y + strokeY[i], color);
		if (k == last)
			break;
		stepOn(walk, &x, &y, &err);
	}
}

} //linewalk
