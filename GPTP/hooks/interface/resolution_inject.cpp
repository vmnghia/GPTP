//Patch sites for the larger view. Each stub notes the address it is installed
//at, what the original instructions were, and how many bytes it replaces.
//Stubs installed with callPatch() return with a RET, adjusting the return
//address past the rest of the overwritten instructions where needed.

#include "resolution.h"
#include <hook_tools.h>
#include <cstring>
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

//Game globals and routines used below.
const u32 Func_StormBlitScreen   = 0x00411E54;  //Storm: copy the screen buffer to the locked surface
const u32 Ret_SetDisplayMode     = 0x0041DA47;
const u32 Ret_TransUpdateDirty   = 0x0041E3E5;

//-------- Plain C entry points for the asm below --------//

static void __cdecl markDirtyThunk(s32 left, s32 top, s32 right, s32 bottom) {
  resolution::markDirty(left, top, right, bottom);
}

static u32 __cdecl isRectDirtyThunk(s32 left, s32 top, s32 right, s32 bottom) {
  return resolution::isRectDirty(left, top, right, bottom) ? 1 : 0;
}

static void __cdecl onScreenUpdateThunk() {
  resolution::onScreenUpdate();
}

//-------- Display mode --------//

//0x0041DA3D: push 0x1E0; push 0x280 (10 bytes) before IDirectDraw::SetDisplayMode.
void __declspec(naked) setDisplayMode_41DA3D() {
  __asm {
    push res_h
    push res_w
    jmp Ret_SetDisplayMode
  }
}

//-------- Screen buffer --------//

//0x0041D44D: mov edx, [ebp-4]; push 0x280 (5 of 8 bytes; returns to 0x0041D45D).
//The screen-to-surface blit in 0x0041D420. Re-issues the Storm call with our
//buffer as the source and the new width as its pitch. The caller already
//pushed the last argument, which RET 4 discards.
static u8* screenBlitSource(u8* original) {
  return original == *(u8**)0x006CEFF4 ? res_screenBmp.data : original;
}

void __declspec(naked) blitScreen_41D44D() {
  __asm {
    push eax
    push ecx
    push ecx
    call screenBlitSource
    add esp, 4
    mov edx, eax            //source
    pop ecx
    pop eax

    push dword ptr [esp+4]  //the argument the caller pushed at 0x0041D44C
    push res_w
    push eax                //destination pitch
    push edx                //source
    push dword ptr [ebp-4]  //destination
    call Func_StormBlitScreen

    add dword ptr [esp], 0x0B
    retn 4
  }
}

//0x0041E2B9: imul ecx, eax; mov esi, ecx (5 bytes).
//In 0x0041E280, on a full redraw, just after the game points the draw target
//(0x006CF4A8) at gameScreenBuffer: swap in the enlarged buffer.
void __declspec(naked) swapScreenBuffer_41E2B9() {
  __asm {
    push eax
    push edx
    mov eax, dword ptr ds:[0x006CF4A8]
    mov edx, res_screenBmp.data
    mov [eax+4], edx
    mov dx, res_screenBmp.width
    mov [eax], dx
    mov dx, res_screenBmp.height
    mov [eax+2], dx
    pop edx
    pop eax
    imul ecx, eax
    mov esi, ecx
    retn
  }
}

//-------- Dirty-cell grid --------//

//0x0041E0D0: the whole function. eax = left, ecx = top, [esp+4] = right,
//edx = bottom (all inclusive), RET 4.
void __declspec(naked) markDirty_41E0D0() {
  __asm {
    push ebp
    mov ebp, esp
    pushad
    push edx
    push dword ptr [ebp+8]
    push ecx
    push eax
    call markDirtyThunk
    add esp, 16
    popad
    pop ebp
    retn 4
  }
}

//0x0041DE20: the whole function. stdcall (left, top, right, bottom), returns al.
void __declspec(naked) isRectDirty_41DE20() {
  __asm {
    push ebp
    mov ebp, esp
    push ecx
    push edx
    push dword ptr [ebp+0x14]
    push dword ptr [ebp+0x10]
    push dword ptr [ebp+0x0C]
    push dword ptr [ebp+0x08]
    call isRectDirtyThunk
    add esp, 16
    pop edx
    pop ecx
    pop ebp
    retn 16
  }
}

