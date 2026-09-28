//The in-game console (HUD) on the larger screen.
//
//The console is not one object. Its panels are separate dialogs loaded from
//rez\*.bin (Minimap, StatData, StatPort, StatBtn, StatRes), each with absolute
//640x480 coordinates, and its art is one 640x480 image,
//game\<race>console.pcx, kept at 0x00597240. Each panel copies its background
//out of that image at its own position (0x004C35F0), the console's
//transparency mask is built from the image (0x0041D640), and so are the lines
//the console hit test uses (0x004D11A0).
//
//The console runs the full width of the screen. The vanilla art is split at
//a column inside the selection-info box (StatData): everything left of it
//stays at the left edge, everything right of it moves to the right edge, and
//the gap between is filled from a strip of the box's art. So the box itself
//widens; the extra room is kept for a larger selection later.
//
//The layout is done in two places, kept consistent:
//  - every panel dialog is moved when its .bin is loaded, per the table below,
//    and the decorative pieces (StatFluf) per their own tables;
//  - the console image is rebuilt at the full screen size.
//Everything derived from the image (mask, hit test, panel backgrounds) then
//follows by itself. To change the layout, change the table and the split, or
//build the full-size image from other art in widenConsoleImage().
//
//The console image uses the in-game palette: it is loaded with no palette, so
//its pixel values are indices into the current game palette, and 0 is
//transparent.

#include "resolution.h"
#include <hook_tools.h>
#include <SCBW/api.h>
#include <cstdio>
#include <cstring>

