#pragma once
#include <cstdint>

namespace hooks {

	bool gameOn();
	bool gameEnd();
	bool nextFrame();

	void injectGameHooks();

} // namespace hooks

struct GRPHeader {
	uint16_t frames;
	uint16_t maxWidth;
	uint16_t maxHeight;
};

struct FrameHeader {
	uint8_t left;
	uint8_t top;
	uint8_t width;
	uint8_t height;
	uint32_t offset;
};

typedef uint8_t *LPBYTE;

struct FrameData {
	uint16_t *lpRowOffsets;
	uint16_t *lpRowSizes;
	uint8_t **lpRowData;
	uint32_t size;
};

struct GradientStop {
	float position;
	int color;
};