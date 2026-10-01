#include "sel_groups.h"
#include "sel_exe.h"
#include "../interface/selection.h"
#include "../interface/resolution.h"
#include <SCBW/selection_ext.h>

using namespace selext;

namespace {

const u16* const RING_STAMPS = (const u16*)0x0063FE40;	//[8][8]

} //unnamed namespace

namespace selgroups {

bool selectRecentGroupOf(u32 tag) {
	const u32 player = (u32)*LOCAL_HUMAN_ID;
	if (player >= PLAYERS)
		return false;
	const int ringGroup = newestRingGroupWith(&groupsExt[player][FIRST_RING_GROUP],
		&RING_STAMPS[player * 8], (u16)tag);
	if (ringGroup < 0)
		return false;
	hooks::selectUnitGroup(FIRST_RING_GROUP + ringGroup);
	return true;
}

void centerViewOnGroup(u32 group) {
	const u32 player = (u32)*LOCAL_HUMAN_ID;
	if (group >= FIRST_RING_GROUP || player >= PLAYERS)
		return;
	const u16* const slots = groupsExt[player][group];
	u32 n = 0;
	while (n < SEL_MAX && slots[n] != 0)
		n++;
	s32 sumX = 0, sumY = 0;
	s32 counted = 0;
	for (u32 i = 0; i < n; i++) {
		CUnit* unit = unitOfTag(slots[i]);
		if (unit == NULL || (unit->sprite->flags & CSprite_Flags::Hidden))
			continue;
		if (!selexe::canMultiSelect(unit) && n > 1)
			continue;
		sumX += unit->sprite->position.x;
		sumY += unit->sprite->position.y;
		counted++;
	}
	if (counted == 0)
		return;
	//Vanilla subtracts 320 and 200, half of a 640x400 view.
	const s32 x = (sumX / counted - resolution::viewWidth() / 2) & ~7;
	const s32 y = (sumY / counted - resolution::viewHeight() / 2) & ~7;
	selexe::moveScreen(x, y);
}

} //selgroups
