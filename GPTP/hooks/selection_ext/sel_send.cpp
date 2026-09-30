#include "sel_send.h"
#include "sel_exe.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u8* const	LAST_HOTKEY				= (u8*)		0x00597280;
u32* const	LAST_HOTKEY_TICK		= (u32*)	0x0059727C;
const u8* const LOCAL_VISIBILITY	= (u8*)		0x0057F0B0;
const u8* const REPLAY_VISIBILITY	= (u8*)		0x006D0F18;
const u32 DOUBLE_TAP_MS = 500;

//Sends one packet: vanilla ids for 12 or fewer units, chunks for more.
void sendPacket(ChunkMode mode, const u16* tags, u32 count) {
	if (count <= VANILLA_MAX) {
		u8 command[2 + 2 * VANILLA_MAX];
		command[0] = mode == CHUNK_REPLACE ? 0x09 : mode == CHUNK_ADD ? 0x0A : 0x0B;
		command[1] = (u8)count;
		memcpy(&command[2], tags, count * sizeof(u16));
		selexe::queueCommand(command, 2 + 2 * count);
		return;
	}
	//The viewer's selection in a replay is local only, and the executor would
	//skip these anyway.
	if (*IS_IN_REPLAY)
		return;
	u8 chunk[CHUNK_MAX_BYTES];
	for (u32 c = 0; c < chunkCountFor(count); c++)
		selexe::queueCommand(chunk, chunkBuild(chunk, mode, tags, count, c));
}

} //unnamed namespace

namespace selsend {

void cmdactSelect(u32 count, CUnit** list) {
	static u16 added[SEL_MAX];
	static u16 removed[SEL_MAX];
	static u16 all[SEL_MAX];

	*LAST_HOTKEY = 0xFF;
	if (count > SEL_MAX)
		count = SEL_MAX;
	const u8 visible = *IS_IN_REPLAY ? *REPLAY_VISIBILITY : *LOCAL_VISIBILITY;
	const u32 lastCount = listCount(lastSent, SEL_MAX);

	u32 addedCount = 0;
	for (u32 k = 0; k < count; k++) {
		CUnit* unit = list[k];
		if (listFind(lastSent, lastCount, unit) >= 0)
			continue;
		if (unit->sprite->visibilityFlags & visible)
			added[addedCount++] = tagOf(unit);
	}
	u32 removedCount = 0;
	for (u32 s = 0; s < lastCount; s++)
		if (listFind(list, count, lastSent[s]) < 0)
			removed[removedCount++] = tagOf(lastSent[s]);

	listClear(lastSent, SEL_MAX);
	memcpy(lastSent, list, count * sizeof(CUnit*));
	mirrorLastSent();

	if (addedCount + removedCount >= count) {
		//A whole new selection, in reverse order as vanilla sends it.
		for (u32 k = 0; k < count; k++)
			all[count - 1 - k] = tagOf(list[k]);
		sendPacket(CHUNK_REPLACE, all, count);
	}
	else {
		if (removedCount != 0)
			sendPacket(CHUNK_REMOVE, removed, removedCount);
		if (addedCount != 0)
			sendPacket(CHUNK_ADD, added, addedCount);
	}
}

void cmdactHotkey(u32 group, u32 action, CUnit** list, u32 count) {
	const u8 command[3] = { 0x13, (u8)action, (u8)group };
	selexe::queueCommand(command, sizeof(command));
	if (action != 1) {
		*LAST_HOTKEY = 0xFF;
		return;
	}
	if (count > SEL_MAX)
		count = SEL_MAX;
	listClear(lastSent, SEL_MAX);
	memcpy(lastSent, list, count * sizeof(CUnit*));
	mirrorLastSent();
	const u32 now = GetTickCount();
	if (group == *LAST_HOTKEY && now - *LAST_HOTKEY_TICK < DOUBLE_TAP_MS) {
		selexe::centerViewOnGroup(group);
		*LAST_HOTKEY = 0xFF;
	}
	else {
		*LAST_HOTKEY = (u8)group;
		*LAST_HOTKEY_TICK = now;
	}
}

} //selsend
