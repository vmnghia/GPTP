//Manifold\buttonsets.bin, written by the Manifold Editor
//(docs/superpowers/specs/2026-10-05-button-set-editor-design.md §4). Pure: the
//host test tests/buttonsets_file_test.bat builds it on its own.
#pragma once
#include "../types.h"

namespace bsfile {

const u32 SET_COUNT = 250;
const u16 VERSION = 1;
const u32 MAX_POSITION = 15;
const u32 HEADER_BYTES = 8;
const u32 SET_HEADER_BYTES = 8;
const u32 BUTTON_BYTES = 20;	//GPTP's BUTTON

struct SetEntry {
	u32 buttonCount;
	u32 firstButton;	//byte offset of the set's first button in the file
	u32 connectedUnit;
};

//Checks the whole file before anything is changed. Returns NULL and fills
//entries[SET_COUNT] when it is good, else the reason (a static string).
//Condition and action addresses must lie in [codeStart, codeEnd).
const char* parse(const u8* data, u32 size, u32 codeStart, u32 codeEnd, SetEntry* entries);

} //bsfile
