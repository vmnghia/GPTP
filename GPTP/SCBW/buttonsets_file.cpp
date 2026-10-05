#include "buttonsets_file.h"
#include <cstddef>

namespace {

u32 u16At(const u8* p) {
	return (u32)p[0] | (u32)p[1] << 8;
}

u32 u32At(const u8* p) {
	return (u32)p[0] | (u32)p[1] << 8 | (u32)p[2] << 16 | (u32)p[3] << 24;
}

} //unnamed namespace

namespace bsfile {

const char* parse(const u8* data, u32 size, u32 codeStart, u32 codeEnd, SetEntry* entries) {
	if (size < HEADER_BYTES || data[0] != 'M' || data[1] != 'B' || data[2] != 'T' || data[3] != 'S')
		return "not a button set file";
	if (u16At(data + 4) != VERSION)
		return "unknown version";
	if (u16At(data + 6) != SET_COUNT)
		return "set count is not 250";
	u32 at = HEADER_BYTES;	//always <= size
	for (u32 s = 0; s < SET_COUNT; s++) {
		if (size - at < SET_HEADER_BYTES)
			return "a set is cut short";
		const u32 count = u16At(data + at);
		entries[s].buttonCount = count;
		entries[s].connectedUnit = u32At(data + at + 4);
		at += SET_HEADER_BYTES;
		if ((size - at) / BUTTON_BYTES < count)
			return "a set is cut short";
		entries[s].firstButton = at;
		for (u32 i = 0; i < count; i++, at += BUTTON_BYTES) {
			const u8* const button = data + at;
			const u32 position = u16At(button);
			if (position < 1 || position > MAX_POSITION)
				return "a position outside 1-15";
			const u32 condition = u32At(button + 4);
			const u32 action = u32At(button + 8);
			if (condition < codeStart || condition >= codeEnd || action < codeStart || action >= codeEnd)
				return "an address outside the code";
		}
	}
	if (at != size)
		return "data after the last set";
	return NULL;
}

} //bsfile
