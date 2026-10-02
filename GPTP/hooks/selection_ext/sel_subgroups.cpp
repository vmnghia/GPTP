#include "sel_subgroups.h"
#include "sel_exe.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u32 keys[SEL_MAX];			//of clientSel, sorted with it
CUnit* previous[SEL_MAX];	//the selection at the last sort
u32 previousCount;
u32 activeKey;
bool freshPending;	//set by markFresh, used by the next sort
u32 stamp;			//changes with activeKey

//The panel highlight: the wireframe colour remap and the game palette.
u8* const WIRE_REMAP = (u8*)0x0050CE80;
const u8* const GAME_PALETTE = (const u8*)0x006CE320;	//PALETTEENTRY[256]
const u32 DIM_PERCENT = 40;
const u8 DIM_ENTRIES[] = { 0x01, 0x02, 0x11, 0x12, 0x13, 0x14, 0x19, 0x1A, 0x1B, 0x1C };
u8 paletteSeen[256 * 4];
u8 dimOf[256];
bool dimBuilt;

void setActiveKey(u32 key) {
	if (key != activeKey)
		stamp++;
	activeKey = key;
}

u16 priorityOf(u16 type) {
	if (type >= UNIT_TYPES)
		return 0;
	return defaultPriority((units_dat::BaseProperty[type] & UnitProperty::Hero) != 0,
	                       units_dat::BuildScore[type]);
}

} //unnamed namespace

namespace selsub {

u32 keyOf(CUnit* unit) {
	const u16 type = subgroupType(unit->id);
	return subgroupKey(priorityOf(type), type, (unit->status & UnitStatus::IsHallucination) != 0);
}

CUnit* sortAndPickLeader(u32 n) {
	if (n == 0) {
		previousCount = 0;
		return NULL;
	}
	for (u32 i = 0; i < n; i++)
		keys[i] = keyOf(clientSel[i]);
	sortBySubgroup(clientSel, keys, n);
	const bool keep = previousCount != 0 &&
		keepsActive(freshPending, previous, previousCount, clientSel, n);
	freshPending = false;
	setActiveKey(activeKeyAfter(keys, n, activeKey, keep));
	memcpy(previous, clientSel, n * sizeof(CUnit*));
	previousCount = n;
	CUnit* leader = NULL;
	for (u32 i = 0; i < n; i++)
		if (keys[i] == activeKey && selexe::outranks(clientSel[i], leader))
			leader = clientSel[i];
	return leader;
}

void markFresh() {
	freshPending = true;
}

bool cycle(bool back) {
	if (clientCount == 0)
		return false;
	const u32 next = keyAfterTab(keys, clientCount, activeKey, back);
	if (next == activeKey)
		return false;
	setActiveKey(next);
	return true;
}

u32 activeMembers(CUnit** out) {
	u32 m = 0;
	for (u32 i = 0; i < clientCount; i++)
		if (keys[i] == activeKey)
			out[m++] = clientSel[i];
	return m;
}

u32 activeFirstIndex() {
	for (u32 i = 0; i < clientCount; i++)
		if (keys[i] == activeKey)
			return i;
	return 0;
}

void reset() {
	previousCount = 0;
	activeKey = 0;
	freshPending = false;
	dimBuilt = false;
	stamp++;
}

void viewBegin() {
	static CUnit* view[SEL_MAX];
	u32 m = activeMembers(view);
	//Most energy first: the check wants one unit able to cast.
	for (u32 i = 1; i < m; i++) {
		CUnit* const unit = view[i];
		u32 j = i;
		for (; j > 0 && view[j - 1]->energy < unit->energy; j--)
			view[j] = view[j - 1];
		view[j] = unit;
	}
	for (u32 i = 0; i < clientCount && m < VANILLA_MAX; i++)
		if (keys[i] != activeKey)
			view[m++] = clientSel[i];
	mirrorClientView(view, m, clientCount);
}

void viewEnd() {
	mirrorClient();
}

bool isDimmed(CUnit* unit) {
	if (clientCount <= 1 || keys[0] == keys[clientCount - 1])
		return false;
	return keyOf(unit) != activeKey;
}

void dimWireframe(CUnit* unit) {
	if (unit == NULL || !isDimmed(unit))
		return;
	if (!dimBuilt || memcmp(paletteSeen, GAME_PALETTE, sizeof(paletteSeen)) != 0) {
		memcpy(paletteSeen, GAME_PALETTE, sizeof(paletteSeen));
		for (u32 i = 0; i < 256; i++)
			dimOf[i] = dimIndex(paletteSeen, (u8)i, DIM_PERCENT);
		dimBuilt = true;
	}
	for (u32 k = 0; k < sizeof(DIM_ENTRIES); k++)
		WIRE_REMAP[DIM_ENTRIES[k]] = dimOf[WIRE_REMAP[DIM_ENTRIES[k]]];
}

u32 activeStamp() {
	return stamp;
}

} //selsub
