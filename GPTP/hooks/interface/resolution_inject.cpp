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

//Outside a game, the menus draw into the top-left 640x480 of the screen
//buffer. Show that block centred and clear everything around it, which also
//removes the last game frame after quitting to the menu. The whole screen is
//copied every time; at this size that is cheap.
static void drawMenuCentred(u8* surface, s32 pitch) {
  const u8* src = *(u8**)0x006CEFF4;          //gameScreenBuffer pixels
  const s32 srcPitch = *(u16*)0x006CEFF0;     //and its width
  for (s32 y = 0; y < res_h; ++y) {
    u8* row = surface + y * pitch;
    const s32 menuRow = y - res_menuY;
    if (menuRow < 0 || menuRow >= 480) {
      memset(row, 0, res_w);
      continue;
    }
    memset(row, 0, res_menuX);
    memcpy(row + res_menuX, src + menuRow * srcPitch, 640);
    memset(row + res_menuX + 640, 0, res_w - res_menuX - 640);
  }
}

static u32 __cdecl blitMenu(u8* surface, s32 pitch) {
  if (resolution::inGame())
    return 0;
  drawMenuCentred(surface, pitch);
  return 1;
}

void __declspec(naked) blitScreen_41D44D() {
  __asm {
    push eax
    push ecx
    push eax                //destination pitch
    push dword ptr [ebp-4]  //destination
    call blitMenu
    add esp, 8
    test eax, eax
    pop ecx
    pop eax
    jne DONE

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

  DONE:
    add dword ptr [esp], 0x0B
    retn 4
  }
}

//0x0041D3A0: the whole function. Blits the rect at esi straight from the
//screen buffer to the surface, used for full redraws. Its source stride is
//hardcoded to 640. Outside a game, draws the centred menu instead.
typedef BOOL (__stdcall *SDrawLockSurfaceFn)(int surface, RECT* rect, u8** bits, s32* pitch, int flags);
typedef BOOL (__stdcall *SDrawUnlockSurfaceFn)(int surface, u8* bits, int rectCount, RECT* rects);
typedef BOOL (__stdcall *SBltROP3Fn)(u8* dest, const u8* src, s32 width, s32 height,
                                     s32 destPitch, s32 srcPitch, int pattern, u32 rop);
const SDrawLockSurfaceFn   stormLockSurface   = (SDrawLockSurfaceFn)0x00411E4E;
const SDrawUnlockSurfaceFn stormUnlockSurface = (SDrawUnlockSurfaceFn)0x00411E48;
const SBltROP3Fn           stormBltROP3       = (SBltROP3Fn)0x004100E2;

static void __cdecl blitRect(RECT* rect) {
  u8* surface;
  s32 pitch;
  if (!stormLockSurface(0, NULL, &surface, &pitch, 0))
    return;

  if (!resolution::inGame()) {
    drawMenuCentred(surface, pitch);
    RECT whole = { 0, 0, res_w, res_h };
    stormUnlockSurface(0, surface, 1, &whole);
    return;
  }

  const u8* src = *(u8**)0x006CEFF4;
  const s32 srcPitch = *(u16*)0x006CEFF0;
  stormBltROP3(surface + rect->top * pitch + rect->left,
               src + rect->top * srcPitch + rect->left,
               rect->right - rect->left, rect->bottom - rect->top,
               pitch, srcPitch, 0, 0x00CC0020);  //SRCCOPY
  stormUnlockSurface(0, surface, 1, rect);
}

