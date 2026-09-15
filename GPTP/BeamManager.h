#pragma once
#include "SCBW/structures/CUnit.h"
#include <vector>

using std::vector;

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

class BeamManager
{
    vector<CUnit *> beamUnits;  // List of units that have beams
    vector<int16_t *> beamData; // List of beam data for each unit

  public:
    BeamManager();
    ~BeamManager();
    uint8_t *GenerateGrp(int16_t *imageData, uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, bool noCompress,
                         uint32_t *grpSize);

  private:
    void EncodeFrameData(int16_t *imageData, uint16_t frame, GRPHeader *grpHeader, FrameHeader *frameHeader,
                         FrameData *frameData, bool noCompress);
};
