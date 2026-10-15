#include "unit_morphs.h"
#include <cstddef>

namespace unitmorphs {

namespace {

//The game's ids (SCBW/enumerations/UnitId.h, TechId.h).
const u16 LARVA = 35, ZERGLING = 37, HYDRALISK = 38, MUTALISK = 43, DEVOURING_ONE = 54, LURKER = 103,
          MATURE_CHRYSALIS = 150;
const u16 LURKER_ASPECT = 0x20;

//The morph table. A row makes its source morphable: while it morphs it is
//its egg, and it may become what the requirements allow (FireGraft's "current
//unit is"), or, with a techTarget, only that type, with what it needs (rule).
//A morph button is Unit_Morph (0x423790), condition 0x428E60 (Lurker:
//0x429720), the target's id as both variables.
const MorphRow ROWS[] = {
	{ LARVA,		EGG,		NONE,			0,					NEED_TECH_USE },
	{ HYDRALISK,	LURKER_EGG,	LURKER,			LURKER_ASPECT,		NEED_TECH_USE },
	{ MUTALISK,		COCOON,		NONE,			0,					NEED_TECH_USE },
	//The Baneling: Devouring One, with a Mature Chrysalis (the future
	//Baneling Nest) completed.
	{ ZERGLING,		LURKER_EGG,	DEVOURING_ONE,	MATURE_CHRYSALIS,	NEED_COMPLETED },
};

} //unnamed namespace

const MorphRow* morphRowOf(u16 unitId) {
	for (u32 i = 0; i < sizeof(ROWS) / sizeof(ROWS[0]); i++)
		if (ROWS[i].source == unitId)
			return &ROWS[i];
	return NULL;
}

bool isMorphSource(u16 unitId) {
	return morphRowOf(unitId) != NULL;
}

u16 eggOf(u16 unitId) {
	const MorphRow* const row = morphRowOf(unitId);
	return row != NULL ? row->egg : NONE;
}

MorphCheck morphCheck(u16 unitId, u16 type) {
	const MorphRow* const row = morphRowOf(unitId);
	if (row == NULL)
		return CHECK_NONE;
	if (row->techTarget == NONE)
		return CHECK_MAKE;
	if (type != row->techTarget)
		return CHECK_NONE;
	return row->rule == NEED_COMPLETED ? CHECK_COMPLETED : CHECK_TECH;
}

s32 completedButtonState(bool hasNeeded) {
	return hasNeeded ? 1 : -1;
}

u16 cancelRevert(u16 eggId, u16 previousType) {
	if (eggId != COCOON && eggId != LURKER_EGG)
		return NONE;
	if (eggOf(previousType) == eggId)
		return previousType;
	return eggId == COCOON ? MUTALISK : HYDRALISK;
}

bool keptFromCancel(u16 unitId, u16 previousType) {
	const u16 egg = eggOf(unitId);
	if (egg != NONE && egg != EGG)
		return true;
	return previousType == COCOON || previousType == LURKER_EGG;
}

} //unitmorphs
