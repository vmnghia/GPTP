//Smart larva morphing and the morph table in the game
//(docs/superpowers/specs/2026-10-15-smart-larva-design.md). Synced: it runs
//on every client from the command, except morphSendAs, which only picks the
//command the local player sends. The rules: SCBW/unit_morphs.h,
//SCBW/smart_larva.h.
#pragma once
#include <SCBW/api.h>

namespace smartmorph {

//0x423846: a morph button's command before it is sent, 0x3F without Shift.
void morphSendAs(u8* command);
//0x4C19BB in the game's morph receive (0x4C1990): whether a selected unit
//morphs into the command's type. While 0x3F runs, only its pick does.
u32 morphAllowed(CUnit* unit, const u8* command);
//Command 0x3F: one unit morphs, the pick (SCBW/smart_larva.h), through the
//game's receive.
void recvMorphOne(const u8* packet);
//Morph1 (0x45DFB0): whether the unit morphs at all.
u32 morphsAtAll(CUnit* unit);
//Morph1 (0x45E019): its egg, unitmorphs::NONE if none.
u32 eggForUnit(CUnit* unit);
//Cancel (0x4682E5): whether an unfinished unit is left alone.
u32 keptFromCancelUnit(CUnit* unit);
//Cancel (0x468351): what a cancelled egg becomes, unitmorphs::NONE for the
//game's own path (removed, or not an egg).
u32 cancelRevertOf(CUnit* unit);
//The button gate 0x428E60 with several units selected (0x428E89): a worker
//(smart-build) or a morph source shows its buttons.
u32 mayCreateWithSeveral(CUnit* unit);
//The button condition 0x428E60's last test (its call of canMakeUnit at
//0x428E92): for a morph table row that needs a completed unit (Zergling to
//Devouring One: a Mature Chrysalis), 1 (enabled) with one, else -1
//(disabled, as a unit button whose building is missing); 0 (hidden) for
//another player's unit or an unfinished one; BUTTON_NOT_OURS for anything
//else, which canMakeUnit decides as before.
const u32 BUTTON_NOT_OURS = 2;
u32 morphButtonState(CUnit* unit, u16 type, u32 playerId);
//The type change 0x49FED0 (at 0x49FF82): a unit the local player has
//selected changed type (began or ended a morph), so the console list is
//sorted again and the card's unit picked again. The active subgroup stays
//while it has units (selext::activeKeyAfter): with larvae selected, morphing
//one leaves the card on Larva instead of the new Egg. Local only.
void selectedTypeChanged(CUnit* unit);

} //smartmorph

namespace hooks {

void injectSmartMorphHooks();

} //hooks
