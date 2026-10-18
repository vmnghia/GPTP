#include "dat_load.h"
#include <SCBW/api.h>
#include <SCBW/Storm.h>
#include <DatExtend/datext.h>
#include <cstring>

//Ported from saintofidiocy/GPTP@DatExtender (0a4516b), without its units.dat
//extras (extended unit names and icons, the u16 flingy ids): this port
//extends images, sprites and flingy only (DatExtend/datext.h).

namespace {
void SysWarn_FileNotFound(u32 code, const char* filename);	//4212C0
} //unnamed namespace

namespace hooks {

//0x004D2E80: reads one .dat into its load table's arrays, property by
//property. For an extended file setDatSize first moves the arrays to ones
//big enough (and points the engine at them); a vanilla-size file loads as
//the game loads it.
void LoadGameData(DatLoad* datTable, const char* filename) {
	u32 size = 0;
	u32 pos = 0;
	u8* const data = (u8*)FastFileRead(filename, NULL, false, 0, &size);

	if (!DatExt::setDatSize(datTable, size))
		SysWarn_FileNotFound(24, filename); //invalid file, as the game warns

	for (u32 i = 0; datTable[i].address != 0; i++) {
		const u32 length = datTable[i].entries * datTable[i].length;
		if (length > size)
			break;
		memcpy((void*)datTable[i].address, data + pos, length);
		size -= length;
		pos += length;
	}

	if (size != 0)
		SysWarn_FileNotFound(24, filename);

	SMemFree(data, "Starcraft\\SWAR\\lang\\gamedata.cpp", 402, 0);
}

} //hooks

namespace {

const u32 Func__SysWarn_FileNotFound = 0x004212C0;
void SysWarn_FileNotFound(u32 code, const char* filename) {
	//As the branch has it (no PUSHAD: whether 0x4212C0 pops its argument is
	//unchecked, and POPAD doesn't restore ESP). A file error is fatal anyway.
	__asm {
		MOV EBX, code
		PUSH filename
		CALL Func__SysWarn_FileNotFound
	}
}

} //unnamed namespace