namespace {

//Where the parts of the vanilla 640x480 console go: the left part at the
//bottom-left corner, the right part at the bottom-right one.
s32 consoleLeftX = 0;
s32 consoleRightX;
s32 consoleY;
//The resources display (top right in vanilla) stays in the top-right corner.
s32 resourcesX;
s32 noOffset = 0;

//Where each race's art is split, and the strip that fills the gap: a plain
//stretch of the selection box's top edge. Indexed like the StatFluf tables
//(consoleRace()).
struct ConsoleStretch {
  s32 split;       //first column of the right part; also the strip's start
  s32 stripWidth;  //the widest strip used
};
const ConsoleStretch consoleStretch[4] = {
  { 270, 40 },  //Zerg
  { 270, 40 },  //Terran
  { 210, 40 },  //Protoss: its box has gold bumps further right
  { 270, 40 },  //replays (nconsole.pcx)
};
//The console art in use: 0 Zerg, 1 Terran, 2 Protoss, 3 replays, as chosen by
//the console loader (0x004C3950) and the StatFluf setup (0x004F4DC0).
u32 consoleRace() {
  if (*(const u32*)0x006D0F14 != 0)
    return 3;
  const u8 race = *(const u8*)0x0057F1E2;
  return race < 3 ? race : 1;
}

s32 consoleGap;  //consoleRightX - consoleLeftX: how much wider the console is

struct PanelPlacement {
  const char* name;  //the dialog's root name in its .bin
  const s32* dx;
  const s32* dy;
  const s32* dw;     //added to the width
};

const PanelPlacement panelPlacements[] = {
  { "Minimap",  &consoleLeftX,  &consoleY, &noOffset },
  { "StatData", &consoleLeftX,  &consoleY, &consoleGap },  //selected unit's info; covers the widened box
  { "StatPort", &consoleRightX, &consoleY, &noOffset },  //portrait
  { "StatBtn",  &consoleRightX, &consoleY, &noOffset },  //command card
  { "StatRes",  &resourcesX,    &noOffset, &noOffset },  //minerals, gas, supply
  { "Stat_F10", &consoleRightX, &consoleY, &noOffset },  //the MENU button
  { "TextBox",  &consoleLeftX,  &consoleY, &noOffset },  //chat input
};

//BinDlg field offsets (see SCBW/structures.h).
const u32 DLG_NEXT = 0x00;
const u32 DLG_BOUNDS = 0x04;     //s16 left, top, right, bottom
const u32 DLG_WIDTH = 0x36;      //u16 width, stored again beside the bounds
const u32 DLG_TEXT = 0x14;
const u32 DLG_TYPE = 0x22;

//In a .bin on disk, a root dialog stores left and top, then width - 1 and
//height - 1 where right and bottom go; the game adds left and top when it
//loads it. So only left and top move, and a width change goes into right and
//into the width field.
void offsetBounds(u8* dlg, s32 dx, s32 dy, s32 dw = 0) {
  s16* bounds = (s16*)(dlg + DLG_BOUNDS);
  bounds[0] = (s16)(bounds[0] + dx);
  bounds[1] = (s16)(bounds[1] + dy);
  bounds[2] = (s16)(bounds[2] + dw);
  *(u16*)(dlg + DLG_WIDTH) = (u16)(*(u16*)(dlg + DLG_WIDTH) + dw);
}

//A .bin as read from disk, before 0x004194E0 turns its offsets into pointers.
//Child rects are stored relative to the root dialog (PyMS adds the root's
//position when it displays them), so moving a panel means moving the root only.
u32 placedMask = 0;   //bit per panelPlacements entry, for debugReport()
u32 widenedCount = 0;

void __cdecl placeDialog(u8* root, u8* base) {
  if (root != base || *(u16*)(root + DLG_TYPE) != 0)
    return;
  const u32 nameOffset = *(u32*)(root + DLG_TEXT);
  if (nameOffset == 0)
    return;
  const char* name = (const char*)(base + nameOffset);

  for (const PanelPlacement& placement : panelPlacements) {
    if (strcmp(name, placement.name) != 0)
      continue;
    const s32 dx = *placement.dx;
    const s32 dy = *placement.dy;
    placedMask |= 1u << (&placement - panelPlacements);
    offsetBounds(root, dx, dy, *placement.dw);
    return;
  }
}

//In-game popups (the F10 menu and its sub-menus, objectives, victory and
//defeat, save/load, the chat-target dialogs, Ok boxes) are all opened by
//0x004F57A0 at their 640x480 positions. Move them to the screen centre.
void __cdecl centrePopup(u8* root) {
  if (resolution::inGame())
    offsetBounds(root, res_dx / 2, res_dy / 2);
}

//-------- Message lines --------//

//The message area is drawn by 0x0048CF60 at vanilla positions relative to the
//console's left part: 11 chat lines from y = 0x70 at x 10-630, the error/cheat
//line at y = 0x127 just above the console (centred in its rect), and one line
//at x 420-620, y 24 under the resources. The error line's rect is widened to
//the screen, so it is centred on the screen. Their redraw marks (0x0048CB80 and an inlined copy at 0x004B22DB)
//wrote into BW's 40-column grid, which the larger screen no longer uses.
const u16* const messageLineHeight = (const u16*)0x0064096C;
const u16* const chatLineSpacing = (const u16*)0x00640B20;
const u8* const chatNextLine = (const u8*)0x00640B58;

//Replaces 0x0048CB80: marks one message line for redraw. 0-10 are chat lines,
//11 the line under the resources, 12 the error line.
void __cdecl markMessageLine(s32 line) {
  const s32 height = *messageLineHeight;
  if (line == 12) {
    resolution::markDirty(0, 288 + consoleY, res_wm1, 295 + height + consoleY);
  }
  else if (line >= 11) {
    resolution::markDirty(416 + resourcesX, 16, 623 + resourcesX, 24 + height);
  }
  else {
    const s32 slot = (line - *chatNextLine + 11) % 11;
    const s32 top = slot * *chatLineSpacing + 0x70 + consoleY;
    resolution::markDirty(10 + consoleLeftX, top, 0x276 + consoleLeftX, top + height);
  }
}

//-------- Console image --------//

struct ConsoleImage {
  u16 width;
  u16 height;
  u8* pixels;
};
ConsoleImage* const consoleImage = (ConsoleImage*)0x00597240;

typedef void* (__stdcall *SMemAllocFn)(u32 size, const char* file, int line, int flags);
typedef BOOL (__stdcall *SMemFreeFn)(void* ptr, const char* file, int line, int flags);
const SMemAllocFn stormMemAlloc = (SMemAllocFn)0x0041006A;
const SMemFreeFn stormMemFree = (SMemFreeFn)0x00410070;

//Copies one column of the vanilla art into the wide image.
void copyColumn(u8* wide, const u8* art, s32 fromX, s32 toX) {
  for (s32 y = 0; y < 480; ++y)
    wide[(y + consoleY) * res_w + toX] = art[y * 640 + fromX];
}

//After the console image loads (0x004C3950): rebuild it at the full screen
//size. The vanilla art left of the split goes to the bottom-left corner, the
//rest to the bottom-right one, and the gap is filled with pairs of the strip,
//each one copy followed by a mirrored copy. The pairs are narrowed slightly
//so a whole number of them fits: every pair then ends on the split column,
//where the right part continues, and no copy is cut off at any width. Storm's
//allocator is used because the game frees this buffer itself.
void __cdecl widenConsoleImage() {
  if (consoleImage->pixels == NULL || consoleImage->width != 640 || consoleImage->height != 480)
    return;

  u8* wide = (u8*)stormMemAlloc(res_w * res_h, __FILE__, __LINE__, 0);
  if (wide == NULL)
    return;
  memset(wide, 0, res_w * res_h);

  const u8* art = consoleImage->pixels;
  const ConsoleStretch& stretch = consoleStretch[consoleRace()];
  const s32 gap = consoleRightX - consoleLeftX;
  for (s32 x = 0; x < stretch.split; ++x)
    copyColumn(wide, art, x, consoleLeftX + x);
  for (s32 x = stretch.split; x < 640; ++x)
    copyColumn(wide, art, x, consoleRightX + x);

  if (gap > 0) {
    const s32 halves = gap / 2;  //gap is even: widths are multiples of 32
    const s32 pairs = (halves + stretch.stripWidth - 1) / stretch.stripWidth;
    s32 toX = consoleLeftX + stretch.split;
    for (s32 pair = 0; pair < pairs; ++pair) {
      const s32 width = halves / pairs + (pair < halves % pairs ? 1 : 0);
      for (s32 i = 0; i < width; ++i)
        copyColumn(wide, art, stretch.split + i, toX++);
      for (s32 i = width - 1; i >= 0; --i)
        copyColumn(wide, art, stretch.split + i, toX++);
    }
  }

  stormMemFree(consoleImage->pixels, __FILE__, __LINE__, 0);
  consoleImage->pixels = wide;
  consoleImage->width = (u16)res_w;
  consoleImage->height = (u16)res_h;
  ++widenedCount;
}

} //unnamed namespace

