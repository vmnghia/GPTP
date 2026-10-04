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
#include <SCBW/console_raise.h>
#include <SCBW/selection_ext.h>
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

//The selection box is raised for the taller selection panel (Console art
//reference in docs/resolution.md). A replacement console drawn with a tall
//box already turns this off, and only the panel moves.
const bool RAISE_SELECTION_BOX = true;

//Where each race's art is split, and the strip that fills the gap: a plain
//stretch of the selection box's top edge. Also where the first button sits in
//the race's vanilla command card (496,354). Indexed like the StatFluf tables
//(consoleRace()).
struct ConsoleStretch {
  s32 split;       //first column of the right part; also the strip's start
  s32 stripWidth;  //the widest strip used
  s32 buttonX;     //first button's offset in the vanilla card
  s32 buttonY;
};
const ConsoleStretch consoleStretch[4] = {
  { 270, 40, 4, 7 },  //Zerg
  { 270, 40, 9, 4 },  //Terran
  { 210, 40, 9, 4 },  //Protoss: its box has gold bumps further right
  { 270, 40, 9, 4 },  //replays (nconsole.pcx), which keep the vanilla card
};

//The races' command cards (rez\statbtn[tpz].bin) are 5x3 and dense: 15
//buttons of 36x34 touching each other. The frame is rebuilt from the vanilla
//3x3 one (cells 46 x 40 apart): its left border, each cell's 36 px of screen
//(the middle cell three times) and its right border, then each row's 34 px of
//screen without the gaps between rows. That makes it 196x114, flush with the
//bottom-right corner, and moves the portrait and the MENU button left by the
//difference. Replays keep the vanilla card (statbtnn.bin) and layout.
const s32 VANILLA_CARD_LEFT = 496, VANILLA_CARD_TOP = 354;
const s32 BUTTON_WIDTH = 36, BUTTON_HEIGHT = 34;
const s32 CARD_WIDTH = 144 + 2 * BUTTON_WIDTH - 2 * (46 - BUTTON_WIDTH);
const s32 CARD_HEIGHT = 126 - 2 * (40 - BUTTON_HEIGHT);
const s32 CARD_LEFT = 640 - CARD_WIDTH, CARD_TOP = 480 - CARD_HEIGHT;
const s32 CARD_DROP = CARD_TOP - VANILLA_CARD_TOP;  //how much lower the frame's top sits
const s32 PORTRAIT_SHIFT = VANILLA_CARD_LEFT - CARD_LEFT;

//The vanilla art column shown at column i of the dense card.
s32 cardSourceColumn(s32 i, const ConsoleStretch& stretch) {
  static const s32 sourceCell[5] = { 0, 1, 1, 1, 2 };
  if (i < stretch.buttonX)
    return VANILLA_CARD_LEFT + i;
  i -= stretch.buttonX;
  if (i < 5 * BUTTON_WIDTH)
    return VANILLA_CARD_LEFT + stretch.buttonX + 46 * sourceCell[i / BUTTON_WIDTH] + i % BUTTON_WIDTH;
  return VANILLA_CARD_LEFT + stretch.buttonX + 2 * 46 + BUTTON_WIDTH + (i - 5 * BUTTON_WIDTH);
}

//The vanilla art row shown at row y in the card's columns, or -1 for none.
s32 cardSourceRow(s32 y, const ConsoleStretch& stretch) {
  y -= CARD_DROP;
  const s32 firstRow = VANILLA_CARD_TOP + stretch.buttonY;
  if (y < firstRow)
    return y;  //-1 and up above the frame: transparent
  y -= firstRow;
  if (y < 3 * BUTTON_HEIGHT)
    return firstRow + 40 * (y / BUTTON_HEIGHT) + y % BUTTON_HEIGHT;
  return firstRow + 2 * 40 + BUTTON_HEIGHT + (y - 3 * BUTTON_HEIGHT);
}

//The console art in use: 0 Zerg, 1 Terran, 2 Protoss, 3 replays, as chosen by
//the console loader (0x004C3950) and the StatFluf setup (0x004F4DC0).
u32 consoleRace() {
  if (*(const u32*)0x006D0F14 != 0)
    return 3;
  const u8 race = *(const u8*)0x0057F1E2;
  return race < 3 ? race : 1;
}

s32 consoleGap;  //consoleRightX - consoleLeftX: how much wider the console is

bool usesExtendedCard(u32 race) {
  return race != 3;
}

//How far left the portrait part moves for a race's console.
s32 portraitShift(u32 race) {
  if (!usesExtendedCard(race))
    return 0;
  return PORTRAIT_SHIFT < consoleGap ? PORTRAIT_SHIFT : consoleGap;
}

//The layout of the console being set up, from consoleSetupStarting().
u32 layoutRace = 1;
s32 consolePortraitX;  //the portrait and the MENU button
s32 consoleCardX;      //the command card
s32 statDataGrowth;    //how much wider the selection box is

