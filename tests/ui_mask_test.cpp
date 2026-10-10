//Host test of the UI mask's pure logic (SCBW/ui_mask.cpp). Build and run with
//ui_mask_test.bat (MSVC x86); it needs no game.
#include <SCBW/ui_mask.h>
#include <cstdio>
#include <string>
#include <vector>

using namespace uimask;

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

//An image from rows of text: '.' is transparent (index 0), anything else art.
std::vector<u8> image(const char* const* rows, u32 width, u32 height) {
	std::vector<u8> v;
	for (u32 y = 0; y < height; ++y)
		for (u32 x = 0; x < width; ++x)
			v.push_back(rows[y][x] == '.' ? 0 : 7);
	return v;
}

//A mask as rows of text: '.' effects, '#' UI.
std::string text(const Mask& mask) {
	std::string s;
	for (u32 y = 0; y < mask.height; ++y) {
		for (u32 x = 0; x < mask.width; ++x)
			s += mask.pixels[y * mask.width + x] == UI ? '#' : '.';
		s += '\n';
	}
	return s;
}

std::string masked(const char* const* rows, u32 width, u32 height) {
	const std::vector<u8> art = image(rows, width, height);
	Mask mask;
	reset(mask, width, height);
	addConsoleImage(mask, art.data());
	return text(mask);
}

} //unnamed namespace

int main() {
	//The game view above a full-width console.
	{
		const char* rows[] = { "........", "........", "########", "########" };
		CHECK(masked(rows, 8, 4) == "........\n........\n########\n########\n");
	}
	//A tall block at one side (the minimap's frame): masked, the view above it is not.
	{
		const char* rows[] = { "........", "###.....", "###.....", "########" };
		CHECK(masked(rows, 8, 4) == "........\n###.....\n###.....\n########\n");
	}
	//A transparent hole enclosed by the console stays masked.
	{
		const char* rows[] = { "........", "########", "#..#####", "########" };
		CHECK(masked(rows, 8, 4) == "........\n########\n########\n########\n");
	}
	//A transparent pocket open to the top is game view.
	{
		const char* rows[] = { "........", "##.#####", "##.#####", "########" };
		CHECK(masked(rows, 8, 4) == "........\n##.#####\n##.#####\n########\n");
	}
	//No transparent pixel in the top row: nothing is game view.
	{
		const char* rows[] = { "########", "........", "........" };
		CHECK(masked(rows, 8, 3) == "########\n########\n########\n");
	}
	//diagonal: a transparent pixel touching the view only at a corner is masked.
	{
		const char* rows[] = { "....####", "....####", "####.###", "########" };
		CHECK(masked(rows, 8, 4) == "....####\n....####\n########\n########\n");
	}
	//Adding only sets pixels: one already marked UI in the game view stays
	//(a later region), and reset() clears to the new size.
	{
		const char* rows[] = { "....", "...." };
		const std::vector<u8> art = image(rows, 4, 2);
		Mask mask;
		reset(mask, 4, 2);
		mask.pixels[1] = UI;
		addConsoleImage(mask, art.data());
		CHECK(text(mask) == ".#..\n....\n");
		reset(mask, 3, 1);
		CHECK(mask.width == 3 && mask.height == 1 && text(mask) == "...\n");
	}
	//A null image and a zero-sized mask change nothing.
	{
		Mask mask;
		reset(mask, 2, 2);
		addConsoleImage(mask, NULL);
		CHECK(text(mask) == "..\n..\n");
		const u8 art[4] = { 7, 7, 7, 7 };
		Mask empty;
		CHECK(empty.width == 0 && empty.height == 0);
		addConsoleImage(empty, art);
		CHECK(empty.pixels.empty());
	}
	//largest: 2048x1536, a console over the bottom 100 rows.
	{
		const u32 width = 2048, height = 1536, console = 100;
		std::vector<u8> art((size_t)width * height, 0);
		for (size_t i = (size_t)width * (height - console); i < art.size(); ++i)
			art[i] = 7;
		Mask mask;
		reset(mask, width, height);
		addConsoleImage(mask, art.data());
		size_t ui = 0;
		for (size_t i = 0; i < mask.pixels.size(); ++i)
			ui += mask.pixels[i] == UI;
		CHECK(ui == (size_t)width * console);
		CHECK(mask.pixels[0] == EFFECTS && mask.pixels.back() == UI);
	}

	if (failures != 0) {
		printf("FAIL: %u checks (first at ui_mask_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
