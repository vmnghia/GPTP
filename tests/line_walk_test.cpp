//Host test of the line walk's pure logic (graphics/line_walk.cpp). Build and
//run with line_walk_test.bat (MSVC x86); it needs no game.
#include <graphics/line_walk.h>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

using namespace linewalk;

namespace {

u32 failures;
u32 firstLine;

void check(bool ok, u32 line) {
	if (!ok) {
		if (failures == 0)
			firstLine = line;
		failures++;
	}
}

#define CHECK(x) check((x), __LINE__)

//-------- The reference: the lines as Bitmap.cpp first drew them, walking
//every pixel from one end to the other. --------//

struct Surface {
	int width, height;
	std::vector<u8> data;
	Surface(int w, int h) : width(w), height(h), data((size_t)w * h, 0) {}

	void dot(int x, int y, u8 color) {
		if (x < 0 || x >= width || y < 0 || y >= height)
			return;
		data[(size_t)y * width + x] = color;
	}
	void line(int x1, int y1, int x2, int y2, u8 color) {
		const int dx = abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
		const int dy = -abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
		int err = dx + dy;
		int x = x1, y = y1;
		dot(x, y, color);
		while (x != x2 || y != y2) {
			const int e2 = err * 2;
			if (e2 >= dy) {
				err += dy;
				x += sx;
			}
			if (e2 <= dx) {
				err += dx;
				y += sy;
			}
			dot(x, y, color);
		}
	}
	void arrow(int x, int y, int length, int angle, u8 color) {
		const double arrowAngle = 3.14159265358979323846264338327950288 / 4;
		double lineAngle = 3.14159265358979323846264338327950288 * angle / 180;
		int arrowX1 = x - lround(length * cos(lineAngle + arrowAngle));
		int arrowY1 = y - lround(length * sin(lineAngle + arrowAngle));
		int arrowX2 = x - lround(length * cos(lineAngle - arrowAngle));
		int arrowY2 = y - lround(length * sin(lineAngle - arrowAngle));
		line(x, y, arrowX1, arrowY1, color);
		line(x, y, arrowX2, arrowY2, color);
	}
	void chevronLine(int x1, int y1, int x2, int y2, u8 color, int dash, int gap, int phase, bool hideDashes) {
		const int arrowLength = 3;
		double lineAngle = atan2(y2 - y1, x2 - x1);
		int count = phase;
		const int dx = abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
		const int dy = -abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
		int err = dx + dy;
		int x = x1, y = y1;
		dot(x1, y1, color);
		dot(x2, y2, color);
		if (count % (dash + gap) < dash && !hideDashes)
			dot(x, y, color);
		count++;
		while (x != x2 || y != y2) {
			const int e2 = err * 2;
			if (e2 >= dy) {
				err += dy;
				x += sx;
			}
			if (e2 <= dx) {
				err += dx;
				y += sy;
			}
			if (count % (dash + gap) < dash && !hideDashes)
				dot(x, y, color);
			if (count % (dash + gap) == 0 || count % (dash + gap) == dash)
				arrow(x, y, arrowLength, lineAngle * 180 / 3.14159265358979323846264338327950288, color);
			count++;
		}
	}
};

//Every pixel of the reference walk, in order.
struct Pixel {
	int x, y, err;
};
std::vector<Pixel> referenceWalk(int x1, int y1, int x2, int y2) {
	std::vector<Pixel> v;
	const int dx = abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
	const int dy = -abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
	int err = dx + dy;
	int x = x1, y = y1;
	const Pixel start = {x, y, err};
	v.push_back(start);
	while (x != x2 || y != y2) {
		const int e2 = err * 2;
		if (e2 >= dy) {
			err += dy;
			x += sx;
		}
		if (e2 <= dx) {
			err += dx;
			y += sy;
		}
		const Pixel p = {x, y, err};
		v.push_back(p);
	}
	return v;
}

//A fixed sequence of numbers, the same on every run.
u32 seed = 12345;
int next(int low, int high) {
	seed = seed * 1664525u + 1013904223u;
	return low + (int)((seed >> 8) % (u32)(high - low + 1));
}

//pixelAt and stepOn against the reference, at every step of one line.
void checkWalk(int x1, int y1, int x2, int y2) {
	const std::vector<Pixel> ref = referenceWalk(x1, y1, x2, y2);
	const Walk walk = walkOf(x1, y1, x2, y2);
	bool count = stepCount(walk) == (int)ref.size() - 1;
	bool jump = true, step = true;
	int sx = 0, sy = 0, serr = 0;
	for (int k = 0; count && k < (int)ref.size(); ++k) {
		int x, y, err;
		pixelAt(walk, k, &x, &y, &err);
		jump = jump && x == ref[k].x && y == ref[k].y && err == ref[k].err;
		if (k == 0)
			pixelAt(walk, 0, &sx, &sy, &serr);
		else
			stepOn(walk, &sx, &sy, &serr);
		step = step && sx == ref[k].x && sy == ref[k].y && serr == ref[k].err;
	}
	CHECK(count);
	CHECK(jump);
	CHECK(step);
}

//stepsInside against a scan of the reference walk.
void checkInside(int x1, int y1, int x2, int y2, int left, int top, int right, int bottom) {
	const std::vector<Pixel> ref = referenceWalk(x1, y1, x2, y2);
	int first = -1, last = -1;
	bool oneRun = true;	//the steps inside are next to each other
	for (int k = 0; k < (int)ref.size(); ++k)
		if (ref[k].x >= left && ref[k].x <= right && ref[k].y >= top && ref[k].y <= bottom) {
			if (first < 0)
				first = k;
			else if (last != k - 1)
				oneRun = false;
			last = k;
		}
	CHECK(oneRun);
	int gotFirst = -7, gotLast = -7;
	const bool got = stepsInside(walkOf(x1, y1, x2, y2), left, top, right, bottom, &gotFirst, &gotLast);
	CHECK(got == (first >= 0));
	if (got && first >= 0) {
		CHECK(gotFirst == first);
		CHECK(gotLast == last);
	}
}

//drawChevronLine against the reference, on a surface this size.
void checkChevron(int width, int height, int x1, int y1, int x2, int y2, int dash, int gap, int phase,
                  bool hideDashes) {
	Surface ref(width, height);
	ref.chevronLine(x1, y1, x2, y2, 9, dash, gap, phase, hideDashes);
	//A border around the surface, to catch a write outside it.
	const int border = 8;
	std::vector<u8> buffer((size_t)width * height + 2 * border, 0xEE);
	memset(&buffer[border], 0, (size_t)width * height);
	drawChevronLine(&buffer[border], width, height, x1, y1, x2, y2, 9, dash, gap, phase, hideDashes);
	CHECK(memcmp(&buffer[border], ref.data.data(), ref.data.size()) == 0);
	bool borderKept = true;
	for (int i = 0; i < border; ++i)
		borderKept = borderKept && buffer[i] == 0xEE && buffer[buffer.size() - 1 - i] == 0xEE;
	CHECK(borderKept);
}

} //unnamed namespace

