#pragma once
#define _USE_MATH_DEFINES
#include "SCBW/structures.h"
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <iterator>
#include <vector>

using std::vector;

typedef uint8_t *LPBYTE;

struct GrpHeader
{
    uint16_t frameCount;
    int16_t width;
    int16_t height;
};

struct GRPHeader
{
    uint16_t frames;
    uint16_t maxWidth;
    uint16_t maxHeight;
};

struct FrameHeader
{
    uint8_t left;
    uint8_t top;
    uint8_t width;
    uint8_t height;
    uint32_t offset;
};

struct FrameData
{
    uint16_t *lpRowOffsets;
    uint16_t *lpRowSizes;
    uint8_t **lpRowData;
    uint32_t size;
};

struct GradientStop
{
    float position;
    int color;
};

int16_t *generateBeamAngle(int x, int y, float angle, int length, int thickness, int16_t *buffer, int nColors = 10);
GrpHead *createGRP(int16_t *imageData, uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, bool noCompress,
                   uint32_t *grpSize);
uint8_t *generateGrp(int16_t *imageData, uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, bool noCompress,
                     uint32_t *grpSize);

// Debug switches - set both to 0 for normal behaviour.
//
// BEAM_DEBUG_FIXED_AIM draws a fixed due-east beam with a hardcoded endpoint,
// bypassing both the unit's aim and Brood War's angle table. It isolates the
// rasterize -> GRP -> overlay path: if the forced beam appears, rendering works
// and the problem is upstream in the aiming. Confirmed working, so it is off by
// default now - turn it back on to re-isolate the renderer.
//
// BEAM_DEBUG_PRINT shows the weapon id of each shot and the aim values on the
// in-game message area, capped at a few messages so it does not flood the
// screen. Deliberately not GPTP::logger: that compiles out unless _DEBUG is
// defined, and writes to StarCraft's working directory rather than anywhere
// obvious. printText works in every configuration.
#define BEAM_DEBUG_FIXED_AIM 0
#define BEAM_DEBUG_PRINT 1

// Which rendering path to use. 1 (the GRP path) is the working default.
//
// 0 queues the beam with the graphics module instead, drawing through the
// BWAPI-derived draw hook. It lifts the 255px cap, but it was tried and found
// wanting on three counts and is kept only for reference:
//
//  - No depth. The draw hook runs after the game has composited the frame, so
//    beams always paint over everything. An air unit firing at a ground target
//    should be drawn under the units between them, and from there it cannot be.
//  - Shape budget. ~17 shapes per beam out of a pool of 10000 that progress
//    bars, rally lines and order queues also draw from, and it scales with beam
//    thickness.
//  - No remap blending. Bitmap's public methods take a flat ColorId, so the
//    additive glow has to be reimplemented rather than inherited.
//
// The GRP path keeps all three because it stays inside the engine's rendering
// model. See docs/beam-weapons.md for where this goes next.
#define BEAM_USE_GRP_PATH 1

// Probe for the custom render function path (docs/beam-weapons.md).
//
// Replaces the beam overlay's CImage::renderFunction with a naked thunk that
// records every register that could be carrying an argument plus the top of the
// stack, then chains to the engine's own function so the image still draws
// normally. The point is to establish the calling convention by observation
// rather than by guessing it - a wrong guess crashes rather than misbehaves.
//
// Correlate the reported values against the "rfn set" line, which prints the
// overlay's coloringData and grpOffset at spawn: whichever slot carries those
// identifies the remap table argument and (via getCurrentFrame, which returns a
// pointer just inside the GRP) the frame argument.
// Settled - the contract it established is recorded in docs/beam-weapons.md and
// is what BEAM_USE_CUSTOM_RENDER's function is written against. Left in place
// because it is the tool to reach for if the engine is ever seen calling that
// pointer differently; set it to 1 (and BEAM_USE_CUSTOM_RENDER to 0) to run it
// again.
#define BEAM_DEBUG_RENDERFN_PROBE 0

// Draw the beam ourselves instead of letting the engine blit our generated GRP.
//
// The probe established the per-instance render function's contract by
// observation (see docs/beam-weapons.md):
//
//   void __fastcall render(int screenX, int screenY, GrpFrame *frame,
//                          void *rctDraw, void *coloringData);
//
// Pointing that at our own blitter removes the 255px ceiling for good: the beam
// stops travelling through a GRP frame's byte-sized width/height on its way to
// the screen. It is still a real CImage on a real CSprite, so depth, culling and
// the image budget are unchanged.
//
// The GRP is still generated and still owns the image's bounds, which is what
// keeps the engine's culling and refresh bookkeeping honest - and what the
// engine falls back to drawing if our function is ever not attached. Setting
// this to 0 goes back to exactly that.
#define BEAM_USE_CUSTOM_RENDER 1

// Follow-up to the probe, and no longer needed: a capture read ECX/EDX as
// exactly the image's own screenPosition at that instant, so the two register
// arguments are screen x/y and the marker has nothing left to settle. Kept as a
// way to see where the engine thinks an image is. Only meaningful with
// BEAM_DEBUG_RENDERFN_PROBE on, and it does not chain to the engine's function,
// so the beam itself will not draw while it is 1.
#define BEAM_DEBUG_RENDERFN_MARKER 0

struct CUnit;

/// Records a beam shot from @p unit along its current facing, out to its order
/// target. Call from the weapon fire path so the beam appears on the shot
/// rather than for the duration of an attack order.
///
/// Purely cosmetic: reads unit state, never writes any, so it is sync-safe by
/// construction.
void fireBeam(CUnit *unit);

/// Draws every beam still inside its visible window, and retires the rest.
/// Call once per frame from nextFrame(), after graphics::resetAllGraphics() -
/// queued shapes are cleared every frame, so a beam must be re-queued for each
/// frame it should appear.
void drawActiveBeams();

/// GRP path (see BEAM_USE_GRP_PATH): rasterizes the beam and hands it to a
/// fresh top overlay on the unit's sprite.
void spawnBeamOverlay(CUnit *unit);

class Beam
{
  private:
    u32 frames = 9;
    Point16 start;
    Point16 end;
    u16 width = 16;
    vector<s16> framesData;
    u8 *grpData;

    void GenerateFramesData(int nColors = 10);
    void GenerateGrpData();
    void EncodeFrameData(vector<s16> imageData, uint16_t frame, GRPHeader *grpHeader, FrameHeader *frameHeader,
                         FrameData *frameData, bool noCompress);

  public:
	Beam();
    Beam(u16 x1, u16 y1, u16 x2, u16 y2);
	Beam(u16 x, u16 y, int length, double angle);
    ~Beam();

    void Initialize();
	void Cleanup();
	void Update(int length, double angle);
    GrpHead *GetGrpHead();
};
