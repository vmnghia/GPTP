#include "Grp.h"

void Grp::Generate(uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, CUnit *unit, int16_t *imageData) {
	Header header;
	unit = unit;

	//Header header;
	FrameHeader *frameHeaders = new FrameHeader[frames];
	FrameData *frameData = new FrameData[frames];
	//uint8_t *lpGrpData;
	int i, j, x, y, x1, x2, y1, y2;
	unsigned long lastOffset = sizeof(Header) + sizeof(FrameHeader) * frames;

	header.frames = frames;
	header.maxWidth = maxWidth;
	header.maxHeight = maxHeight;

	for (i = 0; i < frames; i++) {
		frameHeaders[i].offset = lastOffset;

		// Scan frame to find dimensions of used part
		x1 = y1 = 0x10000;
		x2 = y2 = -1;
		for (y = 0; y < maxHeight; y++) {
			for (x = 0; x < maxWidth; x++) {
				int idx = i * maxWidth * maxHeight + y * maxWidth + x;

				if (imageData[idx] >= 0) {
					if (x < x1)
						x1 = x;
					if (x > x2)
						x2 = x;
					if (y < y1)
						y1 = y;
					if (y > y2)
						y2 = y;
				}
			}
		}
		x2 = x2 - x1 + 1;
		y2 = y2 - y1 + 1;
		if ((uint16_t)x1 > 255)
			x1 = 255;
		if ((uint16_t)y1 > 255)
			y1 = 255;
		if ((uint16_t)x2 > 255)
			x2 = 255;
		if ((uint16_t)y2 > 255)
			y2 = 255;
		frameHeaders[i].left = x1;
		frameHeaders[i].top = y1;
		frameHeaders[i].width = x2;
		frameHeaders[i].height = y2;

		// Search for duplicate frames
		for (j = 0; j < i; j++) {
			if (frameData[j].rowOffsets && frameHeaders[i].width == frameHeaders[j].width &&
			    frameHeaders[i].height == frameHeaders[j].height) {
				y1 = i * maxWidth * maxHeight + frameHeaders[i].top * maxWidth + frameHeaders[i].left;
				y2 = j * maxWidth * maxHeight + frameHeaders[j].top * maxWidth + frameHeaders[j].left;

				for (y = 0; y < frameHeaders[i].height; y++) {
					if (memcmp(&imageData[y1], &imageData[y2], frameHeaders[i].width * sizeof(short)) != 0)
						break;

					y1 += maxWidth;
					y2 += maxWidth;
				}

				if (y == frameHeaders[i].height) {
					break;
				}
			}
		}

		if (j < i) {
			// Duplicate frame found, set offset and flag as duplicate
			frameHeaders[i].offset = frameHeaders[j].offset;
			frameData[i].rowOffsets = 0;
			frameData[i].rowSizes = 0;
			frameData[i].rowData = 0;
			frameData[i].size = 0;
			continue;
		}

		EncodeFrameData(imageData, i, &header, &frameHeaders[i], &frameData[i]);
		lastOffset = frameHeaders[i].offset + frameData[i].size;
	}

	rawData = new uint8_t[lastOffset];

	FrameHeader *frameHeadersFinal = new FrameHeader[frames];

	for (i = 0; i < frames; i++) {
		frameHeadersFinal[i].left = frameHeaders[i].left;
		frameHeadersFinal[i].top = frameHeaders[i].top;
		frameHeadersFinal[i].width = frameHeaders[i].width;
		frameHeadersFinal[i].height = frameHeaders[i].height;
		frameHeadersFinal[i].offset = (uint32_t)(rawData + frameHeaders[i].offset);
	}

	// Write completed GRP to buffer
	memcpy(rawData, &header, sizeof(Header));
	memcpy(rawData + sizeof(Header), frameHeadersFinal, frames * sizeof(FrameHeader));

	for (i = 0; i < frames; i++) {
		if (frameData[i].rowOffsets) {
			for (y = 0; y < frameHeaders[i].height; y++) {
				if (frameData[i].rowData[y]) {
					memcpy(rawData + frameHeaders[i].offset + frameData[i].rowOffsets[y], frameData[i].rowData[y],
					       frameData[i].rowSizes[y]);
					free(frameData[i].rowData[y]);
				}
			}

			free(frameData[i].rowOffsets);
			free(frameData[i].rowSizes);
			free(frameData[i].rowData);
		}
	}

	delete[] frameHeaders;
	delete[] frameData;
}

Grp::Grp(CUnit *unit) {
	this->unit = unit;
}

Grp::~Grp() {
	if (rawData) {
		delete[] rawData;
		rawData = nullptr;
	}
}

GrpHead *Grp::getGrpHead() {
	return reinterpret_cast<GrpHead *>(rawData);
}

void Grp::EncodeFrameData(int16_t *imageData, uint16_t frame, Header *header, FrameHeader *frameHeader,
                     FrameData *frameData) {
	int x, y, i, nBufPos;
	uint8_t *rowBuffer = new uint8_t[frameHeader->width * 2];
	uint16_t nLastOffset = 0;

	frameData->rowOffsets = new uint16_t[frameHeader->height];
	frameData->rowSizes = new uint16_t[frameHeader->height];
	frameData->rowData = new uint8_t *[frameHeader->height];

	for (y = 0; y < frameHeader->height; y++) {
		i = frame * header->maxWidth * header->maxHeight + (frameHeader->top + y) * header->maxWidth;

		nBufPos = 0;
		if (frameHeader->width > 0) {
			for (x = frameHeader->left; x < frameHeader->left + frameHeader->width; x++) {
				rowBuffer[nBufPos] = (uint8_t)(imageData[i + x]);
				if (nLastOffset + nBufPos + 1 <= 0xFFFF)
					nBufPos++;
			}
		}

		frameData->rowOffsets[y] = nLastOffset;
		nLastOffset = frameData->rowOffsets[y] + nBufPos;

		frameData->rowSizes[y] = nBufPos;
		frameData->rowData[y] = new uint8_t[nBufPos];
		memcpy(frameData->rowData[y], rowBuffer, nBufPos);
	}

	frameData->size = nLastOffset;

	delete[] rowBuffer;
}