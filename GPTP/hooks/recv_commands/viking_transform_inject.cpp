#include "viking_transform.h"
#include <SCBW/viking_transform.h>
#include <hook_tools.h>

namespace {

//0x423180, BTNSACT_DoNothing: a bare RET that only the vanilla button table
//points at (sc_disasm callers: none). A button action gets its actVar in CX
//and the Shift (queued) byte in DL: the caller (0x459918) loads only CX
//(`mov cx, word [esi+0xE]`), so ECX's upper half is whatever was there before
//(the first build compared all of ECX and never matched, 2026-10-24).
void __declspec(naked) actionStub() {
	static u32 actVar;
	static u32 queued;
	__asm {
		MOVZX ECX, CX
		MOV actVar, ECX
		MOVZX EDX, DL
		MOV queued, EDX
		PUSHAD
	}
	vikingtransform::buttonPressed(actVar, (u8)queued);
	__asm {
		POPAD
		RETN
	}
}

//0x4EC55D in the per-frame order routine (0x4EC4D0): `movzx eax, byte [esi+0x4D]`
//and `add eax, -4` (7 bytes), then a first switch's `cmp eax, 0xB3` at 0x4EC564.
//That switch runs every tick for a few orders (SiegeMode's case ends at
//0x4EC582) and goes on to 0x4EC6F1: the order timer (+0x85) counts down and,
//at 0, the MAIN switch (0x4EC735, table 0x4ECD24 by order - 1) runs the order's
//real handler - Move's 0x47C950, ReaverStop's 0x4654B0 - once every 8 ticks.
//The transform runs here, on the first switch, every tick, and goes on where a
//handled case goes (0x4EC6F1); anything else redoes the two instructions.
//Frozen units (Lockdown, Stasis, Maelstrom) never get here (0x4EC518).
const u32 Back_OrderSwitch = 0x004EC564;
const u32 Back_OrderDone = 0x004EC6F1;
void __declspec(naked) orderSwitchStub() {
	static CUnit* unit;
	__asm {
		CMP BYTE PTR [ESI+0x4D], 0x68
		JNE notOurs
		MOV unit, ESI
		PUSHAD
	}
	vikingtransform::orderTransform(unit);
	__asm {
		POPAD
		JMP Back_OrderDone
	notOurs:
		MOVZX EAX, BYTE PTR [ESI+0x4D]
		ADD EAX, -4
		JMP Back_OrderSwitch
	}
}

//The flight to a landing spot reuses ReaverStop (0x07). Its entry in the main
//switch's table (0x4ECD34: case 4, ReaverStop's alone) is pointed here: a
//Viking gets the flight (orderFly), every 8 ticks as Move's handler runs;
//anything else goes on to the engine's case (0x4EC7EC: `push esi; call
//0x4654B0`, then the routine's exit). The first build ran the flight from the
//first switch and left this entry alone, so every 8 ticks the engine's
//ReaverStop stopped the Viking and sent it idle (in game, 2026-10-26).
const u32 Engine_ReaverStopCase = 0x004EC7EC;
const u32 MainSwitch_ReaverStopEntry = 0x004ECD34;
CUnit* flyUnit;
u32 flyHandled;
//an ordinary function, so the naked stub needs no temporary on a frame it
//doesn't have (a `?:` in the stub did: check_naked_wrappers, 2026-10-25)
void runFly() {
	flyHandled = vikingtransform::orderFly(flyUnit) ? 1 : 0;
}
void __declspec(naked) flyStepStub() {
	__asm {
		MOV flyUnit, ESI
		PUSHAD
	}
	runFly();
	__asm {
		POPAD
		CMP flyHandled, 0
		JNE done
		JMP Engine_ReaverStopCase
	done:
		POP EDI
		POP ESI
		POP EBX
		RETN
	}
}

} //unnamed namespace

namespace hooks {

void injectVikingTransformHooks() {
	jmpPatch(actionStub,		0x00423180, 0);	//the Viking's buttons, on BTNSACT_DoNothing
	jmpPatch(orderSwitchStub,	0x004EC55D, 2);	//order 0x68, the transform
	memoryPatch<u32>(MainSwitch_ReaverStopEntry, (u32)flyStepStub);	//order 0x07, the flight (Vikings only)
}

} //hooks
