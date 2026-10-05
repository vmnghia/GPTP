#include "sel_subgroups.h"
#include "sel_exe.h"
#include "sel_profile.h"
#include <SCBW/selection_ext.h>
#include <algorithm>
#include <cstring>

using namespace selext;

namespace {

u32 keys[SEL_MAX];			//of clientSel, sorted with it
CUnit* previous[SEL_MAX];	//the selection at the last sort
u32 previousCount;
u32 activeKey;
bool freshPending;	//set by markFresh, used by the next sort
u32 stamp;			//changes with activeKey
u32 version;		//changes with every sort of the console list

//The panel highlight: the wireframe colour remap and the game palette.
u8* const WIRE_REMAP = (u8*)0x0050CE80;
//PALETTEENTRY[256]. Live: the tileset's colour cycling rotates some entries
//(records at 0x6CE2A0) and fades rewrite it.
const u8* const GAME_PALETTE = (const u8*)0x006CE320;
const u8* const CYCLE_RECORDS = (const u8*)0x006CE2A0;
const u32 CYCLE_RECORD_COUNT = 8;
const u32 DIM_PERCENT = 40;
const u8 DIM_ENTRIES[] = { 0x01, 0x02, 0x11, 0x12, 0x13, 0x14, 0x19, 0x1A, 0x1B, 0x1C };
u8 paletteSeen[256 * 4];
u8 dimOf[256];
bool dimBuilt;

//Most energy first; equal energy keeps the selection's order.
bool moreEnergy(const CUnit* a, const CUnit* b) {
	return a->energy > b->energy;
}

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
	version++;
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
	version++;
}

void viewBegin() {
	SEL_PROFILE_SCOPE(VIEW);
	static CUnit* view[SEL_MAX];
	u32 m = activeMembers(view);
	//Most energy first: the check wants one unit able to cast.
	std::stable_sort(view, view + m, moreEnergy);
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
	SEL_PROFILE_SCOPE(WIRE_DIM);
	if (unit == NULL || !isDimmed(unit))
		return;
	//Cycling entries are never a dim colour (they would shimmer) and their
	//rotation is not a palette change.
	static bool cycling[256];
	memset(cycling, 0, sizeof(cycling));
	cyclingEntries(CYCLE_RECORDS, CYCLE_RECORD_COUNT, cycling);
	if (!dimBuilt || !samePalette(paletteSeen, GAME_PALETTE, cycling)) {
		memcpy(paletteSeen, GAME_PALETTE, sizeof(paletteSeen));
		for (u32 i = 0; i < 256; i++)
			dimOf[i] = dimIndex(paletteSeen, cycling, (u8)i, DIM_PERCENT);
		//A changed palette (a fade): the panel draws again with the new
		//colours, so its last draw uses the settled palette.
		if (dimBuilt)
			stamp++;
		dimBuilt = true;
	}
	for (u32 k = 0; k < sizeof(DIM_ENTRIES); k++)
		WIRE_REMAP[DIM_ENTRIES[k]] = dimOf[WIRE_REMAP[DIM_ENTRIES[k]]];
}

u32 activeStamp() {
	return stamp;
}

u32 selectionVersion() {
	return version;
}

} //selsub
