#include "smart_morph.h"
#include "../selection_ext/sel_synced.h"
#include "../selection_ext/sel_local.h"
#include <SCBW/unit_morphs.h>
#include <SCBW/smart_larva.h>
#include <SCBW/selection_ext.h>

namespace smartmorph {

namespace {

//The one unit 0x3F lets through the game's receive; NULL: every unit (0x23).
CUnit* onlyUnit;

//The game's count of each player's completed units, u32 [type][12 players]:
//what the requirements' "must have" reads (0x46D685).
const u32* const COMPLETED_UNITS = (const u32*)0x00584DE4;

//Whether the unit is the player's, finished (the game's tests ask both).
bool ownFinished(CUnit* unit, u32 playerId) {
	return unit->playerId == playerId && playerId < (u32)PLAYER_COUNT && (unit->status & UnitStatus::Completed);
}

//Whether the player has a completed unit of the type a NEED_COMPLETED row
//needs.
bool hasNeededUnit(CUnit* unit, u32 playerId) {
	return COMPLETED_UNITS[unitmorphs::morphRowOf(unit->id)->need * PLAYER_COUNT + playerId] != 0;
}

bool canMorphInto(CUnit* unit, u16 type) {
	switch (unitmorphs::morphCheck(unit->id, type)) {
	case unitmorphs::CHECK_MAKE:
		return unit->canMakeUnit(type, *ACTIVE_NATION_ID) == 1;
	case unitmorphs::CHECK_TECH:
		return unit->canUseTech((u8)unitmorphs::morphRowOf(unit->id)->need, *ACTIVE_NATION_ID) == 1;
	case unitmorphs::CHECK_COMPLETED:
		return ownFinished(unit, *ACTIVE_NATION_ID) && hasNeededUnit(unit, *ACTIVE_NATION_ID);
	default:
		return false;
	}
}

//A larva's group is its Hatchery; a unit without one is its own. Compared
//for equality only, so the same on every client.
u32 groupKey(CUnit* unit) {
	if (unit->id == UnitId::ZergLarva && unit->connectedUnit != NULL)
		return (u32)unit->connectedUnit;
	return (u32)unit;
}

//The game's morph receive, stdcall(command), RET 4.
const u32 Func_RecvUnitMorph = 0x004C1990;
void recvUnitMorph(const u8* packet) {
	static const u8* command;
	command = packet;
	__asm {
		PUSHAD
		PUSH command
		CALL Func_RecvUnitMorph
		POPAD
	}
}

CUnit* candidates[selext::SEL_MAX];
u32 keys[selext::SEL_MAX];

} //unnamed namespace

void morphSendAs(u8* command) {
	command[0] = smartlarva::morphCommandId(*IS_HOLDING_SHIFT != 0);
}

u32 morphAllowed(CUnit* unit, const u8* command) {
	if (onlyUnit != NULL && unit != onlyUnit)
		return 0;
	return canMorphInto(unit, *(const u16*)(command + 1)) ? 1 : 0;
}

void recvMorphOne(const u8* packet) {
	const u16 type = *(const u16*)(packet + 1);
	u32 n = 0;
	*selectionIndexStart = 0;
	for (CUnit* unit = selsync::nextSelected(); unit != NULL && n < selext::SEL_MAX; unit = selsync::nextSelected()) {
		if (unit->mainOrderId != OrderId::Morph1 && canMorphInto(unit, type)) {
			candidates[n] = unit;
			keys[n] = groupKey(unit);
			n++;
		}
	}
	const u32 pick = smartlarva::pickMorpher(keys, n);
	if (pick >= n)
		return;
	onlyUnit = candidates[pick];
	recvUnitMorph(packet);
	onlyUnit = NULL;
}

u32 morphsAtAll(CUnit* unit) {
	return unitmorphs::isMorphSource(unit->id) ? 1 : 0;
}

u32 eggForUnit(CUnit* unit) {
	return unitmorphs::eggOf(unit->id);
}

u32 keptFromCancelUnit(CUnit* unit) {
	return unitmorphs::keptFromCancel(unit->id, unit->previousUnitType) ? 1 : 0;
}

u32 cancelRevertOf(CUnit* unit) {
	return unitmorphs::cancelRevert(unit->id, unit->previousUnitType);
}

u32 mayCreateWithSeveral(CUnit* unit) {
	if (units_dat::BaseProperty[unit->id] & UnitProperty::Worker)
		return 1;
	return unitmorphs::isMorphSource(unit->id) ? 1 : 0;
}

u32 morphButtonState(CUnit* unit, u16 type, u32 playerId) {
	if (unitmorphs::morphCheck(unit->id, type) != unitmorphs::CHECK_COMPLETED)
		return BUTTON_NOT_OURS;
	if (!ownFinished(unit, playerId))
		return 0;
	return (u32)unitmorphs::completedButtonState(hasNeededUnit(unit, playerId));
}

void selectedTypeChanged(CUnit* unit) {
	if (unit->sprite != NULL && (unit->sprite->flags & CSprite_Flags::Selected))
		sellocal::requestRefresh();
}

} //smartmorph