//0x0041D750: mov ecx, 0x12C (5 bytes). eax holds the fill value; the original
//then fills the old 1200-byte grid too, which is harmless.
void __declspec(naked) fillCells_41D750() {
  __asm {
    push eax
    mov ecx, res_cellDwords
    mov edi, res_cells
    rep stosd
    pop eax
    mov ecx, 0x12C
    retn
  }
}

//0x0041E3EF: mov ecx, 0x12C (5 bytes). Clears the grid after the update.
void __declspec(naked) clearCells_41E3EF() {
  __asm {
    mov ecx, res_cellDwords
    xor eax, eax
    mov edi, res_cells
    rep stosd
    mov ecx, 0x12C
    retn
  }
}

//0x0042D2D4: lea edx, [ebx+edx*8+0x6CEFF8] (7 bytes; returns to 0x0042D2DB).
//An inlined grid walk: edi = row, ebx = column. Flags must survive for the jg
//that follows.
void __declspec(naked) cellAddress_42D2D4() {
  __asm {
    pushfd
    add dword ptr [esp+4], 2
    push eax
    mov eax, edi
    imul eax, res_cellCols
    add eax, res_cells
    add eax, ebx
    mov edx, eax
    pop eax
    popfd
    retn
  }
}

//0x0042D305: add edx, 0x28; cmp eax, ecx (5 bytes). Next grid row.
void __declspec(naked) nextCellRow_42D305() {
  __asm {
    add edx, res_cellCols
    cmp eax, ecx
    retn
  }
}

//0x00497060: test eax, eax; lea ecx, [ecx+ecx*4] (5 bytes; returns to 0x0049706C,
//skipping lea edi, [edi+ecx*8+0x6CEFF8]). Another inlined walk: ecx = row,
//edi = column, and edx was set to 40 - esi just before.
void __declspec(naked) cellAddress_497060() {
  __asm {
    push ecx
    imul ecx, res_cellCols
    add ecx, res_cells
    add edi, ecx
    pop ecx
    mov edx, res_cellCols
    sub edx, esi
    add dword ptr [esp], 7
    test eax, eax
    retn
  }
}

//-------- Console transparency (Storm) --------//

namespace {

u8* originalHudMask = NULL;
bool hudMaskBuilt = false;

//The object at [0x006D5E14] is Storm's transparency record for the console
//layer: +8 is its run-length mask, and several fields hold its 640x480 size.
void __cdecl prepareScreenTrans(u8* trans) {
  u8** mask = (u8**)(trans + 8);

  if (!hudMaskBuilt || *mask != res_hudMask) {
    if (*mask != res_hudMask)
      originalHudMask = *mask;
    else
      *mask = originalHudMask;

    RECT whole = { 0, 0, res_w, res_h };
    resolution::clipCursor(&whole);
    resolution::buildHudMask(*mask);
    hudMaskBuilt = true;
  }

  *mask = res_hudMask;
  *(s32*)(trans + 0x18) = res_w;
  *(s32*)(trans + 0x28) = res_w;
  *(s32*)(trans + 0x1C) = res_h;
  *(s32*)(trans + 0x2C) = res_h;
  *(s32*)(trans + 0x50) = res_w;
  *(s32*)(trans + 0x60) = res_w;
  *(s32*)(trans + 0x54) = res_h;
  *(s32*)(trans + 0x64) = res_h;
}

} //unnamed namespace

//0x0041E3DD: push 3; push 0x6CEFF8 (7 bytes), eax = [0x006D5E14], before the
//Storm call that turns the dirty grid into blit regions. Jumps back to that call.
void __declspec(naked) screenTrans_41E3DD() {
  __asm {
    pushad
    push eax
    call prepareScreenTrans
    add esp, 4
    popad
    push 3
    push res_cells
    push eax
    jmp Ret_TransUpdateDirty
  }
}

//-------- Cursor clipping --------//

//0x0041CF1E: mov edx, [ebp-0x1C]; test edx, edx (5 bytes).
void __declspec(naked) screenUpdate_41CF1E() {
  __asm {
    pushad
    call onScreenUpdateThunk
    popad
    mov edx, [ebp-0x1C]
    test edx, edx
    retn
  }
}