void __declspec(naked) blitRect_41D3A0() {
  __asm {
    push eax
    push ecx
    push edx
    push esi
    call blitRect
    add esp, 4
    pop edx
    pop ecx
    pop eax
    retn
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

//0x0041DDFF: mov [0x6CEFF4], eax (5 bytes), in 0x0041DDD0, which allocates
//gameScreenBuffer (0x006CEFF0). Its size and dimensions are patched to the full
//screen, so this is the one screen buffer: record it for the code that draws
//into it directly. It stays Storm-allocated, so the game frees it as usual.
void __declspec(naked) screenBufferAllocated_41DDFF() {
  __asm {
    mov dword ptr ds:[0x006CEFF4], eax
    mov res_screenBmp.data, eax
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
//Width of the first row of a run-length mask (see buildHudMask).
s32 maskRowWidth(const u8* mask) {
  s32 width = 0;
  while (*(const s16*)mask != 0) {
    width += mask[0] + mask[1];
    mask += 2;
  }
  return width;
}

void __cdecl prepareScreenTrans(u8* trans) {
  u8** mask = (u8**)(trans + 8);

  //In a game the console image is already full size (resolution_hud.cpp), so
  //the combined mask needs nothing. The 640x480 menus still get padded.
  if (*mask != res_hudMask && *mask != NULL && maskRowWidth(*mask) >= res_w)
    return;

  if (!hudMaskBuilt || *mask != res_hudMask) {
    if (*mask != res_hudMask)
      originalHudMask = *mask;
    else
      *mask = originalHudMask;

    if (resolution::inGame()) {
      RECT whole = { 0, 0, res_w, res_h };
      resolution::clipCursor(&whole);
    }
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

//-------- Cursor clipping --------//

namespace {

const u8* const gameLayerFlags = (const u8*)0x006CEFB4;

void __cdecl adjustCursorClip(RECT* clip) {
  if (*gameLayerFlags) {
    clip->right = clip->left + res_w;
    clip->bottom = clip->top + res_h;
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

//-------- Fill rect --------//

//0x004E1D20: the whole function. Fills a rect of the current draw target
//(0x006CF4A8) with the current colour (0x006CF4AC). The original doesn't clip;
//this one clips to the target bitmap, so no caller can write past a buffer.
//stdcall (s16 x, s16 y, u16 width, u16 height).
static void __stdcall fillRect(s32 x, s32 y, u32 width, u32 height) {
  const ResBitmap* target = *(const ResBitmap**)0x006CF4A8;
  const u8 colour = *(const u8*)0x006CF4AC;
  s32 left = (s16)x;
  s32 top = (s16)y;
  s32 right = left + (s32)(u16)width;
  s32 bottom = top + (s32)(u16)height;
  if (left < 0) left = 0;
  if (top < 0) top = 0;
  if (right > target->width) right = target->width;
  if (bottom > target->height) bottom = target->height;
  if (right <= left)
    return;
  for (s32 row = top; row < bottom; ++row)
    memset(target->data + row * target->width + left, colour, right - left);
}

void __declspec(naked) fillRect_4E1D20() {
  __asm {
    push ecx
    push edx
    //After the two pushes the arguments start at [esp+0x0C]; each push below
    //shifts them by 4, so [esp+0x18] is height, width, y, then x in turn.
    push dword ptr [esp+0x18]
    push dword ptr [esp+0x18]
    push dword ptr [esp+0x18]
    push dword ptr [esp+0x18]
    call fillRect
    pop edx
    pop ecx
    retn 0x10
  }
}

//0x0041D810 (horizontal) and 0x0041D7D0 (vertical): single-pixel lines in
//the current draw target and colour, also unclipped in the original. x and y
//are on the stack (RET 8); the length is in cx (horizontal) or dx (vertical).
static void __cdecl drawHLine(s32 x, s32 y, u32 length) {
  const ResBitmap* target = *(const ResBitmap**)0x006CF4A8;
  const u8 colour = *(const u8*)0x006CF4AC;
  s32 left = (s16)x;
  const s32 row = (s16)y;
  s32 right = left + (s32)(u16)length;
  if (row < 0 || row >= target->height) return;
  if (left < 0) left = 0;
  if (right > target->width) right = target->width;
  if (right > left)
    memset(target->data + row * target->width + left, colour, right - left);
}

static void __cdecl drawVLine(s32 x, s32 y, u32 length) {
  const ResBitmap* target = *(const ResBitmap**)0x006CF4A8;
  const u8 colour = *(const u8*)0x006CF4AC;
  const s32 column = (s16)x;
  s32 top = (s32)(u16)y;
  s32 bottom = top + (s32)(u16)length;
  if (column < 0 || column >= target->width) return;
  if (bottom > target->height) bottom = target->height;
  for (s32 row = top; row < bottom; ++row)
    target->data[row * target->width + column] = colour;
}

void __declspec(naked) drawHLine_41D810() {
  __asm {
    push ecx
    push dword ptr [esp+0x0C]
    push dword ptr [esp+0x0C]
    call drawHLine
    add esp, 0x0C
    retn 8
  }
}

void __declspec(naked) drawVLine_41D7D0() {
  __asm {
    push edx
    push dword ptr [esp+0x0C]
    push dword ptr [esp+0x0C]
    call drawVLine
    add esp, 0x0C
    retn 8
  }
}

//-------- Menus: output --------//

//0x00417354: mov eax, [ebp-0x10]; jmp 0x417365 (5 bytes), in the dialog blit
//at 0x004172F0. When [0x006D05A0] is set, as on the menu screens, dialogs are
//blitted straight onto the locked surface at their 640x480 position, bypassing
//the screen buffer. Outside a game, move that onto the centred menu area.
//ecx holds the surface pitch.
const u32 Ret_DialogBlitSurface = 0x00417365;

void __declspec(naked) dialogBlitSurface_417354() {
  __asm {
    mov eax, [ebp-0x10]
    cmp byte ptr ds:[0x006CEFB4], 0
    jne DONE
    push edx
    mov edx, res_menuY
    imul edx, ecx
    add eax, edx
    add eax, res_menuX
    pop edx
  DONE:
    jmp Ret_DialogBlitSurface
  }
}

namespace resolution {

//Blanks the whole surface: the menus only ever redraw their own 640x480, so
//whatever was on screen before (the last game frame) would stay around them.
void clearSurface() {
  u8* surface;
  s32 pitch;
  if (!stormLockSurface(0, NULL, &surface, &pitch, 0))
    return;
  for (s32 y = 0; y < res_h; ++y)
    memset(surface + y * pitch, 0, res_w);
  RECT whole = { 0, 0, res_w, res_h };
  stormUnlockSurface(0, surface, 1, &whole);
}

}

//-------- Menus: input --------//

//0x004D2324: cmp ebx, 0x215 (6 bytes) in the window procedure, just before
//the mouse messages are dispatched (ebx = message, [ebp+0x14] = lParam).
//Outside a game, move the mouse from screen coordinates into the centred
//640x480 menu. (WM_MOUSEWHEEL, 0x20A, carries screen coordinates and is left
//alone.)
static u32 __cdecl menuMouse(u32 message, u32 lParam) {
  if (message < 0x200 || message > 0x209 || resolution::inGame())
    return lParam;
  s32 x = (s16)(lParam & 0xFFFF) - res_menuX;
  s32 y = (s16)(lParam >> 16) - res_menuY;
  x = x < 0 ? 0 : (x > 639 ? 639 : x);
  y = y < 0 ? 0 : (y > 479 ? 479 : y);
  return ((u32)y << 16) | (u32)x;
}

void __declspec(naked) menuMouse_4D2324() {
  __asm {
    pushad
    push dword ptr [ebp+0x14]
    push ebx
    call menuMouse
    add esp, 8
    mov [ebp+0x14], eax
    popad
    cmp ebx, 0x215
    retn
  }
}

//The game's ClipCursor and SetCursorPos calls use 640x480 client coordinates.
//Outside a game, offset them to the centred menu. Each replaces a
//call [import] (6 bytes) and keeps its stdcall signature.
static BOOL WINAPI clipCursorForMenu(const RECT* rect) {
  if (rect && !resolution::inGame()) {
    RECT menu = *rect;
    OffsetRect(&menu, res_menuX, res_menuY);
    return resolution::clipCursor(&menu);
  }
  return resolution::clipCursor(rect);
}

static BOOL WINAPI setCursorPosForMenu(int x, int y) {
  if (!resolution::inGame()) {
    x += res_menuX;
    y += res_menuY;
  }
  else if (x == 320 && y == 200) {
    //Centre of the vanilla 640x400 view: use the centre of the larger one.
    x = res_wHalf;
    y = res_viewH / 2;
  }
  return resolution::setCursorPos(x, y);
}

const u32 clipCursorSites[7] = {
  0x004216E0, 0x0042171E, 0x0042175E, 0x004A3ED2, 0x004A4D57, 0x004D3032, 0x004E45AF,
};
//0x004D1791 is left out: it re-sets the cursor to where GetCursorPos found it.
const u32 setCursorPosSites[4] = {
  0x00421678, 0x0047EB4A, 0x004C58AB, 0x004E0884,
};

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

  //The dialog layer (screenLayers[2], set up at 0x0041A030) covers the whole
  //screen, or dialogs moved outside 640x480 (the console) are never redrawn.
  patchValue<s16>(0x0041A049 + 7, (s16)res_w);              //width 0x280
  patchValue<s16>(0x0041A052 + 7, (s16)res_h);              //height 0x1E0

  //Full-redraw rect in 0x0041E280, passed to 0x0041D3A0
  patchValue<s32>(0x0041E2D5 + 3, res_w);                   //mov [ebp-0xC], 0x280
  patchValue<s32>(0x0041E2DC + 3, res_h);                   //mov [ebp-8], 0x1E0

  //Game layer and view rect at game start (0x004BD630)
  patchValue<s32>(0x004BD633 + 1, res_viewHm1);             //SetRect(0x5993B0, 0, 0, 639, 399)
  patchValue<s32>(0x004BD638 + 1, res_wm1);
  patchValue<s16>(0x004BD675 + 7, (s16)res_w);              //game layer width
  patchValue<s16>(0x004BD67E + 7, (s16)res_viewH);          //game layer height

  //Centring the view on a point subtracts half of 640x400 before calling
  //setScreenPos (0x0049C440).
  const s32 halfW = res_w / 2, halfH = res_viewH / 2;
  const s8 halfWTiles = (s8)(res_w / 64), halfHTiles = (s8)(res_viewH / 64);
  patchValue<s32>(0x0045EE52 + 2, -halfW);                  //last alert: lea eax, [edi-0x140]
  patchValue<s32>(0x0045EE65 + 1, -halfH);                  //add eax, -0xC8
  patchValue<s32>(0x0049691B + 2, halfH);                   //selected group: sub ecx, 0xC8
  patchValue<s32>(0x00496924 + 1, halfW);                   //sub eax, 0x140
  patchValue<s32>(0x004C6E68 + 2, halfH);                   //CenterView trigger: sub ecx, 0xC8
  patchValue<s32>(0x004C6E6E + 1, halfW);                   //sub eax, 0x140
  patchValue<s32>(0x004C6EF7 + 2, halfW);                   //location centre: sub ecx, 0x140
  patchValue<s32>(0x004C6EFD + 1, halfH);                   //sub eax, 0xC8
  patchValue<s32>(0x004C6F11 + 2, res_w);                   //lea esi, [ecx+0x280]
  patchValue<s32>(0x004C6F1B + 2, -(res_w + 1));            //lea ecx, [edx-0x281]
  patchValue<s32>(0x004C6F30 + 2, res_viewH);               //lea esi, [eax+0x190]
  patchValue<s32>(0x004C6F3A + 2, -(res_viewH + 1));        //lea eax, [edx-0x191]
  patchValue<s32>(0x004844BB + 2, res_viewH);               //scroll by percent: sub ecx, 0x190
  patchValue<s32>(0x004844DC + 2, res_w);                   //sub edx, 0x280
  patchValue<s8>(0x004BD4B0 + 2, -halfHTiles);              //tile centre: add ecx, -6
  patchValue<s8>(0x004BD4B3 + 2, -halfWTiles);              //add eax, -0xA
  patchValue<s8>(0x004E6040 + 2, halfHTiles);               //unit (portrait click): sub ecx, 6
  patchValue<s8>(0x004E6043 + 2, halfWTiles);               //sub eax, 0xA
}

} //unnamed namespace

namespace hooks {

void injectResolutionHooks() {
  if (RESOLUTION_HACK_ENABLED == 0)
    return;
  //640x480: no larger screen, but the console's layout still runs (the
  //selection panel's taller box and page controls need it).
  if (!resolution::enabled()) {
    resolution::useVanillaSize();
    resolution::injectConsoleLayoutHooks();
    return;
  }

  if (!resolution::init()) {
    resolution::active = false;
    MessageBox(NULL, "Could not allocate the larger screen buffers; running at 640x480.",
               "StarCraft: Manifold", MB_OK | MB_ICONWARNING);
    resolution::useVanillaSize();
    resolution::injectConsoleLayoutHooks();
    return;
  }

  //Display and screen buffer: the game allocates it at the full size itself
  //(0x0041DDD0), and resets its dimensions at 0x0041E050.
  memoryPatch(0x0041DDD9 + 1, (s32)(res_w * res_h));        //push 0x4B000
  memoryPatch(0x0041DDDE + 7, (s16)res_w);                  //width 0x280
  memoryPatch(0x0041DDE7 + 7, (s16)res_h);                  //height 0x1E0
  memoryPatch(0x0041E07B + 7, (s16)res_w);
  memoryPatch(0x0041E084 + 7, (s16)res_h);
  callPatch(screenBufferAllocated_41DDFF, 0x0041DDFF);
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

  //Menus, centred
  jmpPatch(blitRect_41D3A0,         0x0041D3A0);
  jmpPatch(dialogBlitSurface_417354, 0x00417354);
  jmpPatch(fillRect_4E1D20,          0x004E1D20);
  jmpPatch(drawHLine_41D810,         0x0041D810);
  jmpPatch(drawVLine_41D7D0,         0x0041D7D0);
  callPatch(menuMouse_4D2324,       0x004D2324, 1);
  for (u32 site : clipCursorSites)
    callPatch(clipCursorForMenu, site, 1);
  for (u32 site : setCursorPosSites)
    callPatch(setCursorPosForMenu, site, 1);

  //Game layer, mouse, scrolling
  callPatch(fillCellsAtStart_4BD64C, 0x004BD64C);
  patchScreenConstants();

  //Storm
  callPatch(stormScratch_1501AEDB,  0x1501AEDB, 1);
  callPatch(stormRows_1501AF9B,     0x1501AF9B, 1);

  resolution::injectHudHooks();
  resolution::injectTerrainHooks();
  resolution::injectFogHooks();
}

} //hooks
