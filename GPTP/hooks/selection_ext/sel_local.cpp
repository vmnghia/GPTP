#include "sel_local.h"
#include "sel_exe.h"
#include "sel_send.h"
#include "sel_subgroups.h"
#include "sel_build.h"
#include "sel_panel.h"
#include "sel_profile.h"
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
const u8* const	SHIFT_HELD				= (u8*)		0x00596A28;
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

//Free lists of the two image pools: the exe takes images from the head.
void** const CIRCLE_POOL_HEAD		= (void**)	0x0052F564;	//80 images at 0x57D768
void** const CIRCLE_POOL_TAIL		= (void**)	0x0052E4C0;
void** const HEALTH_BAR_POOL_HEAD	= (void**)	0x005254B8;	//12 images at 0x57EB78
void** const HEALTH_BAR_POOL_TAIL	= (void**)	0x0057EB6C;
//Circles: the local selection, and every other player's dashed ally circles.
CImage extraCircles[SEL_MAX * PLAYERS];
CImage extraHealthBars[SEL_MAX];
//Each health-bar image draws from its own 14-byte GRP frame (image+0x2C),
//which 0x4D6010 fills per unit; vanilla's 12 are at 0x51F200.
const u32 HEALTH_BAR_FRAME_BYTES = 14;
u8 extraHealthBarFrames[SEL_MAX][HEALTH_BAR_FRAME_BYTES];

//The unit each sprite's circle was last made for, by sprite index: past slot
//255 the sprite's byte can't say which unit a health bar belongs to.
CUnit* spriteUnit[SPRITE_ARRAY_LENGTH];

u32 spriteIndexOf(const CSprite* sprite) {
	return (u32)(sprite - spriteTable);
}

//Records the unit before its circle (and health bar) is made.
void createCircle(CUnit* unit, u32 slot) {
	const u32 index = spriteIndexOf(unit->sprite);
	if (index < (u32)SPRITE_ARRAY_LENGTH)
		spriteUnit[index] = unit;
	selexe::createSelectionCircle(unit, slot);
}

//buildActive's scratch, by unit index (1-based): a unit's circle slot + 1 in
//the old selection, and whether the new one keeps it there. Zero between calls.
u16 oldSlotPlusOne[UNIT_ARRAY_LENGTH + 1];
bool keepsCircle[UNIT_ARRAY_LENGTH + 1];

//Whether the unit's sprite still has the circle createCircle made for it: a
//sprite replaced (a morph) or deselected by the exe since then gets a new one.
bool hasOurCircle(CUnit* unit) {
	CSprite* const sprite = unit->sprite;
	if (sprite == NULL || !(sprite->flags & CSprite_Flags::Selected))
		return false;
	const u32 index = spriteIndexOf(sprite);
	return index < (u32)SPRITE_ARRAY_LENGTH && spriteUnit[index] == unit;
}

u32 unitIndexOf(CUnit* unit) {
	const u32 index = unit->getIndex();
	return index <= (u32)UNIT_ARRAY_LENGTH ? index : 0;
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

void growImagePools() {
	//A new game: every image of the last one is gone, so these are all free.
	memset(extraCircles, 0, sizeof(extraCircles));
	memset(extraHealthBars, 0, sizeof(extraHealthBars));
	memset(extraHealthBarFrames, 0, sizeof(extraHealthBarFrames));
	memset(spriteUnit, 0, sizeof(spriteUnit));
	//As the pool init does for vanilla's 12 (0x4D68C0), then each gets its own frame.
	for (u32 i = 0; i < SEL_MAX; i++) {
		selexe::initHealthBarImage(&extraHealthBars[i]);
		u8** const frame = (u8**)((u8*)&extraHealthBars[i] + 0x2C);
		*frame = extraHealthBarFrames[i];
		*(u16*)extraHealthBarFrames[i] = 1;
	}
	freeListAppend(CIRCLE_POOL_HEAD, CIRCLE_POOL_TAIL, extraCircles,
		sizeof(extraCircles) / sizeof(CImage), sizeof(CImage));
	freeListAppend(HEALTH_BAR_POOL_HEAD, HEALTH_BAR_POOL_TAIL, extraHealthBars,
		sizeof(extraHealthBars) / sizeof(CImage), sizeof(CImage));
}

CUnit* unitForHealthBar(CSprite* sprite, u32 slot) {
	SEL_PROFILE_COUNT(HEALTH_BAR);
	//Below the byte's cap the slot is exact, as in vanilla.
	if (slot < 255 && slot < SEL_MAX && activeSel[slot] != NULL)
		return activeSel[slot];
	//Past it, the unit its circle was made for.
	const u32 index = spriteIndexOf(sprite);
	if (index < (u32)SPRITE_ARRAY_LENGTH && spriteUnit[index] != NULL && spriteUnit[index]->sprite == sprite)
		return spriteUnit[index];
	SEL_PROFILE_COUNT(HEALTH_BAR_SCAN);
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++)
		if (activeSel[i]->sprite == sprite)
			return activeSel[i];
	CUnit* const* const vanillaActive = (CUnit* const*)0x006284B8;
	return slot < VANILLA_MAX ? vanillaActive[slot] : activeSel[0];
}

