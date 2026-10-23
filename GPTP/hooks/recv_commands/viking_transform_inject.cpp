#include "viking_transform.h"
#include <SCBW/viking_transform.h>
#include <hook_tools.h>

namespace {

//0x423180, BTNSACT_DoNothing: a bare RET that only the vanilla button table
//points at (sc_disasm callers: none). A button action gets its actVar in ECX
//and the Shift (queued) byte in DL (the siege action 0x423450 stores DL).
void __declspec(naked) actionStub() {
	static u32 actVar;
	static u32 queued;
	__asm {
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
//(0x4EC518).
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

} //unnamed namespace

namespace hooks {

void injectVikingTransformHooks() {
	jmpPatch(actionStub,		0x00423180, 0);	//the Viking's buttons, on BTNSACT_DoNothing
	jmpPatch(orderSwitchStub,	0x004EC55D, 2);	//order 0x68, the transform
}

} //hooks
