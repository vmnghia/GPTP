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

//What a row's techTarget needs: a tech, by the game's can-use test (0x46DD80:
//the tech's use requirements in the data, as the Lurker), or a completed unit
//of the owner's (a building, as SC2's Baneling Nest), by the game's count of
//completed units (0x584DE4, which the requirements' "must have" reads).
enum NeedRule { NEED_TECH_USE, NEED_COMPLETED };

struct MorphRow {
	u16 source;		//the unit that morphs
	u16 egg;		//EGG, COCOON or LURKER_EGG
	u16 techTarget;	//NONE: any type the requirements allow (canMakeUnit)
	u16 need;		//with a techTarget: the tech or unit type it needs
	NeedRule rule;	//with a techTarget: which of the two need is
};

//The row of a unit that morphs, or NULL.
const MorphRow* morphRowOf(u16 unitId);
bool isMorphSource(u16 unitId);
//The egg a unit is while it morphs; NONE if it doesn't morph.
u16 eggOf(u16 unitId);

enum MorphCheck { CHECK_NONE, CHECK_MAKE, CHECK_TECH, CHECK_COMPLETED };
//How to test whether a unit may morph into a type: it can't, the
//requirements decide (canMakeUnit), the row's tech's can-use test
//(canUseTech), or the owner has a completed unit of the row's need.
MorphCheck morphCheck(u16 unitId, u16 type);
//The morph button's state for a CHECK_COMPLETED row (the button condition's
//values: 1 enabled, -1 disabled): disabled while the needed unit is missing,
//as a unit button whose building is missing.
s32 completedButtonState(bool hasNeeded);

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
//Whether a unit whose type makes two per egg (Two Units in One Egg) gets its
//twin at birth (0x45DDA9): only when it hatches from a larva's Egg, as in
//vanilla. A Zergling back from a cancelled Lurker Egg runs the same birth;
//the twin's placement reads the egg image's overlay, which the Lurker Egg
//lacks (a crash at 0x498277), and it would be a free unit besides.
bool hatchesTwin(u16 previousType);

} //unitmorphs
