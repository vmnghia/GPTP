#include "dat_load.h"
#include <hook_tools.h>

//Ported from saintofidiocy/GPTP@DatExtender (0a4516b), the loader only (its
//GetUnitName reroute serves extended unit names, which this port leaves out).

namespace {

//0x004D2E80 takes the load table in EAX and the file name on the stack, and
//pops it (RETN 4).
void __declspec(naked) LoadGameData_Wrapper() {
	static DatLoad* datTable;
	static char* filename;

	__asm {
		MOV datTable, EAX
		MOV ECX, [ESP+4]
		MOV filename, ECX
		PUSHAD
	}
	hooks::LoadGameData(datTable, filename);
	__asm {
		POPAD
		RETN 4
	}
}

} //unnamed namespace

namespace hooks {

void injectDatLoadHooks() {
	jmpPatch(LoadGameData_Wrapper, 0x004D2E80);	//the .dat loader, with the extender's resize first
}

} //hooks