//Only the circles that change are made again: a unit whose circle slot is
//the same in both selections keeps its circle. A death or a Shift-removal
//moves only the units after it, and past slot 255 (the byte's cap) none.
void buildActive(CUnit** list, u32 count) {
	SEL_PROFILE_SCOPE(BUILD_ACTIVE);
	if (count > SEL_MAX)
		count = SEL_MAX;
	static CUnit* old[SEL_MAX];
	const u32 oldCount = listCount(activeSel, SEL_MAX);
	memcpy(old, activeSel, oldCount * sizeof(CUnit*));
	for (u32 i = 0; i < oldCount; i++)
		oldSlotPlusOne[unitIndexOf(old[i])] = (u16)(circleSlot(i) + 1);
	for (u32 i = 0; i < count; i++) {
		list[i] = parentOf(list[i]);
		const u32 index = unitIndexOf(list[i]);
		keepsCircle[index] = index != 0 && oldSlotPlusOne[index] == circleSlot(i) + 1
			&& hasOurCircle(list[i]);
	}

	for (u32 i = 0; i < oldCount; i++) {
		activeSel[i] = NULL;
		if (!keepsCircle[unitIndexOf(old[i])])
			selexe::removeSelectionCircle(old[i]);
	}
	mirrorActive();
	CUnit** const vanillaActive = (CUnit**)0x006284B8;
	for (u32 i = 0; i < count; i++) {
		CUnit* const unit = list[i];
		activeSel[i] = unit;
		//Vanilla fills each slot before its circle is made; keep the mirror so.
		if (i < VANILLA_MAX)
			vanillaActive[i] = unit;
		if (!keepsCircle[unitIndexOf(unit)])
			createCircle(unit, circleSlot(i));
	}
	mirrorActive();

	for (u32 i = 0; i < oldCount; i++)
		oldSlotPlusOne[unitIndexOf(old[i])] = 0;
	for (u32 i = 0; i < count; i++)
		keepsCircle[unitIndexOf(list[i])] = false;
	oldSlotPlusOne[0] = 0;
	keepsCircle[0] = false;
}

void buildActiveNewSelection(CUnit** list, u32 count) {
	selectionPage = 0;
	//Shift-click and shift-drag come here too: they add, and keep the active
	//subgroup (stage 5); anything else is a fresh selection.
	if (!*SHIFT_HELD)
		selsub::markFresh();
	buildActive(list, count);
}

void localRemove(CUnit* unit) {
	SEL_PROFILE_SCOPE(LOCAL_REMOVE);
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
	SEL_PROFILE_SCOPE(CIRCLES);
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++) {
		CUnit* unit = parentOf(activeSel[i]);
		activeSel[i] = unit;
		createCircle(unit, circleSlot(i));
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
	SEL_PROFILE_SCOPE(CLIENT_COPY);
	memcpy(clientSel, activeSel, sizeof(clientSel));
	clientCount = listCount(clientSel, SEL_MAX);
	//Stage 5: the console list is sorted by subgroup, and the portrait (whose
	//button set is the card) is the active subgroup's leader.
	*activePortraitUnit = selsub::sortAndPickLeader(clientCount);
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
	selsub::reset();
	selbuild::reset();
	selpanel::reset();
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
