#include "unit_morphs.h"
#include <cstddef>

namespace unitmorphs {

namespace {

//The game's ids (SCBW/enumerations/UnitId.h, TechId.h).
const u16 LARVA = 35, ZERGLING = 37, HYDRALISK = 38, MUTALISK = 43, DEVOURING_ONE = 54, LURKER = 103;
const u8 LURKER_ASPECT = 0x20, BANELING_TECH = 35;

//The morph table. A row makes its source morphable: while it morphs it is
//its egg, and it may become what the requirements allow (FireGraft's "current
//unit is"), or, with a techTarget, only that type, by its tech (rule). A
//morph button is Unit_Morph (0x423790) with the target's id, condition
//0x428E60 (Lurker: 0x429720).
const MorphRow ROWS[] = {
	{ LARVA,		EGG,		NONE,			0,				TECH_USE },
	{ HYDRALISK,	LURKER_EGG,	LURKER,			LURKER_ASPECT,	TECH_USE },
	{ MUTALISK,		COCOON,		NONE,			0,				TECH_USE },
	//The Baneling: Devouring One, once tech 35 (unused in vanilla) is researched.
	{ ZERGLING,		LURKER_EGG,	DEVOURING_ONE,	BANELING_TECH,	TECH_RESEARCHED },
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
	return row->rule == TECH_RESEARCHED ? CHECK_RESEARCHED : CHECK_TECH;
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
