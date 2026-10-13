//Smart rally's hooks (hooks/rally/smart_rally_hooks.h). The wrappers are
//naked: static locals only, never [ebp...]; tests/check_naked_wrappers.py
//checks this file's object too.
#include "smart_rally_hooks.h"
#include <hook_tools.h>

namespace {

//0x466F50: EAX = the new unit, ECX = the building; plain RET.
void __declspec(naked) orderNewUnitToRallyWrapper() {
	static CUnit* unit;
	static CUnit* factory;
	__asm {
		MOV unit, EAX
		MOV factory, ECX
		PUSHAD
	}
	smartrally::orderNewUnitToRally(unit, factory);
	__asm {
		POPAD
		RETN
	}
}

//0x4A076D in the unit's death (0x4A0740, EDI = the unit): its call of the
//reference clearing 0x49EB70 (1 already pushed; RET 4), which this stub
//ends in.
const u32 Func_ClearReferences = 0x0049EB70;
void __declspec(naked) unitDyingStub() {
	static CUnit* unit;
	__asm {
		MOV unit, EDI
		PUSHAD
	}
	smartrally::unitDying(unit);
	__asm {
		POPAD
		JMP Func_ClearReferences
	}
}

//0x49E63A in 0x49E590, which clears ESI's references to EBX (the dying
//unit): its call of isFactory (EAX = ESI), which this stub ends in. Every
//path of 0x49E590 passes here, just before a factory's rally is cleared.
const u32 Func_IsFactory = 0x0047B2E0;
void __declspec(naked) referenceClearStub() {
	static CUnit* holder;
	static CUnit* dying;
	__asm {
		MOV holder, ESI
		MOV dying, EBX
		PUSHAD
	}
	smartrally::referenceCleared(holder, dying);
	__asm {
		POPAD
		JMP Func_IsFactory
	}
}

} //unnamed namespace

namespace hooks {

void injectSmartRallyHooks() {
	jmpPatch(orderNewUnitToRallyWrapper,	0x00466F50, 0);
	callPatch(unitDyingStub,				0x004A076D, 0);
	callPatch(referenceClearStub,			0x0049E63A, 0);
}

} //hooks