//-------- Game layer --------//

//0x004BD64C in 0x004BD630 (game layer setup at game start): mov ecx, 0x12C
//(5 bytes), before filling the old grid with 1s (eax is set after this).
void __declspec(naked) fillCellsAtStart_4BD64C() {
  __asm {
    mov eax, 0x01010101
    mov ecx, res_cellDwords
    mov edi, res_cells
    rep stosd
    mov ecx, 0x12C
    retn
  }
}

//-------- Console hit test --------//

const u32 Ret_ConsoleHitTest = 0x004D1146;

//0x004D1140: cmp eax, [0x596B6C] (6 bytes), the start of "is (ecx, eax) on the
//console?". It answers "no" above the console's top line and "yes" below its
//bottom line without looking at the mask; in vanilla everything below that
//line is console across the full 640 width. With the console left at its
//vanilla place, nothing right of x = 640 or below y = 480 is console.
void __declspec(naked) consoleHitTest_4D1140() {
  __asm {
    cmp ecx, 640
    jge NOT_CONSOLE
    cmp eax, 480
    jge NOT_CONSOLE
    cmp eax, dword ptr ds:[0x00596B6C]
    jmp Ret_ConsoleHitTest
  NOT_CONSOLE:
    xor eax, eax
    retn
  }
}

//-------- Cursor clipping --------//

namespace {

const u8* const gameLayerFlags = (const u8*)0x006CEFB4;

void __cdecl adjustCursorClip(RECT* clip) {
  if (*gameLayerFlags) {
    clip->right = res_w;
    clip->bottom = res_h;
  }
}

void __cdecl adjustDialogClip(RECT* clip) {
  //The game clips the cursor to the view (0,0)-(639,399) in some modes.
  if (clip->right == 639 && clip->bottom == 399) {
    clip->right = res_wm1;
    clip->bottom = res_viewHm1;
  }
}

} //unnamed namespace

const u32 Ret_ClipCursorCall = 0x004216E0;

//0x0042163B: the RET of 0x004215E0, which sets the cursor clip rect
//(0x006CDDB0) to the 640x480 client area. In a game, widen it.
void __declspec(naked) cursorClipRect_42163B() {
  __asm {
    pushad
    push 0x006CDDB0
    call adjustCursorClip
    add esp, 4
    popad
    retn
  }
}

//0x004216DB: push 0x6CDDB0 (5 bytes), before ClipCursor.
void __declspec(naked) clipCursor_4216DB() {
  __asm {
    pushad
    push 0x006CDDB0
    call adjustDialogClip
    add esp, 4
    popad
    push 0x006CDDB0
    jmp Ret_ClipCursorCall
  }
}

//-------- Storm's dirty-cell blit --------//

//Storm walks the dirty-cell grid (passed at 0x0041E3E5) with a row-offset
//table and a scratch buffer, both sized for 640x480.

//storm 0x1501AEDB: mov esi, [ebp-0x2C]; mov eax, [ebx+0x1C] (6 bytes).
void __declspec(naked) stormScratch_1501AEDB() {
  __asm {
    mov esi, res_stormBuf
    mov [ebp-0x2C], esi
    mov eax, [ebx+0x1C]
    inc dword ptr [esp]
    retn
  }
}

//storm 0x1501AF9B: mov [ebp-0x1C], edi; mov [ebp-0x14], esi (6 bytes), just
//after ecx was loaded with the row-offset table.
void __declspec(naked) stormRows_1501AF9B() {
  __asm {
    mov [ebp-0x1C], edi
    mov [ebp-0x14], esi
    mov ecx, res_cellRows
    inc dword ptr [esp]
    retn
  }
}

