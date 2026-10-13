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

//0x45620D in the right-click receive (0x4560D0), RallyPoint1 for a factory:
//EDI = the building, EAX = the clicked unit. Vanilla sets the rally there,
//then jumps on to 0x456384.
const u32 Back_RightClickRally = 0x00456384;
void __declspec(naked) rightClickRallyStub() {
	static CUnit* building;
	static CUnit* target;
	__asm {
		MOV building, EDI
		MOV target, EAX
		PUSHAD
	}
	smartrally::rightClickRally(building, target);
	__asm {
		POPAD
		JMP Back_RightClickRally
	}
}

//0x4244A0, the rally button's action (fastcall: ECX = actVar, EDX = Shift),
//which never reads actVar. Its first two instructions (7 bytes) are redone
//here: MOV AL, 0x28; MOV [0x641691], AL.
const u32 Back_RallyButton = 0x004244A7;
void __declspec(naked) rallyButtonStub() {
	static u32 actVar;
	__asm {
		MOV actVar, ECX
		PUSHAD
	}
	smartrally::noteRallyButton(actVar);
	__asm {
		POPAD
		MOV AL, 0x28
		MOV BYTE PTR DS:[0x00641691], AL
		JMP Back_RallyButton
	}
}

//0x4C036C in 0x4C0300, which sends a click's targeted command 0x15: its call
//of the send (ECX = the command, EDX = 11), which this stub ends in. After
//Set Worker Rally Point, a rally click goes as the 7-byte 0x3E instead.
const u32 Func_QueueCommandSend = 0x00485BD0;
void __declspec(naked) targetedSendStub() {
	static u8* command;
	static u8* replaced;
	__asm {
		MOV command, ECX
		PUSHAD
	}
	replaced = smartrally::workerRallySendAs(command);
	__asm {
		POPAD
		CMP DWORD PTR replaced, 0
		JE sendIt
		MOV ECX, replaced
		MOV EDX, 7
	sendIt:
		JMP Func_QueueCommandSend
	}
}

} //unnamed namespace

namespace hooks {

void injectSmartRallyHooks() {
	jmpPatch(orderNewUnitToRallyWrapper,	0x00466F50, 0);
	callPatch(unitDyingStub,				0x004A076D, 0);
	callPatch(referenceClearStub,			0x0049E63A, 0);
	jmpPatch(rightClickRallyStub,			0x0045620D, 35);
	jmpPatch(rallyButtonStub,				0x004244A0, 2);
	callPatch(targetedSendStub,				0x004C036C, 0);
}

} //hooks