int main() {
	//the walk: every line between two points of a small square (every
	//direction, both rounding ties), each step by a jump and by stepping on.
	for (int x1 = -3; x1 <= 3; x1 += 3)
		for (int y1 = -3; y1 <= 3; y1 += 3)
			for (int x2 = -13; x2 <= 13; ++x2)
				for (int y2 = -13; y2 <= 13; ++y2)
					checkWalk(x1, y1, x2, y2);
	//the walk: long lines.
	for (int i = 0; i < 300; ++i)
		checkWalk(next(-9000, 9000), next(-9000, 9000), next(-9000, 9000), next(-9000, 9000));
	//the walk: a point, and the four straight lines.
	checkWalk(5, 5, 5, 5);
	checkWalk(0, 0, 4000, 0);
	checkWalk(0, 0, -4000, 0);
	checkWalk(0, 0, 0, 4000);
	checkWalk(0, 0, 0, -4000);

	//steps inside a rect: small lines around a small rect, from every side.
	for (int x1 = -9; x1 <= 15; x1 += 4)
		for (int y1 = -9; y1 <= 15; y1 += 4)
			for (int x2 = -9; x2 <= 15; ++x2)
				for (int y2 = -9; y2 <= 15; ++y2)
					checkInside(x1, y1, x2, y2, 0, 0, 7, 5);
	//steps inside a rect: long lines over a screen with the arrowhead's margin.
	for (int i = 0; i < 300; ++i)
		checkInside(next(-9000, 9000), next(-9000, 9000), next(-9000, 9000), next(-9000, 9000),
		            -3, -3, 1602, 902);
	//steps inside a rect: a rect of one pixel, and a line that only touches a corner.
	checkInside(-20, -10, 20, 10, 0, 0, 0, 0);
	checkInside(-5, 5, 5, -5, 0, 0, 7, 5);
	checkInside(-6, 5, 5, -6, 0, 0, 7, 5);

	//the chevron line: on the surface, with the default pattern at every phase
	//the caller makes (0 down to -12).
	for (int phase = -12; phase <= 0; ++phase) {
		checkChevron(64, 48, 3, 4, 60, 40, 10, 3, phase, false);
		checkChevron(64, 48, 60, 40, 3, 4, 10, 3, phase, false);
		checkChevron(64, 48, 5, 44, 58, 2, 10, 3, phase, false);
	}
	//the chevron line: arrowheads only.
	checkChevron(64, 48, 3, 4, 60, 40, 10, 3, -5, true);
	//the chevron line: the game's pattern (pairs of arrowheads 4 px apart,
	//every 18 px), at every phase.
	for (int phase = -17; phase <= 0; ++phase) {
		checkChevron(64, 48, 3, 4, 60, 40, 4, 14, phase, true);
		checkChevron(64, 48, 700, -300, -500, 400, 4, 14, phase, true);
	}
	//the chevron line: a point, in and out of the surface.
	checkChevron(64, 48, 10, 10, 10, 10, 10, 3, 0, false);
	checkChevron(64, 48, -10, 10, -10, 10, 10, 3, 0, false);
	//the chevron line: both ends off the surface, crossing it and missing it.
	checkChevron(64, 48, -500, -300, 700, 400, 10, 3, -4, false);
	checkChevron(64, 48, -500, 100, 700, 300, 10, 3, -4, false);
	//the chevron line: an arrowhead just off the edge still reaches in.
	for (int offset = 1; offset <= 5; ++offset) {
		checkChevron(64, 48, -offset, -40, -offset, 90, 10, 3, 0, false);
		checkChevron(64, 48, 63 + offset, -40, 63 + offset, 90, 10, 3, 0, false);
		checkChevron(64, 48, -40, -offset, 90, -offset, 10, 3, 0, false);
		checkChevron(64, 48, -40, 47 + offset, 90, 47 + offset, 10, 3, 0, false);
	}
	//the chevron line: every direction from the centre, ends on and off.
	for (int angle = 0; angle < 360; angle += 3) {
		const double a = angle * 3.14159265358979323846 / 180;
		checkChevron(64, 48, 32, 24, 32 + (int)(25 * cos(a)), 24 + (int)(20 * sin(a)), 10, 3, -7, false);
		checkChevron(64, 48, 32, 24, 32 + (int)(900 * cos(a)), 24 + (int)(900 * sin(a)), 10, 3, -7, false);
		checkChevron(64, 48, 32 + (int)(900 * cos(a)), 24 + (int)(900 * sin(a)), 30, 20, 10, 3, -7, false);
	}
	//the chevron line: long lines across a map, most of them off the surface.
	for (int i = 0; i < 400; ++i)
		checkChevron(320, 200, next(-9000, 9000), next(-9000, 9000), next(-9000, 9000), next(-9000, 9000),
		             10, 3, -next(0, 12), false);
	//the chevron line: short lines near a small surface, other patterns.
	for (int i = 0; i < 2000; ++i)
		checkChevron(40, 30, next(-30, 70), next(-30, 60), next(-30, 70), next(-30, 60),
		             next(1, 12), next(0, 6), next(-20, 20), next(0, 3) == 0);

	if (failures != 0) {
		printf("FAIL: %u checks (first at line_walk_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
