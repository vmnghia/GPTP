//Larger game view ("resolution hack").
//
//Brood War 1.16.1 renders into a fixed 640x480 buffer, and the size is baked
//into dozens of routines: the terrain cache, the 16x16 dirty-cell grid, fog
//of war, cursor clipping, screen scrolling and the HUD. This module makes the
//game run at RESOLUTION_WIDTH x RESOLUTION_HEIGHT instead.
//
//The patch set is a port of Hellinsect's Resolution Expander v6 (2009), worked
//out by disassembling its DLL. See docs/resolution.md for how each site was
//found and which parts are verified.
//
//The view is cosmetic: nothing here feeds game state, so it cannot desync.
//
//Needs a DirectDraw wrapper that accepts arbitrary display modes, such as
//cnc-ddraw. WMode assumes 640x480, so it is not offered while this is on.

#pragma once
#include "../../types.h"
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

//Debug switch. 0 builds the plugin with no resolution patches at all.
#define RESOLUTION_HACK_ENABLED 1

//Prints a layout report in the message area early in each game.
#define RESOLUTION_DEBUG 0

//Width must be a multiple of 32 and height a multiple of 16. Anything from
//640x480 upward; the Expander's own list topped out at 1440x900.
#define RESOLUTION_WIDTH  1280
#define RESOLUTION_HEIGHT 720

static_assert(RESOLUTION_WIDTH % 32 == 0, "RESOLUTION_WIDTH must be a multiple of 32");
static_assert(RESOLUTION_HEIGHT % 16 == 0, "RESOLUTION_HEIGHT must be a multiple of 16");
static_assert(RESOLUTION_WIDTH >= 640 && RESOLUTION_HEIGHT >= 480, "Resolution must be at least 640x480");

//Everything below is read from inline asm, so it lives in plain globals.
//Vanilla values are in the comments.

//A graphics::Bitmap-compatible header: {u16 width, u16 height, u8* data}.
struct ResBitmap {
  u16 width;
  u16 height;
  u8* data;
};

extern "C" {

extern s32 res_w;             //640
extern s32 res_h;             //480
extern s32 res_wm1;           //639
extern s32 res_hm1;           //479
extern s32 res_wHalf;         //320
extern s32 res_hHalf;         //240
extern s32 res_dx;            //0    extra width,  res_w - 640
extern s32 res_dy;            //0    extra height, res_h - 480
extern s32 res_viewH;         //400  game view height (the whole screen in the trial layout)
extern s32 res_viewHm1;       //399
extern s32 res_w64;           //704

//Where the 640x480 menus are drawn on the larger screen: centred.
extern s32 res_menuX;         //0
extern s32 res_menuY;         //0


//16x16 dirty-cell grid (vanilla: 40 x 30 bytes at 0x006CEFF8).
extern s32 res_cellCols;      //40
extern s32 res_cellDwords;    //300
extern u8* res_cells;
extern u32* res_cellRows;     //row start offsets into res_cells

//Terrain cache, a ring buffer of 32x32 tiles (vanilla 672 x 448).
extern s32 res_cacheW;        //672
extern s32 res_cacheH;        //448
extern s32 res_cacheCols;     //21
extern s32 res_cacheColsP1;   //22
extern s32 res_cacheRows;     //14
extern s32 res_cacheSize;     //0x49800
extern s32 res_cacheSizeNeg;  //-0x49800
extern s32 res_cacheRow32;    //672 * 32, one row of tiles
extern s32 res_cacheLastRow;  //res_cacheSize - res_cacheRow32
extern s32 res_cacheSkip16;   //672 * 16 - 640
extern u8* res_tileCache;

//Fog of war darkness grids.
extern s32 res_fogW;          //88
extern s32 res_fogH;          //60
extern s32 res_fogW4m4;       //res_fogW * 4 - 4
extern s32 res_fogW3p4;       //res_fogW * 3 + 4
extern s32 res_fogCW;         //24
extern s32 res_fogCH;         //17
extern s32 res_fogCWp1;       //25
extern s32 res_fogCWm1;       //23
extern s32 res_fogCDwords;    //0x66

//The enlarged screen buffer, swapped into gameScreenBuffer (0x006CEFF0).
extern ResBitmap res_screenBmp;

//Storm's scratch buffer for the dirty-cell blit, sized for the largest mode.
extern u8* res_stormBuf;
//The console transparency mask, rebuilt at the new size.
extern u8* res_hudMask;

} //extern "C"

namespace resolution {

//Internal to the resolution module (resolution*.cpp).
bool init();
void markDirty(s32 left, s32 top, s32 right, s32 bottom);
bool isRectDirty(s32 left, s32 top, s32 right, s32 bottom);
void buildHudMask(const u8* src);
void onScreenUpdate();
void setScreenLimits(bool wide);
void clearSurface();
void injectTerrainHooks();
void injectFogHooks();
void injectHudHooks();
void debugReport();

//Cursor calls routed through StarCraft.exe's own import slots, so a
//DirectDraw wrapper that hooks them (cnc-ddraw scales mouse coordinates this
//way) sees them. Calling the plugin's own imports would bypass the wrapper and
//clip the real cursor to the top-left res_w x res_h pixels of the monitor.
inline BOOL clipCursor(const RECT* rect) {
  return (*(BOOL (WINAPI**)(const RECT*))0x004FE37C)(rect);
}
inline BOOL setCursorPos(int x, int y) {
  return (*(BOOL (WINAPI**)(int, int))0x004FE2CC)(x, y);
}

//Whether a game is running (the game layer, screenLayers[5], is active).
//Outside a game the 640x480 menus are shown centred.
inline bool inGame() {
  return *(const u8*)0x006CEFB4 != 0;
}

//True when the larger view is compiled in.
constexpr bool enabled() { return RESOLUTION_HACK_ENABLED != 0; }

//Game view size in pixels (the part above the console): 640x400 in vanilla.
inline s32 viewWidth()  { return enabled() ? res_w : 640; }
inline s32 viewHeight() { return enabled() ? res_viewH : 400; }

}

namespace hooks {

void injectResolutionHooks();

}
