//The morph table (docs/superpowers/specs/2026-10-15-smart-larva-design.md §3):
//which units morph, and through which egg. To make a unit morphable, add its
//row in unit_morphs.cpp. Pure, so the host test covers it
//(tests/unit_morphs_test.cpp); hooks/recv_commands/smart_morph.cpp reads it
//at the exe's morph sites.
#pragma once
#include "../types.h"

namespace unitmorphs {

const u16 NONE = 0xE4;		//UnitId::None
//The three egg units the exe knows (AI, console, birth, cancel): a morph
//uses one of them.
const u16 EGG = 36, COCOON = 59, LURKER_EGG = 97;

//How a row's tech allows its techTarget: the game's can-use test (0x46DD80:
//the tech's use requirements in the data, as the Lurker), or the tech
//researched by the unit's owner (a tech the data has no use requirements
//for, which the can-use test always refuses).
enum TechRule { TECH_USE, TECH_RESEARCHED };

struct MorphRow {
	u16 source;		//the unit that morphs
	u16 egg;		//EGG, COCOON or LURKER_EGG
	u16 techTarget;	//NONE: any type the requirements allow (canMakeUnit)
	u8 tech;		//with a techTarget: the tech that allows it
	TechRule rule;	//with a techTarget: how
};

//The row of a unit that morphs, or NULL.
const MorphRow* morphRowOf(u16 unitId);
bool isMorphSource(u16 unitId);
//The egg a unit is while it morphs; NONE if it doesn't morph.
u16 eggOf(u16 unitId);

enum MorphCheck { CHECK_NONE, CHECK_MAKE, CHECK_TECH, CHECK_RESEARCHED };
//How to test whether a unit may morph into a type: it can't, the
//requirements decide (canMakeUnit), the row's tech's can-use test
//(canUseTech), or the row's tech researched.
MorphCheck morphCheck(u16 unitId, u16 type);

//What a cancelled egg becomes (cancel, 0x468351): the unit it was
//(previousType) when that unit morphs through this egg, else vanilla's
//(Cocoon: Mutalisk; Lurker Egg: Hydralisk). NONE for an egg that is removed
//(a larva's Egg) and for any other unit.
u16 cancelRevert(u16 eggId, u16 previousType);
//Whether cancel must leave an unfinished unit alone (0x4682E5): a source
//back from its egg, or a unit hatching from a Cocoon or Lurker Egg. Vanilla
//names Mutalisk and Hydralisk; without this a Zergling back from its egg, or
//a Baneling hatching, would be refunded a second time.
bool keptFromCancel(u16 unitId, u16 previousType);

} //unitmorphs