const u32 Func_ConsoleHitLines = 0x004D11A0;
const u32 Func_LoadConsoleImage = 0x004C3950;

static void __cdecl consoleSetupStarting() {
  resolution::setScreenLimits(true);
}

//0x004C3BB2: call 0x4C3950 (5 bytes), the first step of console setup at
//0x004C3BB0, before any panel dialog is created and queued for its first
//redraw. The dialog layer's screen limits must already cover the whole screen
//then, or those first redraws are clipped away and never retried.
void __declspec(naked) consoleSetup_4C3BB2() {
  __asm {
    pushad
    call consoleSetupStarting
    popad
    jmp Func_LoadConsoleImage
  }
}
const u32 Ret_RelocateBin = 0x004194E6;
const u32 Func_RelocateBin = 0x004194E0;
const u32 Ret_MarkErrorLine = 0x004B2322;

//0x004C39F5: call 0x4D11A0 (5 bytes), right after the console image is stored.
//eax = 0x00597240.
void __declspec(naked) consoleImageLoaded_4C39F5() {
  __asm {
    pushad
    call widenConsoleImage
    popad
    jmp Func_ConsoleHitLines
  }
}

//0x004194E0: push ebp; mov ebp, esp; push esi; mov esi, eax (6 bytes), the
//start of the .bin relocation. eax = the dialog, ebx = the file's base.
void __declspec(naked) relocateBin_4194E0() {
  __asm {
    pushad
    push ebx
    push eax
    call placeDialog
    add esp, 8
    popad
    push ebp
    mov ebp, esp
    push esi
    mov esi, eax
    jmp Ret_RelocateBin
  }
}

//0x004F5912: call 0x4194E0, the popup loader relocating the .bin it just read.
//eax = the dialog.
void __declspec(naked) popupLoaded_4F5912() {
  __asm {
    pushad
    push eax
    call centrePopup
    add esp, 4
    popad
    jmp Func_RelocateBin
  }
}

//0x0048CB80: the whole function. eax = the message line.
void __declspec(naked) markMessageLine_48CB80() {
  __asm {
    pushad
    push eax
    call markMessageLine
    add esp, 4
    popad
    retn
  }
}

