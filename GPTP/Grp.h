#pragma once
#include <algorithm>
#include <cstdint>
#include "SCBW/structures.h"
#include "SCBW/structures/CUnit.h"

class Grp {

  public:
	struct Header {
		uint16_t frames;
		uint16_t maxWidth;
		uint16_t maxHeight;
	};

	struct FrameHeader {
		uint8_t left;
		uint8_t top;
		uint8_t width;
		uint8_t height;
		uint32_t offset;
	};

	struct FrameData {
		uint16_t *rowOffsets;
		uint16_t *rowSizes;
		uint8_t **rowData;
		uint32_t size;
	};

	CUnit *unit; // Unit associated with the GRP, if any

	void Generate(uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, CUnit *unit, int16_t *imageData = nullptr);
	Grp(CUnit *unit);
	~Grp();

	GrpHead *getGrpHead();
	
  private:
	uint8_t *rawData; // Pointer to the raw data of the GRP

	void EncodeFrameData(int16_t *imageData, uint16_t frame, Header *header, FrameHeader *frameHeader,
	                     FrameData *frameData);
};
