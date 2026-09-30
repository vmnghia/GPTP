#include "sel_local.h"
#include "sel_exe.h"
#include "sel_send.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u8* const	REBUILD_CLIENT_SELECTION	= (u8*)		0x0059723C;
u32* const	REFRESH_BUTTON_SET			= (u32*)	0x0068C1B0;
u8* const	REFRESH_PORTRAIT			= (u8*)		0x0068AC74;
u8* const	REFRESH_STAT_DATA			= (u8*)		0x0068C1F8;
u32* const	HOVER_DIALOG				= (u32*)	0x0068C1E8;
u32* const	HOVER_DIALOG_USER			= (u32*)	0x0068C1EC;
const u32* const PLACING_BUILDING		= (u32*)	0x00640880;
const u8* const	TARGETING				= (u8*)		0x00641694;
const u8* const	LOCAL_VISIBILITY		= (u8*)		0x0057F0B0;
const u8* const	ALT_HELD				= (u8*)		0x00596A2A;
const u8* const	IS_MULTIPLAYER			= (u8*)		0x0057F0B4;
const u8* const	IS_TEAM_GAME			= (u8*)		0x00596875;
const u8* const	PLAYER_FORCES			= (u8*)		0x0057EEEA;	//+ player * 0x24

//CSprite::selectionIndex is a byte.
u32 circleSlot(u32 slot) {
	return slot < 255 ? slot : 255;
}

CUnit* parentOf(CUnit* unit) {
	if (units_dat::BaseProperty[unit->id] & UnitProperty::Subunit)
		return unit->subunit;
	return unit;
}

bool isDying(const CUnit* unit) {
	return unit->mainOrderId == OrderId::Die && unit->mainOrderState == 1;
}

} //unnamed namespace

namespace sellocal {

void requestRefresh() {
	*HOVER_DIALOG = 0;
	*HOVER_DIALOG_USER = 0;
	*REBUILD_CLIENT_SELECTION = 1;
	*REFRESH_BUTTON_SET = 1;
	*REFRESH_PORTRAIT = 1;
	*REFRESH_STAT_DATA = 1;
}

CUnit* unitForHealthBar(CSprite* sprite, u32 slot) {
	//Below the byte's cap the slot is exact, as in vanilla.
	if (slot < 255 && slot < SEL_MAX && activeSel[slot] != NULL)
		return activeSel[slot];
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++)
		if (activeSel[i]->sprite == sprite)
			return activeSel[i];
	CUnit* const* const vanillaActive = (CUnit* const*)0x006284B8;
	return slot < VANILLA_MAX ? vanillaActive[slot] : activeSel[0];
}

void buildActive(CUnit** list, u32 count) {
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++) {
		CUnit* unit = activeSel[i];
		activeSel[i] = NULL;
		selexe::removeSelectionCircle(parentOf(unit));
	}
	mirrorActive();
	if (count > SEL_MAX)
		count = SEL_MAX;
	CUnit** const vanillaActive = (CUnit**)0x006284B8;
	for (u32 i = 0; i < count; i++) {
		CUnit* unit = parentOf(list[i]);
		activeSel[i] = unit;
		//Vanilla fills each slot before its circle is made; keep the mirror so.
		if (i < VANILLA_MAX)
			vanillaActive[i] = unit;
		selexe::createSelectionCircle(unit, circleSlot(i));
		list[i] = unit;
	}
	mirrorActive();
}

void localRemove(CUnit* unit) {
	if (!(unit->sprite->flags & CSprite_Flags::Selected))
		return;
	CUnit* list[SEL_MAX];
	u32 n = listCount(activeSel, SEL_MAX);
	memcpy(list, activeSel, n * sizeof(CUnit*));
	if (listFind(list, n, unit) < 0)
		return;
	n = listRemove(list, n, unit);
	buildActive(list, n);
	requestRefresh();
	if (n == 0) {
		if (*PLACING_BUILDING != 0)
			selexe::cancelPlacement();
		if (*TARGETING != 0)
			selexe::cancelTargetOrder();
	}
}

void redrawCircles() {
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++) {
		CUnit* unit = parentOf(activeSel[i]);
		activeSel[i] = unit;
		selexe::createSelectionCircle(unit, circleSlot(i));
	}
	mirrorActive();
	const u32 local = (u32)*LOCAL_HUMAN_ID;
	if (*IS_MULTIPLAYER == 0 || *IS_TEAM_GAME == 0 || local >= PLAYERS)
		return;
	for (int p = PLAYERS - 1; p >= 0; p--) {
		if ((u32)p == local || PLAYER_FORCES[p * 0x24] != PLAYER_FORCES[local * 0x24])
			continue;
		for (u32 j = 0; j < SEL_MAX && playersSel[p][j] != NULL; j++)
			selexe::addDashedCircle(playersSel[p][j]);
	}
}

void clientCopy() {
	memcpy(clientSel, activeSel, sizeof(clientSel));
	clientCount = 0;
	*activePortraitUnit = NULL;
	CUnit* best = NULL;
	for (u32 i = 0; i < SEL_MAX; i++) {
		CUnit* unit = clientSel[i];
		if (unit == NULL)
			continue;
		if (selexe::outranks(unit, best))
			best = unit;
		clientCount++;
	}
	*activePortraitUnit = best;
	if (clientCount == 1) {
		listClear(clientSel, SEL_MAX);
		clientSel[0] = best;
	}
	mirrorClient();
}

void deselectAndSend(CUnit* unit) {
	CUnit* list[SEL_MAX];
	u32 n = 0;
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++)
		if (activeSel[i] != unit)
			list[n++] = activeSel[i];
	buildActive(list, n);
	selsend::cmdactSelect(n, list);
	requestRefresh();
}

void reselectAtStart() {
	CUnit* list[SEL_MAX];
	u32 n = 0;
	for (u32 i = 0; i < SEL_MAX && reselect[i] != NULL; i++) {
		CUnit* unit = reselect[i];
		if (unit->sprite == NULL || isDying(unit))
			continue;
		if (!(unit->sprite->visibilityFlags & *LOCAL_VISIBILITY))
			continue;
		list[n++] = unit;
	}
	if (n != 0) {
		bool done = false;
		if (n == 1 && *ALT_HELD != 0)
			done = selexe::selectRecentGroupOf(tagOf(list[0]));
		if (!done) {
			buildActive(list, n);
			selsend::cmdactSelect(n, list);
			*REBUILD_CLIENT_SELECTION = 1;
		}
	}
	*REFRESH_BUTTON_SET = 1;
	*REFRESH_PORTRAIT = 1;
	*REFRESH_STAT_DATA = 1;
	*HOVER_DIALOG = 0;
	*HOVER_DIALOG_USER = 0;
}

void gameStartClear() {
	clearAll();
}

void gameStartKeepLocal() {
	listClear(reselect, SEL_MAX);
	const u32 local = (u32)*LOCAL_HUMAN_ID;
	if (local < PLAYERS)
		memcpy(reselect, playersSel[local], sizeof(reselect));
	for (u32 p = 0; p < PLAYERS; p++) {
		listClear(playersSel[p], SEL_MAX);
		mirrorPlayer(p);
	}
	memset(pending, 0, sizeof(pending));
}

void addTwin(CUnit* twin) {
	CUnit* list[SEL_MAX];
	const u32 n = listCount(activeSel, SEL_MAX);
	if (n >= SEL_MAX)
		return;
	memcpy(list, activeSel, n * sizeof(CUnit*));
	list[n] = twin;
	buildActive(list, n + 1);
	selsend::cmdactSelect(n + 1, list);
	requestRefresh();
}

} //sellocal
