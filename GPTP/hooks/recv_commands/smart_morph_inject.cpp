//Smart larva morphing's hooks (hooks/recv_commands/smart_morph.h). The stubs
//are naked: static locals only, never [ebp...]; tests/check_naked_wrappers.py
//checks this file's object too. Addresses: "Exe facts" in
//docs/superpowers/plans/2026-10-15-smart-larva.md.
#include "smart_morph.h"
#include <hook_tools.h>

namespace {

//0x423846 in the morph button's action 0x423790: its call of the send
//(ECX = [0x23][u16 type], EDX = 3), which this stub ends in.
const u32 Func_QueueCommandSend = 0x00485BD0;
void __declspec(naked) morphSendStub() {
	static u8* command;
	__asm {
		MOV command, ECX
		PUSHAD
	}
	smartmorph::morphSendAs(command);
	__asm {
		POPAD
		JMP Func_QueueCommandSend
	}
}

//0x4C19BB in the morph receive 0x4C1990: ESI = the unit, EBX = the command.
//Allowed goes on at 0x4C19FD (not already Morph1, pay, order); refused at
//0x4C1A74 (the next unit).
const u32 Recv_MorphAllowed = 0x004C19FD;
const u32 Recv_MorphRefused = 0x004C1A74;
void __declspec(naked) morphAllowedStub() {
	static CUnit* unit;
	static const u8* command;
	static u32 allowed;
	__asm {
		MOV unit, ESI
		MOV command, EBX
		PUSHAD
	}
	allowed = smartmorph::morphAllowed(unit, command);
	__asm {
		POPAD
		CMP allowed, 0
		JE refused
		JMP Recv_MorphAllowed
	refused:
		JMP Recv_MorphRefused
	}
}

//0x45DFB0, Morph1's first state (ESI = the unit): a unit that morphs goes on
//at 0x45DFCA, else Morph1 returns (0x45E07C).
const u32 Morph1_Goes = 0x0045DFCA;
const u32 Morph1_Return = 0x0045E07C;
void __declspec(naked) morph1SourceStub() {
	static CUnit* unit;
	static u32 morphs;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	morphs = smartmorph::morphsAtAll(unit);
	__asm {
		POPAD
		CMP morphs, 0
		JE stop
		JMP Morph1_Goes
	stop:
		JMP Morph1_Return
	}
}

//0x45E019, Morph1's egg choice: clears status bit 1 (not completed), pushes
//the egg and goes on at 0x45E048 (the type change 0x49FED0).
const u32 Morph1_ToEgg = 0x0045E048;
void __declspec(naked) morph1EggStub() {
	static CUnit* unit;
	static u32 egg;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	egg = smartmorph::eggForUnit(unit);
	__asm {
		POPAD
		AND DWORD PTR [ESI+0xDC], 0xFFFFFFFE
		CMP egg, 0xE4
		JE stop
		PUSH egg
		JMP Morph1_ToEgg
	stop:
		JMP Morph1_Return
	}
}

//0x4682E5 in cancel 0x468280 (ESI = an unfinished unit; AL = its status, DX
//= its id, both read on at 0x4682F9): a unit kept from cancel returns
//(0x468418).
const u32 Cancel_GoesOn = 0x004682F9;
const u32 Cancel_Return = 0x00468418;
void __declspec(naked) cancelKeptStub() {
	static CUnit* unit;
	static u32 kept;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	kept = smartmorph::keptFromCancelUnit(unit);
	__asm {
		POPAD
		CMP kept, 0
		JE goesOn
		JMP Cancel_Return
	goesOn:
		JMP Cancel_GoesOn
	}
}

//0x468351 in cancel, after the refund: an egg that reverts pushes the unit
//it becomes and goes on at 0x46839E (the type change); anything else goes on
//at 0x468361 with AX = its id.
const u32 Cancel_Revert = 0x0046839E;
const u32 Cancel_NotReverted = 0x00468361;
void __declspec(naked) cancelRevertStub() {
	static CUnit* unit;
	static u32 revert;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	revert = smartmorph::cancelRevertOf(unit);
	__asm {
		POPAD
		CMP revert, 0xE4
		JE notReverted
		PUSH revert
		JMP Cancel_Revert
	notReverted:
		MOV AX, WORD PTR [ESI+0x64]
		JMP Cancel_NotReverted
	}
}

} //unnamed namespace

namespace hooks {

void injectSmartMorphHooks() {
	callPatch(morphSendStub,		0x00423846, 0);
	jmpPatch(morphAllowedStub,		0x004C19BB, 0);
	jmpPatch(morph1SourceStub,		0x0045DFB0, 0);
	jmpPatch(morph1EggStub,			0x0045E019, 0);
	jmpPatch(cancelKeptStub,		0x004682E5, 0);
	jmpPatch(cancelRevertStub,		0x00468351, 0);
}

} //hooks