namespace {

template <typename T>
void patchValue(u32 address, T value) {
  memoryPatch(address, value);
}

//Screen-size immediates in the game's own instructions.
void patchScreenConstants() {
  //Mouse position clamps: three button-message handlers (0x004D1940...) and
  //WM_MOUSEMOVE in the window procedure.
  const u32 handlerOffsets[3] = { 0, 0x004D19EC - 0x004D1960, 0x004D1A7C - 0x004D1960 };
  for (u32 delta : handlerOffsets) {
    patchValue<s16>(0x004D1960 + delta + 3, (s16)res_w);    //cmp si, 0x280
    patchValue<s16>(0x004D196D + delta + 2, (s16)res_wm1);  //mov ax, 0x27F
    patchValue<s16>(0x004D1982 + delta + 2, (s16)res_h);    //cmp ax, 0x1E0
    patchValue<s16>(0x004D198E + delta + 2, (s16)res_hm1);  //mov ax, 0x1DF
  }
  patchValue<s16>(0x004D24E6 + 2, (s16)res_w);              //cmp ax, 0x280
  patchValue<s32>(0x004D24EC + 6, res_wm1);                 //mov [mouse.x], 0x27F
  patchValue<s16>(0x004D2504 + 2, (s16)res_h);              //cmp ax, 0x1E0
  patchValue<s32>(0x004D250C + 6, res_hm1);                 //mov [mouse.y], 0x1DF

  //Scrolling when the mouse touches the screen edge
  patchValue<s32>(0x004D12FF + 1, res_w - 2);               //cmp eax, 0x27E
  patchValue<s32>(0x004D1332 + 2, res_h - 2);               //cmp ecx, 0x1DE

  //View bounds
  patchValue<s32>(0x004D5856 + 1, res_w);                   //mov esi, 0x280
  patchValue<s32>(0x004D5887 + 1, res_viewH);               //mov ecx, 0x190
  patchValue<s32>(0x0048D5F2 + 2, res_w);                   //lea edi, [ecx+0x280]
  patchValue<s32>(0x0048D5FC + 2, res_viewH);               //lea edi, [eax+0x190]
  patchValue<s16>(0x0048D663 + 2, (s16)res_w);              //cmp ax, 0x280
  patchValue<s16>(0x0048D66D + 3, (s16)res_viewH);          //cmp cx, 0x190

  //Game layer and view rect at game start (0x004BD630)
  patchValue<s32>(0x004BD633 + 1, res_viewHm1);             //SetRect(0x5993B0, 0, 0, 639, 399)
  patchValue<s32>(0x004BD638 + 1, res_wm1);
  patchValue<s16>(0x004BD675 + 7, (s16)res_w);              //game layer width
  patchValue<s16>(0x004BD67E + 7, (s16)res_viewH);          //game layer height
}

} //unnamed namespace

namespace hooks {

void injectResolutionHooks() {
  if (!resolution::enabled())
    return;

  if (!resolution::init()) {
    MessageBox(NULL, "Could not allocate the larger screen buffers; running at 640x480.",
               "StarCraft: Manifold", MB_OK | MB_ICONWARNING);
    return;
  }

  //Display and screen buffer
  jmpPatch(setDisplayMode_41DA3D,   0x0041DA3D);
  callPatch(blitScreen_41D44D,      0x0041D44D);
  callPatch(swapScreenBuffer_41E2B9, 0x0041E2B9);

  //Dirty-cell grid
  jmpPatch(markDirty_41E0D0,        0x0041E0D0);
  jmpPatch(isRectDirty_41DE20,      0x0041DE20);
  callPatch(fillCells_41D750,       0x0041D750);
  callPatch(clearCells_41E3EF,      0x0041E3EF);
  callPatch(cellAddress_42D2D4,     0x0042D2D4);
  callPatch(nextCellRow_42D305,     0x0042D305);
  callPatch(cellAddress_497060,     0x00497060);

  //Console transparency and cursor
  jmpPatch(screenTrans_41E3DD,      0x0041E3DD);
  callPatch(screenUpdate_41CF1E,    0x0041CF1E);
  jmpPatch(cursorClipRect_42163B,   0x0042163B);
  jmpPatch(clipCursor_4216DB,       0x004216DB);
  jmpPatch(consoleHitTest_4D1140,   0x004D1140, 1);

  //Game layer, mouse, scrolling
  callPatch(fillCellsAtStart_4BD64C, 0x004BD64C);
  patchScreenConstants();

  //Storm
  callPatch(stormScratch_1501AEDB,  0x1501AEDB, 1);
  callPatch(stormRows_1501AF9B,     0x1501AF9B, 1);

  resolution::injectTerrainHooks();
  resolution::injectFogHooks();
}

} //hooks
