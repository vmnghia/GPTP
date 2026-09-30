//Wrappers from the exe's calling conventions to the extended selection, and
//the functions that install them. Addresses and conventions: see the spec.
#include "selection_ext_hooks.h"
#include "sel_synced.h"
#include "sel_local.h"
#include "sel_send.h"
#include "sel_save.h"
#include <SCBW/selection_ext.h>
#include <hook_tools.h>

namespace {

//-------- Stage 1 --------//

//0x49A850: no arguments, the unit in EAX; every other register kept.
void __declspec(naked) nextSelectedWrapper() {
	static CUnit* unit;
	__asm PUSHAD
	unit = selsync::nextSelected();
	__asm {
		POPAD
		MOV EAX, unit
		RETN
	}
}

//0x49A7F0: EDI = unit.
void __declspec(naked) removeFromAllSelectionsWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EDI
		PUSHAD
	}
	selsync::removeFromAllSelections(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x49A740: EAX = player.
void __declspec(naked) clearSelectionWrapper() {
	static u32 player;
	__asm {
		MOV player, EAX
		PUSHAD
	}
	selsync::clearSelection(player);
	__asm {
		POPAD
		RETN
	}
}

//0x4C2750, 0x4C2560, 0x4BFB40: stdcall(packet).
void __declspec(naked) recvSelectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvSelect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

void __declspec(naked) recvShiftSelectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvShiftSelect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

void __declspec(naked) recvShiftDeselectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvShiftDeselect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

//0x4C2870: ECX = packet.
void __declspec(naked) recvHotkeyWrapper() {
	static const u8* packet;
	__asm {
		MOV packet, ECX
		PUSHAD
	}
	selsync::recvHotkey(packet);
	__asm {
		POPAD
		RETN
	}
}

//0x49AE40: EAX = list, stdcall(count).
void __declspec(naked) buildActiveWrapper() {
	static CUnit** list;
	static u32 count;
	__asm {
		MOV list, EAX
		MOV EAX, [ESP+4]
		MOV count, EAX
		PUSHAD
	}
	sellocal::buildActive(list, count);
	__asm {
		POPAD
		RETN 4
	}
}

//0x49F7A0: EAX = unit.
void __declspec(naked) localRemoveWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EAX
		PUSHAD
	}
	sellocal::localRemove(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x499A60: no arguments.
void __declspec(naked) redrawCirclesWrapper() {
	__asm PUSHAD
	sellocal::redrawCircles();
	__asm {
		POPAD
		RETN
	}
}

//0x4C38B0: no arguments; ends with a tail jump to 0x458DE0 (button set).
const u32 Func_UpdateButtonSet = 0x00458DE0;
void __declspec(naked) clientCopyWrapper() {
	__asm PUSHAD
	sellocal::clientCopy();
	__asm {
		POPAD
		JMP Func_UpdateButtonSet
	}
}

//0x4C3B40: EDX = unit.
void __declspec(naked) deselectAndSendWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EDX
		PUSHAD
	}
	sellocal::deselectAndSend(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x4D0820: no arguments.
void __declspec(naked) reselectAtStartWrapper() {
	__asm PUSHAD
	sellocal::reselectAtStart();
	__asm {
		POPAD
		RETN
	}
}

//0x4EED10 entry (8 bytes: push edi; xor eax, eax; mov ecx, 0xC).
const u32 Back_GameStartEntry = 0x004EED18;
void __declspec(naked) gameStartEntryWrapper() {
	__asm PUSHAD
	sellocal::gameStartClear();
	__asm {
		POPAD
		PUSH EDI
		XOR EAX, EAX
		MOV ECX, 0x0C
		JMP Back_GameStartEntry
	}
}

//0x4EEDC6 (5 bytes: mov eax, [0x512688]), where vanilla keeps the local
//player's 12 and wipes the synced selections.
const u32 Back_GameStartKeepLocal = 0x004EEDCB;
void __declspec(naked) gameStartKeepLocalWrapper() {
	__asm PUSHAD
	sellocal::gameStartKeepLocal();
	__asm {
		POPAD
		MOV EAX, 0x00512688
		MOV EAX, [EAX]
		JMP Back_GameStartKeepLocal
	}
}

//0x4C0860: stdcall(count, list).
void __declspec(naked) cmdactSelectWrapper() {
	static u32 count;
	static CUnit** list;
	__asm {
		MOV EAX, [ESP+4]
		MOV count, EAX
		MOV EAX, [ESP+8]
		MOV list, EAX
		PUSHAD
	}
	selsend::cmdactSelect(count, list);
	__asm {
		POPAD
		RETN 8
	}
}

//0x4C07B0: BL = group, stdcall(u8 action, CUnit** list, u8 count).
void __declspec(naked) cmdactHotkeyWrapper() {
	static u32 group, action, count;
	static CUnit** list;
	__asm {
		MOV group, EBX
		MOV EAX, [ESP+4]
		MOV action, EAX
		MOV EAX, [ESP+8]
		MOV list, EAX
		MOV EAX, [ESP+12]
		MOV count, EAX
		PUSHAD
	}
	selsend::cmdactHotkey(group & 0xFF, action & 0xFF, list, count & 0xFF);
	__asm {
		POPAD
		RETN 12
	}
}

} //unnamed namespace

namespace hooks {

void injectSelectionExtHooks() {
	jmpPatch(nextSelectedWrapper,				0x0049A850, 2);
	jmpPatch(removeFromAllSelectionsWrapper,	0x0049A7F0, 1);
	jmpPatch(clearSelectionWrapper,				0x0049A740, 1);
	jmpPatch(recvSelectWrapper,					0x004C2750, 1);
	jmpPatch(recvShiftSelectWrapper,			0x004C2560, 1);
	jmpPatch(recvShiftDeselectWrapper,			0x004BFB40, 1);
	jmpPatch(recvHotkeyWrapper,					0x004C2870, 0);
	jmpPatch(buildActiveWrapper,				0x0049AE40, 0);
	jmpPatch(localRemoveWrapper,				0x0049F7A0, 1);
	jmpPatch(redrawCirclesWrapper,				0x00499A60, 1);
	jmpPatch(clientCopyWrapper,					0x004C38B0, 3);
	jmpPatch(deselectAndSendWrapper,			0x004C3B40, 1);
	jmpPatch(reselectAtStartWrapper,			0x004D0820, 1);
	jmpPatch(gameStartEntryWrapper,				0x004EED10, 3);
	jmpPatch(gameStartKeepLocalWrapper,			0x004EEDC6, 0);
	jmpPatch(cmdactSelectWrapper,				0x004C0860, 1);
	jmpPatch(cmdactHotkeyWrapper,				0x004C07B0, 2);
	callPatch(selsave::writeLastAndExtension,	0x004C2E0A, 0);
	callPatch(selsave::readLastAndExtension,	0x004D0225, 0);
}

} //hooks
