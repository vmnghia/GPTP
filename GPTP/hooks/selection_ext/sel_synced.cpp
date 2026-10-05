#include "sel_synced.h"
#include "sel_exe.h"
#include "sel_profile.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u16* const RING_STAMPS				= (u16*)		0x0063FE40;	//[8][8]
const u32* const FRAME_COUNTER		= (const u32*)	0x0057EEBC;

u32 iteratorCursor;

u32 activePlayer() {
	return (u32)*ACTIVE_PLAYER_ID;
}

bool isDying(const CUnit* unit) {
	return unit->mainOrderId == OrderId::Die && unit->mainOrderState == 1;
}

u16* groupSlots(u32 player, u32 group) {
	return groupsExt[player][group];
}

//One fewer dashed ally circle on the sprite (sprite flags bits 1-2 count them).
void dropDashedCircle(CSprite* sprite) {
	u8 count = (sprite->flags >> 1) & 3;
	if (count == 0)
		return;
	count--;
	sprite->flags = (u8)((sprite->flags & ~6) | (count << 1));
	if (count == 0)
		selexe::removeDashedCircle(sprite);
}

//Puts unit at slot of the player's selection (vanilla 0x49AF80).
bool addUnit(u32 player, CUnit* unit, u32 slot) {
	if (slot >= SEL_MAX)
		return false;
	if (unit->sprite->flags & CSprite_Flags::Hidden)
		return false;
	if (slot > 0 && !(selexe::canMultiSelect(unit) && unit->playerId == *ACTIVE_NATION_ID))
		return false;
	playersSel[player][slot] = unit;
	if (selexe::isTeamAlly(player))
		selexe::addDashedCircle(unit);
	return true;
}

//Stores the player's selection in a control group (vanilla 0x4965D0).
void assignSlots(u32 player, u16* slots, bool replace) {
	u32 n = 0;
	if (!replace) {
		CUnit* first = unitOfTag(slots[0]);
		if (first != NULL && !selexe::canMultiSelect(first))
			return;
		while (n < SEL_MAX && slots[n] != 0)
			n++;
	}
	else
		memset(slots, 0, SEL_MAX * sizeof(u16));
	for (u32 j = 0; j < SEL_MAX; j++) {
		CUnit* unit = playersSel[player][j];
		if (unit == NULL || unit->playerId != *ACTIVE_NATION_ID)
			return;
		const u16 tag = tagOf(unit);
		if (tag == 0)
			continue;
		if (!replace && n > 0) {
			bool present = false;
			for (u32 k = 0; k < n && !present; k++)
				present = (slots[k] == tag);
			if (present || !selexe::canMultiSelect(unit))
				continue;
		}
		if (n >= SEL_MAX)
			return;	//vanilla writes one past the group here
		slots[n++] = tag;
		if (n >= SEL_MAX)
			return;
	}
}

void groupAssign(u32 player, u32 group, bool replace) {
	assignSlots(player, groupSlots(player, group), replace);
	mirrorGroup(player, group);
}

//Copies the selection into the oldest recent-selection group.
void ringPush(u32 player) {
	u16* stamps = &RING_STAMPS[player * 8];
	const u8 slot = oldestRingSlot(stamps);
	//0xFF + 10 wraps to group 9, as in vanilla; its out-of-bounds stamp is skipped.
	groupAssign(player, (u8)(slot + FIRST_RING_GROUP), true);
	if (slot < 8)
		stamps[slot] = (u16)*FRAME_COUNTER;
}

//Selects a control group (vanilla 0x496940).
void groupRecall(u32 player, u32 group) {
	u16* slots = groupSlots(player, group);
	u32 n = 0;
	while (n < SEL_MAX && slots[n] != 0)
		n++;
	if (n == 0)
		return;
	selsync::clearSelection(player);
	u32 i = 0;
	while (i < n) {
		CUnit* unit = unitOfTag(slots[i]);
		if (unit != NULL && unit->playerId == *ACTIVE_NATION_ID
			&& !(unit->sprite->flags & CSprite_Flags::Hidden)
			&& (selexe::canMultiSelect(unit) || n <= 1))
		{
			playersSel[player][i] = unit;
			if (selexe::isTeamAlly(player))
				selexe::addDashedCircle(unit);
			i++;
		}
		else {
			n--;
			slots[i] = slots[n];
			slots[n] = 0;
		}
	}
	mirrorPlayer(player);
	mirrorGroup(player, group);
	if (group >= FIRST_RING_GROUP)
		RING_STAMPS[player * 8 + group - FIRST_RING_GROUP] = (u16)*FRAME_COUNTER;
	else
		ringPush(player);
}

//The rules of 0x09 (vanilla 0x4C2750), for any count.
void commitReplace(u32 player, const u16* tags, u32 count) {
	selsync::clearSelection(player);
	if (player >= PLAYERS)
		return;
	u32 added = 0;
	for (u32 i = 0; i < count; i++) {
		CUnit* unit = unitOfTag(tags[i]);
		if (unit == NULL || listFind(playersSel[player], SEL_MAX, unit) >= 0)
			continue;
		if (unit->id == UnitId::TerranNuclearMissile)
			continue;
		if (addUnit(player, unit, added))
			added++;
	}
	mirrorPlayer(player);
	if (added > 1)
		ringPush(player);
}

