#include "smart_larva.h"

namespace smartlarva {

u8 morphCommandId(bool shiftHeld) {
	return shiftHeld ? CMD_MORPH_ALL : CMD_MORPH_ONE;
}

u32 pickMorpher(const u32* keys, u32 n) {
	u32 best = n;
	u32 bestCount = 0;
	for (u32 i = 0; i < n; i++) {
		u32 count = 0;
		for (u32 j = 0; j < n; j++)
			if (keys[j] == keys[i])
				count++;
		if (count > bestCount) {
			best = i;
			bestCount = count;
		}
	}
	return best;
}

} //smartlarva
