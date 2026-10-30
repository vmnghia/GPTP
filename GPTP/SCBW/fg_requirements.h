//FireGraft's Dat Requirements, read from the FireGraft project (.fgp) in the
//mod exe's MPQ, so the plugin can apply them itself: FireGraft's own runtime
//never runs in Manifold (GPTP's campaign fix bypasses its hook; spec
//docs/superpowers/specs/2026-10-29-fg-requirements-design.md). Pure: the host
//test tests/fg_requirements_test.bat builds it on its own.
#pragma once
#include "../types.h"
#include <string>
#include <vector>

namespace fgreq {

//The five tables, in the game's order: units, upgrades, tech research, tech
//use, orders. Each section is u16 count, then per record u8 id, u8 n, n words
//(the last 0xFFFF; an upgrade's levels hold inner 0xFFFFs too), u8 used.
const u32 TABLE_COUNT = 5;
extern const char* const SECTION_TAGS[TABLE_COUNT];

//The .fgp's section with this four-letter tag: NULL and the reason in *error
//when the file is bad, NULL and no error when the section is missing.
const u8* findSection(const u8* fgp, u32 size, const char* tag, u32* length, const char** error);

//The game's table from one section: word 0, then each used record's id and
//words, then 0xFFFF (as FireGraft builds it, 0x10008910 in its runtime).
//NULL, or the reason the section is bad (then the table is left empty).
const char* buildTable(const u8* section, u32 length, std::vector<u16>& table);

//The .fgp named in Firegraft\datalist.txt (one MPQ path per line), or "".
std::string fgpName(const char* text, u32 size);

} //fgreq