//0x004B22DB: the error line's redraw mark, inlined when a cheat is toggled.
//Runs to 0x004B2322.
void __declspec(naked) markErrorLine_4B22DB() {
  __asm {
    pushad
    push 12
    call markMessageLine
    add esp, 4
    popad
    jmp Ret_MarkErrorLine
  }
}

//0x004A456C: mov [0x512D0C], ecx (6 bytes), the end of the minimap rect
//(0x00512D00) update. The rect adds the vanilla minimap panel's top (315) but
//assumes its left is 0; add the panel's horizontal offset.
void __declspec(naked) minimapRectX_4A456C() {
  __asm {
    mov dword ptr ds:[0x00512D0C], ecx
    push eax
    mov eax, consoleLeftX
    add dword ptr ds:[0x00512D00], eax
    add dword ptr ds:[0x00512D08], eax
    pop eax
    retn
  }
}

//0x004A3D77: mov dx, [esi+4]; sub [eax], dx (7 bytes), in 0x004A3D70, which
//turns a point on the minimap into map coordinates. It subtracts the minimap
//control's position within its panel and a constant 315 for the panel's
//vanilla top, assuming the panel's left is 0; add the panel's horizontal
//offset. (The vertical one goes into that constant.) Flags from the sub feed
//the jns that follows.
void __declspec(naked) minimapPointX_4A3D77() {
  __asm {
    mov dx, word ptr [esi+4]
    add dx, word ptr consoleLeftX
    sub word ptr [eax], dx
    retn
  }
}

