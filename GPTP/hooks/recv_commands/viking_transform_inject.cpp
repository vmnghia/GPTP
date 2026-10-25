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

//0x4EC55D in the per-frame order switch: `movzx eax, byte [esi+0x4D]` and
//`add eax, -4` (7 bytes), then the switch's `cmp eax, 0xB3` at 0x4EC564. The
//transform's order runs here and goes on where a handled case goes
//(0x4EC6F1, as SiegeMode's 0x4EC582); anything else redoes the two
//instructions. Frozen units (Lockdown, Stasis, Maelstrom) never get here
//(0x4EC518). The flight to a landing spot reuses ReaverStop (0x07): run here
//for a Viking only (orderFly says), the Reaver's goes on to the exe's case.
const u32 Back_OrderSwitch = 0x004EC564;
const u32 Back_OrderDone = 0x004EC6F1;
CUnit* flyUnit;
u32 flyHandled;
//an ordinary function, so the naked stub needs no temporary on a frame it
//doesn't have (a `?:` in the stub did: check_naked_wrappers, 2026-10-25)
void runFly() {
	flyHandled = vikingtransform::orderFly(flyUnit) ? 1 : 0;
}
void __declspec(naked) orderSwitchStub() {
	static CUnit* unit;
	__asm {
		CMP BYTE PTR [ESI+0x4D], 0x68
		JE transform
		CMP BYTE PTR [ESI+0x4D], 0x07
		JNE notOurs
		MOV flyUnit, ESI
		PUSHAD
	}
	runFly();
	__asm {
		POPAD
		CMP flyHandled, 0
		JNE done
		JMP notOurs
	transform:
		MOV unit, ESI
		PUSHAD
	}
	vikingtransform::orderTransform(unit);
	__asm {
		POPAD
	done:
		JMP Back_OrderDone
	notOurs:
		MOVZX EAX, BYTE PTR [ESI+0x4D]
		ADD EAX, -4
		JMP Back_OrderSwitch
	}
}

} //unnamed namespace

namespace hooks {

void injectVikingTransformHooks() {
	jmpPatch(actionStub,		0x00423180, 0);	//the Viking's buttons, on BTNSACT_DoNothing
	jmpPatch(orderSwitchStub,	0x004EC55D, 2);	//order 0x68, the transform
}

} //hooks
