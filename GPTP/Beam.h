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

struct CUnit;

/// Rasterizes a beam along @p unit's current facing, out to its order target,
/// and hands it to a fresh top overlay on the unit's sprite.
///
/// Purely cosmetic: this reads unit state but never writes any, so it stays
/// sync-safe by construction. Call it from the weapon fire path so the beam
/// appears on the shot itself rather than for the duration of an attack order.
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