namespace resolution {

//Early in each game, prints what the layout code did and where the dialogs
//are, to the message area.
void debugReport() {
#if RESOLUTION_DEBUG
  const u32 frame = *(const u32*)0x0057F23C;
  if (frame != 48 && frame != 480)
    return;

  char line[256];
  sprintf_s(line, "res: placed %X (1 map 2 data 4 port 8 btn 10 res 40 f10 80 txt), widened %u, image %ux%u",
            placedMask, widenedCount, consoleImage->width, consoleImage->height);
  scbw::printText(line);

  const ResBitmap* screen = (const ResBitmap*)0x006CEFF0;
  const s32* limit = (const s32*)0x0051A15C;
  sprintf_s(line, "res: screen buffer %ux%u %s, limit %d,%d-%d,%d",
            screen->width, screen->height, screen->data == res_screenBmp.data ? "ours" : "OTHER",
            limit[0], limit[1], limit[2], limit[3]);
  scbw::printText(line);

  const u8* layer = (const u8*)0x006CEF78;
  sprintf_s(line, "res: dialog layer flags %02X rect %d,%d %dx%d",
            layer[0], *(s16*)(layer + 2), *(s16*)(layer + 4), *(s16*)(layer + 6), *(s16*)(layer + 8));
  scbw::printText(line);

  line[0] = 0;
  int count = 0;
  for (u8* dlg = *(u8**)0x006D5E34; dlg != NULL && count < 24; dlg = *(u8**)(dlg + DLG_NEXT), ++count) {
    const char* name = *(const char**)(dlg + DLG_TEXT);
    const s16* b = (const s16*)(dlg + DLG_BOUNDS);
    char item[64];
    sprintf_s(item, "%.8s@%d,%d f%X ", name ? name : "?", b[0], b[1], *(u32*)(dlg + 0x18) & 0xFFFF);
    if (strlen(line) + strlen(item) > 110) {
      scbw::printText(line);
      line[0] = 0;
    }
    strcat_s(line, item);
  }
  if (line[0])
    scbw::printText(line);
#endif
}

void injectHudHooks() {
  consoleRightX = res_dx;
  consoleGap = consoleRightX - consoleLeftX;
  consoleY = res_dy;
  resourcesX = res_dx;

  jmpPatch(relocateBin_4194E0, 0x004194E0, 1);
  callPatch(consoleSetup_4C3BB2, 0x004C3BB2);
  callPatch(consoleImageLoaded_4C39F5, 0x004C39F5);

  //The minimap rect: vertical offset in its constants, horizontal in a stub.
  memoryPatch(0x004A4539 + 2, (s32)(0x13B + consoleY));   //add edx, 0x13B
  memoryPatch(0x004A4565 + 3, (s32)(0x13A + consoleY));   //lea ecx, [edx+eax+0x13A]
  callPatch(minimapRectX_4A456C, 0x004A456C, 1);

  //Minimap clicks and drags (0x004A3D70): mov ax, 0xFEC5, i.e. -315.
  callPatch(minimapPointX_4A3D77, 0x004A3D77, 2);
  memoryPatch(0x004A3DB2 + 2, (s16)(-(315 + consoleY)));

  //Tooltips (0x00481510) are kept inside the screen: right edge 639, bottom 479.
  memoryPatch(0x004815E6 + 1, (s32)res_wm1);
  memoryPatch(0x00481620 + 1, (s32)res_hm1);
  //Command button tooltips (0x00458850) have their own right-edge clamp.
  memoryPatch(0x00458889 + 1, (s32)res_wm1);

  //In-game popups open in the screen centre.
  callPatch(popupLoaded_4F5912, 0x004F5912);

  //Message lines (0x0048CF60) keep their vanilla places relative to the
  //console's left part, and the line under the resources follows the resources.
  memoryPatch(0x0048CF79 + 1, (s32)(0x70 + consoleY));      //chat: mov edi, 0x70
  memoryPatch(0x0048CF85 + 1, (s32)(0x0A + consoleLeftX));  //mov esi, 0xA (also the error line's left)
  memoryPatch(0x0048CFCA + 7, (s16)(0x276 + consoleLeftX)); //right edge
  memoryPatch(0x0048D019 + 2, (s32)(0x127 + consoleY));     //error line: add edx, 0x127
  memoryPatch(0x0048D01F + 1, (s32)(0x127 + consoleY));     //push 0x127
  memoryPatch(0x0048D037 + 7, (s16)(res_w - 10));            //error line's right edge: centred on screen
  memoryPatch(0x0048D040 + 7, (s16)(0x127 + consoleY));
  memoryPatch(0x0048D070 + 1, (s32)(0x1A4 + resourcesX));   //top line: mov esi, 0x1A4
  memoryPatch(0x0048D090 + 7, (s16)(0x26C + resourcesX));
  jmpPatch(markMessageLine_48CB80, 0x0048CB80);
  jmpPatch(markErrorLine_4B22DB, 0x004B22DB, 2);

  //The command card area rect (496,354)-(639,479), used for the cursor.
  RECT* const commandCardRect = (RECT*)0x005136CC;
  RECT moved = *commandCardRect;
  OffsetRect(&moved, consoleRightX, consoleY);
  memoryPatch(0x005136CC, moved);

  //The decorative console pieces (StatFluf): one .bin, copied once per entry of
  //a per-race table {u16 x, y, width, height; BinDlg*} ending in x = 0xFFFF,
  //whose position and size override the .bin's. Indexed like consoleStretch.
  //Pieces left of the race's split stay left, pieces right of it move right,
  //and pieces across it (the top of the selection box) widen with the gap.
  const u32 flufTables[4] = { 0x005152A8, 0x00515300, 0x00515348, 0x00515388 };
  for (u32 race = 0; race < 4; ++race) {
    const s32 split = consoleStretch[race].split;
    for (u32 entry = flufTables[race]; *(u16*)entry != 0xFFFF; entry += 12) {
      const s32 x = *(u16*)entry;
      const s32 width = *(u16*)(entry + 4);
      if (x >= split)
        memoryPatch(entry, (u16)(x + consoleRightX));
      else if (x + width > split)
        memoryPatch(entry + 4, (u16)(width + consoleRightX - consoleLeftX));
      else
        memoryPatch(entry, (u16)(x + consoleLeftX));
      memoryPatch(entry + 2, (u16)(*(u16*)(entry + 2) + consoleY));
    }
  }

  //The dialog blitters are compiled once at startup with SCodeCompile
  //(0x00417D70, 0x00417DA3) for at most 0xA0 iterations of 4 pixels, i.e. 640
  //wide. A wider dialog (the selection box, the pieces over it) would run past
  //the compiled code.
  memoryPatch(0x00417D48 + 1, (s32)(res_w / 4));
  memoryPatch(0x00417D92 + 1, (s32)(res_w / 4));

  //Storm's dirty-cell geometry, set when the screen mask is rebuilt
  //(0x0041D470): STransSetDirtyArrayInfo(640, 480, 16, 16).
  memoryPatch(0x0041D52C + 1, (s32)res_h);
  memoryPatch(0x0041D531 + 1, (s32)res_w);
}

} //resolution