//Where a column of the vanilla art goes on the screen. Columns of the dense
//card are rearranged (cardSourceColumn); this gives their proportional place,
//which is enough for the decorative pieces' rects.
s32 mapConsoleX(s32 x, u32 race) {
  const ConsoleStretch& stretch = consoleStretch[race];
  if (x < stretch.split)
    return consoleLeftX + x;
  if (!usesExtendedCard(race))
    return consoleRightX + x;
  if (x < VANILLA_CARD_LEFT)
    return consoleRightX - portraitShift(race) + x;
  return consoleRightX + CARD_LEFT + (x - VANILLA_CARD_LEFT) * CARD_WIDTH / 144;
}

//At console setup: the layout for the console art and card in use.
void setConsoleLayout() {
  layoutRace = consoleRace();
  const s32 shift = portraitShift(layoutRace);
  consolePortraitX = consoleRightX - shift;
  consoleCardX = consoleRightX;
  statDataGrowth = consoleGap - shift;

  //The command card's rect (0x005136CC, vanilla (496,354)-(639,479)), used for
  //the cursor.
  RECT card;
  if (usesExtendedCard(layoutRace))
    SetRect(&card, consoleCardX + CARD_LEFT, consoleY + CARD_TOP,
            consoleCardX + CARD_LEFT + CARD_WIDTH - 1, consoleY + CARD_TOP + CARD_HEIGHT - 1);
  else
    SetRect(&card, consoleRightX + 496, consoleY + 354, consoleRightX + 639, consoleY + 479);
  memoryPatch(0x005136CC, card);
}

//-------- Decorative pieces (StatFluf) --------//

//One .bin, copied once per entry of a per-race table whose position and size
//override the .bin's. The game finds each race's table through a pointer
//table, so the plugin gives it its own: each vanilla piece mapped like the art
//(mapConsoleX), and, for the extended command card, with the card's rect cut
//out, since a piece over the buttons repaints the art over them. A piece can
//split into up to four around the card.
struct FlufEntry {
  u16 x, y, width, height;
  u32 dialog;  //set by the game
};
static_assert(sizeof(FlufEntry) == 12, "StatFluf table entries are 12 bytes");

const u32 flufVanillaTables[4] = { 0x005152A8, 0x00515300, 0x00515348, 0x00515388 };
const u32 FLUF_TABLE_POINTERS = 0x005153E8;
const u32 MAX_FLUF_PIECES = 47;	//raised pieces split in up to three
FlufEntry flufTables[4][MAX_FLUF_PIECES + 1];

void addFluf(FlufEntry*& out, const FlufEntry* end, s32 left, s32 top, s32 right, s32 bottom) {
  if (left >= right || top >= bottom || out == end)
    return;
  out->x = (u16)left;
  out->y = (u16)top;
  out->width = (u16)(right - left);
  out->height = (u16)(bottom - top);
  out->dialog = 0;
  ++out;
}

void buildFlufTable(u32 race) {
  FlufEntry* out = flufTables[race];
  const FlufEntry* const end = out + MAX_FLUF_PIECES;
  const bool cutCard = usesExtendedCard(race);
  const s32 cardLeft = consoleRightX + CARD_LEFT;
  const s32 cardTop = consoleY + CARD_TOP;
  const s32 cardRight = cardLeft + CARD_WIDTH;
  const s32 cardBottom = cardTop + CARD_HEIGHT;

  for (const FlufEntry* vanilla = (const FlufEntry*)flufVanillaTables[race]; vanilla->x != 0xFFFF; ++vanilla) {
    //The raised selection box: the part of the piece over it grows up.
    consoleraise::Piece raised[3];
    const consoleraise::Piece whole = { vanilla->x, vanilla->y, vanilla->x + vanilla->width, vanilla->y + vanilla->height };
    u32 count = 1;
    raised[0] = whole;
    if (RAISE_SELECTION_BOX)
      count = consoleraise::raisePiece(whole, consoleraise::SPANS[race], consoleraise::CUT_ROW,
                                       selext::PANEL_RAISE, raised);
    for (u32 i = 0; i < count; ++i) {
      FlufEntry pieceEntry;
      pieceEntry.x = (u16)raised[i].left;
      pieceEntry.y = (u16)raised[i].top;
      pieceEntry.width = (u16)(raised[i].right - raised[i].left);
      pieceEntry.height = (u16)(raised[i].bottom - raised[i].top);
      const FlufEntry* const piece = &pieceEntry;
      const s32 left = mapConsoleX(piece->x, race);
      const s32 lastX = piece->x + piece->width - 1;
      const s32 right = cutCard && lastX >= VANILLA_CARD_LEFT
                          ? mapConsoleX(lastX + 1, race)  //proportional: map the end itself
                          : mapConsoleX(lastX, race) + 1;
      const s32 top = piece->y + consoleY;
      s32 bottom = top + piece->height;
      if (cutCard && piece->x + piece->width > VANILLA_CARD_LEFT)
        bottom += CARD_DROP;
      if (!cutCard || right <= cardLeft || left >= cardRight || bottom <= cardTop || top >= cardBottom) {
        addFluf(out, end, left, top, right, bottom);
        continue;
      }
      const s32 midTop = top > cardTop ? top : cardTop;
      const s32 midBottom = bottom < cardBottom ? bottom : cardBottom;
      addFluf(out, end, left, top, right, cardTop);          //above the card
      addFluf(out, end, left, cardBottom, right, bottom);    //below it
      addFluf(out, end, left, midTop, cardLeft, midBottom);  //left of it
      addFluf(out, end, cardRight, midTop, right, midBottom);//right of it
    }
  }
  out->x = 0xFFFF;
  memoryPatch(FLUF_TABLE_POINTERS + race * 4, (u32)flufTables[race]);
}

