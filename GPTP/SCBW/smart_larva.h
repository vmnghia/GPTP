//Smart larva morphing (docs/superpowers/specs/2026-10-15-smart-larva-design.md):
//a plain press of a morph button morphs one selected unit, Shift+press all.
//Pure, so the host test covers it (tests/unit_morphs_test.cpp);
//hooks/recv_commands/smart_morph.cpp applies it.
#pragma once
#include "../types.h"

namespace smartlarva {

//The game's morph command, [0x23][u16 type]: every selected unit that can.
const u8 CMD_MORPH_ALL = 0x23;
//Morph one, [0x3F][u16 type]. Beside the plugin's 0x3C, 0x3D (SCBW/
//selection_ext.h) and 0x3E (SCBW/smart_rally.h).
const u8 CMD_MORPH_ONE = 0x3F;
const u32 MORPH_ONE_BYTES = 3;

//The command a morph button's press sends: with Shift the game's (all), else
//one.
u8 morphCommandId(bool shiftHeld);

//Which candidate a plain press morphs. keys[i] is candidate i's group, in
//selection order: a larva's Hatchery, else the unit itself. The first unit of
//the largest group; ties go to the group met first. n when there is none.
u32 pickMorpher(const u32* keys, u32 n);

} //smartlarva
