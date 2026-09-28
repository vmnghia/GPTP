#include "resolution.h"
#include <SCBW/api.h>
#include <cstdio>
#include <cstring>
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

//Runtime state for the larger view. See resolution.h for what each value is;
//the vanilla 640x480 equivalents are listed there.

extern "C" {

s32 res_w, res_h, res_wm1, res_hm1, res_wHalf, res_hHalf;
s32 res_dx, res_dy, res_viewH, res_viewHm1, res_w64;
s32 res_menuX, res_menuY;

s32 res_cellCols, res_cellDwords;
u8* res_cells;
u32* res_cellRows;

s32 res_cacheW, res_cacheH, res_cacheCols, res_cacheColsP1, res_cacheRows;
s32 res_cacheSize, res_cacheSizeNeg, res_cacheRow32, res_cacheLastRow, res_cacheSkip16;
u8* res_tileCache;

s32 res_fogW, res_fogH, res_fogW4m4, res_fogW3p4;
s32 res_fogCW, res_fogCH, res_fogCWp1, res_fogCWm1, res_fogCDwords;

ResBitmap res_screenBmp;

u8* res_stormBuf;
u8* res_hudMask;

} //extern "C"

namespace {

//Zeroed memory with a guard band on both sides. The Expander offset each of
//its buffers a few bytes into the allocation, so some routines read slightly
//before the start; the guard makes that harmless.
u8* allocBuffer(size_t size, size_t leadingGuard = 64) {
  const size_t total = size + leadingGuard + 4096;
  u8* block = (u8*)VirtualAlloc(NULL, total, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  return block ? block + leadingGuard : NULL;
}

//-------- Settings --------//

const char* const iniName = "Manifold.ini";

const char* const defaultIni =
  "; StarCraft: Manifold settings.\r\n"
  "\r\n"
  "[Resolution]\r\n"
  "; Size of the game screen in pixels. The 640x480 menus are shown centred.\r\n"
  "; Width must be a multiple of 32 and height a multiple of 16 (other values\r\n"
  "; are rounded down), from 640x480 up to 2048x1536.\r\n"
  "; 640x480 plays at the vanilla size and offers window mode (WMode).\r\n"
  "; Anything larger needs a DirectDraw wrapper such as cnc-ddraw.\r\n"
  ";   16:9   1024x576  1280x720  1536x864  1920x1072\r\n"
  ";   16:10  1280x800  1440x896  1920x1200\r\n"
  ";   4:3    1024x768\r\n"
  "Width=1280\r\n"
  "Height=720\r\n";
static_assert(RESOLUTION_DEFAULT_WIDTH == 1280 && RESOLUTION_DEFAULT_HEIGHT == 720,
              "Update defaultIni with the new default size");

s32 settingWidth = RESOLUTION_DEFAULT_WIDTH;
s32 settingHeight = RESOLUTION_DEFAULT_HEIGHT;
s32 askedWidth = RESOLUTION_DEFAULT_WIDTH;
s32 askedHeight = RESOLUTION_DEFAULT_HEIGHT;
bool iniCreated = false;

s32 fitSize(s32 value, s32 minimum, s32 maximum, s32 multiple) {
  if (value < minimum) value = minimum;
  if (value > maximum) value = maximum;
  return value / multiple * multiple;
}

} //unnamed namespace

namespace resolution {

bool active = false;

void loadSettings(const char* exePath) {
  char path[MAX_PATH];
  strncpy_s(path, exePath, _TRUNCATE);
  char* slash = strrchr(path, '\\');
  if (slash == NULL || strlen(iniName) >= sizeof(path) - (slash + 1 - path))
    return;
  strcpy_s(slash + 1, sizeof(path) - (slash + 1 - path), iniName);

  if (GetFileAttributes(path) == INVALID_FILE_ATTRIBUTES) {
    HANDLE file = CreateFile(path, GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file != INVALID_HANDLE_VALUE) {
      DWORD written;
      WriteFile(file, defaultIni, (DWORD)strlen(defaultIni), &written, NULL);
      CloseHandle(file);
      iniCreated = true;
    }
  }

  askedWidth = (s32)GetPrivateProfileInt("Resolution", "Width", RESOLUTION_DEFAULT_WIDTH, path);
  askedHeight = (s32)GetPrivateProfileInt("Resolution", "Height", RESOLUTION_DEFAULT_HEIGHT, path);
  settingWidth = fitSize(askedWidth, 640, RESOLUTION_MAX_WIDTH, 32);
  settingHeight = fitSize(askedHeight, 480, RESOLUTION_MAX_HEIGHT, 16);
  active = settingWidth > 640 || settingHeight > 480;
}

void printSettings() {
  char line[160];
  if (!enabled())
    sprintf_s(line, "Resolution: 640x480 (vanilla)");
  else
    sprintf_s(line, "Resolution: %dx%d", res_w, res_h);

  if (askedWidth != settingWidth || askedHeight != settingHeight) {
    char note[96];
    sprintf_s(note, " - %s asked for %dx%d", iniName, askedWidth, askedHeight);
    strcat_s(line, note);
  }
  else if (iniCreated) {
    strcat_s(line, " - created ");
    strcat_s(line, iniName);
  }
  scbw::printText(line);
}

//Computes every derived size and allocates the enlarged buffers. Must run
//before the game creates its screen, i.e. at plugin load.
bool init() {
  const s32 w = settingWidth;
  const s32 h = settingHeight;

  res_w = w;
  res_h = h;
  res_wm1 = w - 1;
  res_hm1 = h - 1;
  res_wHalf = w / 2;
  res_hHalf = h / 2;
  res_dx = w - 640;
  res_dy = h - 480;
  res_w64 = w + 64;
  res_menuX = res_dx / 2;
  res_menuY = res_dy / 2;

  //Trial layout: the console stays at its vanilla place (the top-left 640x480)
  //and the game view is the whole screen behind it. (The Expander moved the
  //console to the bottom and used res_h - 80.)
  res_viewH = h;
  res_viewHm1 = res_viewH - 1;

  res_cellCols = w / 16;
  res_cellDwords = (w / 16) * (h / 16) / 4;

  //The cache holds the view plus slack for partial tiles, as vanilla's 672 x
  //448 does for a 640 x 400 view.
  res_cacheW = w + 32;
  res_cacheH = (res_viewH + 48 + 31) / 32 * 32;
  res_cacheCols = res_cacheW / 32;
  res_cacheColsP1 = res_cacheCols + 1;
  res_cacheRows = res_cacheH / 32;
  res_cacheSize = res_cacheW * res_cacheH;
  res_cacheSizeNeg = -res_cacheSize;
  res_cacheRow32 = res_cacheW * 32;
  res_cacheLastRow = res_cacheSize - res_cacheRow32;
  res_cacheSkip16 = res_cacheW * 16 - w;

  //Fog grids follow the cache: vanilla 88 x 60 fine and 24 x 17 coarse for
  //21 x 14 cache tiles.
  res_fogW = res_cacheCols * 4 + 4;
  res_fogH = res_cacheRows * 4 + 4;
  res_fogW4m4 = res_fogW * 4 - 4;
  res_fogW3p4 = res_fogW * 3 + 4;
  res_fogCW = res_cacheCols + 3;
  res_fogCH = res_cacheRows + 3;
  res_fogCWp1 = res_fogCW + 1;
  res_fogCWm1 = res_fogCW - 1;
  res_fogCDwords = res_fogCW * res_fogCH / 4;

  //Sizes follow the Expander, which were generous; the guards add slack.
  res_tileCache = allocBuffer(res_cacheSize);
  res_hudMask = allocBuffer((w / 16) * h * 2);
  res_cells = allocBuffer((w / 16) * (h / 16) * 4, 2 * w + 64);
  res_stormBuf = allocBuffer(w * h > 0xFD200 ? w * h : 0xFD200);
  res_cellRows = (u32*)allocBuffer((h / 16 + 2) * sizeof(u32));
  res_screenBmp.data = allocBuffer(w * h * 2);

  if (!res_tileCache || !res_hudMask || !res_cells || !res_stormBuf || !res_cellRows
      || !res_screenBmp.data)
    return false;

  res_screenBmp.width = (u16)w;
  res_screenBmp.height = (u16)h;

  for (s32 row = 0; row <= h / 16; ++row)
    res_cellRows[row] = row * res_cellCols;

  return true;
}

//-------- Dirty-cell grid --------//

//Replaces 0x0041E0D0: marks every 16x16 cell touched by an inclusive rect.
void markDirty(s32 left, s32 top, s32 right, s32 bottom) {
  //A request to redraw the whole vanilla view is widened to the new one.
  if (left == 0 && top == 0
      && ((right == 640 && bottom == 400) || (right == 639 && bottom == 399))) {
    right = res_w;
    bottom = res_viewH;
  }

  res_cells[0] = 1;  //The Expander always dirties the first cell; kept as-is.

  if (left < 0) left = 0;
  else if (left >= res_w) return;
  if (right < 0) return;
  if (right > res_wm1) right = res_wm1;

  if (top < 0) top = 0;
  else if (top >= res_h) return;
  if (bottom < 0) return;
  if (bottom > res_hm1) bottom = res_hm1;

  const s32 firstCol = left / 16;
  const s32 count = right / 16 - firstCol + 1;
  u8* row = res_cells + (top / 16) * res_cellCols + firstCol;
  for (s32 r = top / 16; r <= bottom / 16; ++r, row += res_cellCols)
    memset(row, 1, count);
}

//Replaces 0x0041DE20: whether any cell in [left, right) x [top, bottom) is dirty.
bool isRectDirty(s32 left, s32 top, s32 right, s32 bottom) {
  const s32 firstCol = left / 16;
  const s32 lastRow = (bottom - 1) / 16;
  const s32 count = (right - 1) / 16 - firstCol + 1;
  const u8* row = res_cells + (top / 16) * res_cellCols + firstCol;
  for (s32 r = top / 16; r <= lastRow; ++r, row += res_cellCols) {
    for (s32 i = 0; i < count; ++i)
      if (row[i]) return true;
  }
  return false;
}

//-------- Cursor clipping --------//

namespace {

RECT* const cursorClipRect = (RECT*)0x006CDDB0;
//First byte of the game layer (screenLayers[5]); non-zero while in a game.
const u8* const gameLayerActive = (const u8*)0x006CEFB4;

bool clipIsForGame = true;  //Starts true so the first menu frame resets it.

} //unnamed namespace

//The screen limits the dialog layer clips its updates to (read in
//0x0041C1xx..0x0041CAxx): ScrLimit (0,0)-(639,479) and ScrSize (0,0)-(640,480).
//In a game, dialogs moved outside 640x480 (the console) stay queued for a
//redraw that never happens unless these cover the whole screen. A dialog is
//queued only once, so the limits must already be wide when the console is
//created. Outside a game, menus and loading screens keep the vanilla limits.
void setScreenLimits(bool wide) {
  RECT* const scrLimit = (RECT*)0x0051A15C;
  RECT* const scrSize = (RECT*)0x0051A16C;
  const s32 w = wide ? res_w : 640;
  const s32 h = wide ? res_h : 480;
  SetRect(scrLimit, 0, 0, w - 1, h - 1);
  SetRect(scrSize, 0, 0, w, h);
}

//Runs once per screen update (hooked at 0x0041CF1E). The game clips the
//cursor to 640x480; while in a game, clip it to the whole larger screen.
void onScreenUpdate() {
  //Normally already set when the console is created (resolution_hud.cpp).
  setScreenLimits(*gameLayerActive != 0);

  if (*gameLayerActive) {
    if (!clipIsForGame) {
      cursorClipRect->right = cursorClipRect->left + res_w;
      cursorClipRect->bottom = cursorClipRect->top + res_h;
      setCursorPos(res_wHalf, res_hHalf);
      clipCursor(cursorClipRect);
      clipIsForGame = true;
    }
  }
  else if (clipIsForGame) {
    //Leaving a game: back to the 640x480 menu area, which is shown centred.
    //(The menu blit clears the rest of the screen, so the last game frame
    //doesn't linger around the menu.)
    memset(res_screenBmp.data, 0, res_w * res_h);
    memset(res_cells, 1, res_cellCols * (res_h / 16));
    clearSurface();
    cursorClipRect->right = cursorClipRect->left + 640;
    cursorClipRect->bottom = cursorClipRect->top + 480;
    RECT menu = *cursorClipRect;
    OffsetRect(&menu, res_menuX, res_menuY);
    clipCursor(&menu);
    clipIsForGame = false;
  }
}

//-------- Console transparency mask --------//

//Storm's layer masks are run-length rows: alternating transparent and opaque
//run lengths (bytes), where a run may continue as "0, n" pairs, each row ends
//with a zero word and the mask ends with a zero dword.
//
//The console's mask is 640x480. Rebuilt at the new size for the trial layout:
//every row gets res_dx transparent pixels on the right, and res_dy fully
//transparent rows follow, so the game shows everywhere outside the console.
//(The Expander instead put res_dy rows on top and widened the run crossing
//x = 400, splitting the console across the bottom of the screen.)
void buildHudMask(const u8* src) {
  u8* out = res_hudMask;

  //A transparent run of any length followed by an empty opaque run.
  const auto emitTransparent = [&out](s32 run) {
    while (run > 0xFC) {
      *out++ = 0xFC;
      *out++ = 0;
      run -= 0xFC;
    }
    *out++ = (u8)run;
    *out++ = 0;
  };

  while (*(const u32*)src != 0) {
    while (*(const s16*)src != 0) {
      *out++ = *src++;
      *out++ = *src++;
    }
    if (res_dx > 0)
      emitTransparent(res_dx);
    *out++ = 0;
    *out++ = 0;
    src += 2;
  }

  for (s32 row = 0; row < res_dy; ++row) {
    emitTransparent(res_w);
    *out++ = 0;
    *out++ = 0;
  }

  *(u32*)out = 0;
}

} //resolution