struct PanelPlacement {
  const char* name;  //the dialog's root name in its .bin
  const s32* dx;
  const s32* dy;
  const s32* dw;     //added to the width
};

const PanelPlacement panelPlacements[] = {
  { "Minimap",  &consoleLeftX,  &consoleY, &noOffset },
  { "StatData", &consoleLeftX,  &consoleY, &statDataGrowth },  //selected unit's info; covers the widened box
  { "StatPort", &consolePortraitX, &consoleY, &noOffset },  //portrait
  { "StatBtn",  &consoleCardX,  &consoleY, &noOffset },  //command card
  { "StatRes",  &resourcesX,    &noOffset, &noOffset },  //minerals, gas, supply
  { "Stat_F10", &consolePortraitX, &consoleY, &noOffset },  //the MENU button
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

//The taller selection panel: StatData starts PANEL_RAISE higher and is as
//much taller (the root stores height - 1 where bottom goes, and the height
//again at 0x38); its controls are laid out by the selection code.
void raiseStatData(u8* root, u8* base) {
  s16* bounds = (s16*)(root + DLG_BOUNDS);
  bounds[1] = (s16)(bounds[1] - selext::PANEL_RAISE);
  bounds[3] = (s16)(bounds[3] + selext::PANEL_RAISE);
  *(u16*)(root + DLG_WIDTH + 2) = (u16)(*(u16*)(root + DLG_WIDTH + 2) + selext::PANEL_RAISE);
  selext::layOutStatDataBin(base);
}

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
    if (strcmp(name, "StatData") == 0)
      raiseStatData(root, base);
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
//the screen, so it is centred on the screen. Their redraw marks (0x0048CB80
//and an inlined copy at 0x004B22DB) wrote into BW's 40-column grid, which the
//larger screen no longer uses.
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
//rest to the bottom-right one (the portrait part shifted left for the wider
//command card, whose frame is rebuilt dense, per cardSourceColumn/Row()), and
//the gap is filled with pairs of the strip,
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

  //Step 1 of the console art (docs/resolution.md, Console art reference):
  //raise the selection box, in the vanilla art, so the widening below
  //copies the raised box and its strip like any other art.
  if (RAISE_SELECTION_BOX)
    consoleraise::raiseArt(consoleImage->pixels, 640, 480, consoleraise::SPANS[layoutRace],
                           consoleraise::CUT_ROW, selext::PANEL_RAISE);
  memset(wide, 0, res_w * res_h);

  const u8* art = consoleImage->pixels;
  const ConsoleStretch& stretch = consoleStretch[layoutRace];
  const s32 shift = portraitShift(layoutRace);
  const s32 gap = consoleRightX - consoleLeftX - shift;
  const bool denseCard = usesExtendedCard(layoutRace);
  for (s32 x = 0; x < (denseCard ? VANILLA_CARD_LEFT : 640); ++x)
    copyColumn(wide, art, x, mapConsoleX(x, layoutRace));
  if (denseCard) {
    for (s32 i = 0; i < CARD_WIDTH; ++i) {
      const s32 fromX = cardSourceColumn(i, stretch);
      u8* column = wide + consoleY * res_w + consoleRightX + CARD_LEFT + i;
      for (s32 y = 0; y < 480; ++y) {
        const s32 fromY = cardSourceRow(y, stretch);
        column[y * res_w] = fromY < 0 ? 0 : art[fromY * 640 + fromX];
      }
    }
  }

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
  setConsoleLayout();
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

void injectConsoleLayoutHooks() {
  consoleRightX = res_dx;
  consoleGap = consoleRightX - consoleLeftX;
  consoleY = res_dy;
  resourcesX = res_dx;

  jmpPatch(relocateBin_4194E0, 0x004194E0, 1);
  callPatch(consoleSetup_4C3BB2, 0x004C3BB2);
  callPatch(consoleImageLoaded_4C39F5, 0x004C39F5);

  //The decorative console pieces, per race.
  for (u32 race = 0; race < 4; ++race)
    buildFlufTable(race);
}

void injectHudHooks() {
  injectConsoleLayoutHooks();

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