//The rules of 0x0A (vanilla 0x4C2560): refused whole past the limit.
void commitAdd(u32 player, const u16* tags, u32 count) {
	if (player >= PLAYERS)
		return;
	u32 current = listCount(playersSel[player], SEL_MAX);
	if (count + current > SEL_MAX)
		return;
	for (u32 i = 0; i < count; i++) {
		CUnit* unit = unitOfTag(tags[i]);
		if (unit == NULL || listFind(playersSel[player], SEL_MAX, unit) >= 0)
			continue;
		if (addUnit(player, unit, current))
			current++;
	}
	mirrorPlayer(player);
	if (current > 1)
		ringPush(player);
}

//The rules of 0x0B (vanilla 0x4BFB40).
void commitRemove(u32 player, const u16* tags, u32 count) {
	u32 left = 0;
	for (u32 i = 0; i < count; i++) {
		CUnit* unit = unitOfTag(tags[i]);
		if (unit == NULL)
			continue;
		if (player >= PLAYERS) {
			left = 0;
			continue;
		}
		if (selexe::isTeamAlly(player))
			dropDashedCircle(unit->sprite);
		left = listRemove(playersSel[player], SEL_MAX, unit);
	}
	if (player >= PLAYERS)
		return;
	mirrorPlayer(player);
	if (left > 1)
		ringPush(player);
}

} //unnamed namespace

namespace selsync {

CUnit* nextSelected() {
	SEL_PROFILE_COUNT(NEXT_SELECTED);
	if (*selectionIndexStart == 0)
		iteratorCursor = 0;
	*selectionIndexStart = 1;
	const u32 player = activePlayer();
	if (player >= PLAYERS)
		return NULL;
	CUnit** const selection = playersSel[player];
	while (iteratorCursor < SEL_MAX) {
		CUnit* unit = selection[iteratorCursor];
		if (unit == NULL)
			return NULL;
		if (unit->sprite != NULL && !isDying(unit)) {
			iteratorCursor++;
			return unit;
		}
		//The removal moves the next unit into this slot.
		removeFromAllSelections(unit);
	}
	return NULL;
}

void removeFromAllSelections(CUnit* unit) {
	for (u32 p = 0; p < PLAYERS; p++) {
		listRemove(playersSel[p], SEL_MAX, unit);
		mirrorPlayer(p);
	}
	CSprite* sprite = unit->sprite;
	if (sprite != NULL && (sprite->flags & 0x06)) {
		sprite->flags &= ~0x06;
		for (CImage* image = sprite->images.tail; image != NULL; image = image->link.prev)
			if (image->id >= 0x23B && image->id <= 0x244) {
				selexe::freeImage(image);
				break;
			}
	}
	listRemoveSwapLast(lastSent, SEL_MAX, unit);
	mirrorLastSent();
}

void clearSelection(u32 player) {
	if (player >= PLAYERS)
		return;
	if (selexe::isTeamAlly(player))
		for (u32 i = 0; i < SEL_MAX && playersSel[player][i] != NULL; i++)
			dropDashedCircle(playersSel[player][i]->sprite);
	listClear(playersSel[player], SEL_MAX);
	mirrorPlayer(player);
}

void recvSelect(const u8* packet) {
	const u32 count = packet[1];
	if (count > VANILLA_MAX)
		return;
	commitReplace(activePlayer(), (const u16*)&packet[2], count);
}

void recvShiftSelect(const u8* packet) {
	const u32 count = packet[1];
	if (count > VANILLA_MAX)
		return;
	commitAdd(activePlayer(), (const u16*)&packet[2], count);
}

void recvShiftDeselect(const u8* packet) {
	const u32 count = packet[1];
	if (count == 0 || count > VANILLA_MAX)
		return;
	commitRemove(activePlayer(), (const u16*)&packet[2], count);
}

void recvHotkey(const u8* packet) {
	const u32 group = packet[2];
	if (group >= GROUP_COUNT)
		return;
	const u32 player = activePlayer();
	if (player >= PLAYERS)
		return;
	switch (packet[1]) {
		case 0: groupAssign(player, group, true); break;
		case 1: groupRecall(player, group); break;
		case 2: groupAssign(player, group, false); break;
	}
}

void recvSelectChunk(const u8* packet) {
	const u32 player = activePlayer();
	if (player >= PLAYERS)
		return;
	PendingPacket& p = pending[player];
	if (!chunkFeed(p, packet))
		return;
	switch (p.mode) {
		case CHUNK_REPLACE:	commitReplace(player, p.tags, p.count); break;
		case CHUNK_ADD:		commitAdd(player, p.tags, p.count); break;
		case CHUNK_REMOVE:	if (p.count > 0) commitRemove(player, p.tags, p.count); break;
	}
}

} //selsync
