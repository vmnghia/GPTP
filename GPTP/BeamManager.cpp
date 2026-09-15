#include "BeamManager.h"

BeamManager::BeamManager()
{
    // Constructor can initialize any necessary data structures or variables
}

BeamManager::~BeamManager()
{
    // Destructor can clean up any allocated resources
    for (auto &data : beamData)
    {
        delete[] data; // Free each beam data array
    }
    beamData.clear();
    beamUnits.clear();
}

uint8_t *BeamManager::GenerateGrp(int16_t *imageData, uint16_t frames, uint16_t maxWidth, uint16_t maxHeight,
                                  bool noCompress, uint32_t *grpSize)
{
    GRPHeader header;
    FrameHeader *frameHeaders;
    FrameData *frameData;
    uint8_t *lpGrpData;
    int i, j, x, y, x1, x2, y1, y2;
    unsigned long lastOffset;

    if (!imageData || !grpSize)
    {
        return (uint8_t *)(-1);
    }

    header.frames = frames;
    header.maxWidth = maxWidth;
    header.maxHeight = maxHeight;

    frameHeaders = new FrameHeader[frames];
    frameData = new FrameData[frames];
    lastOffset = sizeof(GRPHeader) + sizeof(FrameHeader) * frames;

    for (i = 0; i < frames; i++)
    {
        frameHeaders[i].offset = lastOffset;

        // Scan frame to find dimensions of used part
        x1 = y1 = 0x10000;
        x2 = y2 = -1;
        for (y = 0; y < maxHeight; y++)
        {
            for (x = 0; x < maxWidth; x++)
            {
                int idx = i * maxWidth * maxHeight + y * maxWidth + x;

                if (imageData[idx] >= 0)
                {
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
        for (j = 0; j < i; j++)
        {
            if (frameData[j].lpRowOffsets && frameHeaders[i].width == frameHeaders[j].width &&
                frameHeaders[i].height == frameHeaders[j].height)
            {
                y1 = i * maxWidth * maxHeight + frameHeaders[i].top * maxWidth + frameHeaders[i].left;
                y2 = j * maxWidth * maxHeight + frameHeaders[j].top * maxWidth + frameHeaders[j].left;

                for (y = 0; y < frameHeaders[i].height; y++)
                {
                    if (memcmp(&imageData[y1], &imageData[y2], frameHeaders[i].width * sizeof(short)) != 0)
                        break;

                    y1 += maxWidth;
                    y2 += maxWidth;
                }

                if (y == frameHeaders[i].height)
                {
                    break;
                }
            }
        }

        if (j < i)
        {
            // Duplicate frame found, set offset and flag as duplicate
            frameHeaders[i].offset = frameHeaders[j].offset;
            frameData[i].lpRowOffsets = 0;
            frameData[i].lpRowSizes = 0;
            frameData[i].lpRowData = 0;
            frameData[i].size = 0;
            continue;
        }

        EncodeFrameData(imageData, i, &header, &frameHeaders[i], &frameData[i], noCompress);
        lastOffset = frameHeaders[i].offset + frameData[i].size;
    }

    // lpGrpData = (uint8_t *)malloc(lastOffset);
    lpGrpData = new uint8_t[lastOffset];

    FrameHeader *frameHeadersFinal = new FrameHeader[frames];

    for (i = 0; i < frames; i++)
    {
        frameHeadersFinal[i].left = frameHeaders[i].left;
        frameHeadersFinal[i].top = frameHeaders[i].top;
        frameHeadersFinal[i].width = frameHeaders[i].width;
        frameHeadersFinal[i].height = frameHeaders[i].height;
        frameHeadersFinal[i].offset = (uint32_t)(lpGrpData + frameHeaders[i].offset);
    }

    // Write completed GRP to buffer
    memcpy(lpGrpData, &header, sizeof(GRPHeader));
    memcpy(lpGrpData + sizeof(GRPHeader), frameHeadersFinal, frames * sizeof(FrameHeader));

    for (i = 0; i < frames; i++)
    {
        if (frameData[i].lpRowOffsets)
        {
            if (!noCompress)
                memcpy(lpGrpData + frameHeaders[i].offset, frameData[i].lpRowOffsets,
                       frameHeaders[i].height * sizeof uint16_t);

            for (y = 0; y < frameHeaders[i].height; y++)
            {
                if (frameData[i].lpRowData[y])
                {
                    memcpy(lpGrpData + frameHeaders[i].offset + frameData[i].lpRowOffsets[y], frameData[i].lpRowData[y],
                           frameData[i].lpRowSizes[y]);
                    free(frameData[i].lpRowData[y]);
                }
            }

            free(frameData[i].lpRowOffsets);
            free(frameData[i].lpRowSizes);
            free(frameData[i].lpRowData);
        }
    }

    free(frameHeaders);
    free(frameData);

    *grpSize = lastOffset;
    return lpGrpData;
}

void BeamManager::EncodeFrameData(int16_t *imageData, uint16_t frame, GRPHeader *grpHeader, FrameHeader *frameHeader,
                     FrameData *frameData, bool noCompress) {
	int x, y, i, j, nBufPos, nRepeat;
	uint8_t *lpRowBuf;
	uint16_t nLastOffset = 0;

	frameData->lpRowOffsets = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
	frameData->lpRowSizes = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
	frameData->lpRowData = (uint8_t **)malloc(frameHeader->height * sizeof(uint8_t *));
	lpRowBuf = (uint8_t *)malloc(frameHeader->width * 2);

	if (!noCompress)
		nLastOffset = frameHeader->height * sizeof(uint16_t);

	for (y = 0; y < frameHeader->height; y++) {
		i = frame * grpHeader->maxWidth * grpHeader->maxHeight + (frameHeader->top + y) * grpHeader->maxWidth;

		if (!noCompress) {
			// Search for duplicate rows
			for (x = 0; x < y; x++) {
				j = frame * grpHeader->maxWidth * grpHeader->maxHeight + (frameHeader->top + x) * grpHeader->maxWidth;
				if (memcmp(&imageData[i + frameHeader->left], &imageData[j + frameHeader->left],
				           grpHeader->maxWidth * sizeof(short)) == 0)
					break;
			}

			if (x < y) {
				frameData->lpRowOffsets[y] = frameData->lpRowOffsets[x];
				frameData->lpRowSizes[y] = 0;
				frameData->lpRowData[y] = 0;

				continue;
			}
		}

		nBufPos = 0;
		if (frameHeader->width > 0) {
			for (x = frameHeader->left; x < frameHeader->left + frameHeader->width; x++) {
				if (!noCompress) {
					if (x < frameHeader->left + frameHeader->width - 1) {
						if (imageData[i + x] < 0) {
							lpRowBuf[nBufPos] = 0x80;
							for (; imageData[i + x] < 0 && x < frameHeader->left + frameHeader->width &&
							       lpRowBuf[nBufPos] < 0xFF;
							     x++) {
								lpRowBuf[nBufPos]++;
							}
							x--;
							if (nLastOffset + nBufPos + 1 <= 0xFFFF)
								nBufPos++;
							continue;
						}

						// Count repeating pixels, nRepeat = number of pixels - 1, ignore if there are less than 4
						// duplicates
						for (nRepeat = 0; imageData[i + x + nRepeat] == imageData[i + x + nRepeat + 1] &&
						                  x + nRepeat < frameHeader->left + frameHeader->width - 1 && nRepeat < 0x3E;
						     nRepeat++) {
						}

						if (nRepeat > 2) {
							lpRowBuf[nBufPos] = 0x41 + nRepeat;
							lpRowBuf[nBufPos + 1] = (uint8_t)(imageData[i + x]);
							x += nRepeat;
							if (nLastOffset + nBufPos + 2 <= 0xFFFF)
								nBufPos += 2;
						} else {
							lpRowBuf[nBufPos] = 0;
							for (; imageData[i + x] >= 0 && x < frameHeader->left + frameHeader->width &&
							       lpRowBuf[nBufPos] < 0x3F;
							     x++) {
								// Count repeating pixels, ignore if there are less than 4 duplicates
								for (nRepeat = 0;
								     imageData[i + x + nRepeat] == imageData[i + x + nRepeat + 1] &&
								     x + nRepeat < frameHeader->left + frameHeader->width - 1 && nRepeat < 3;
								     nRepeat++) {
								}
								if (nRepeat > 2)
									break;

								lpRowBuf[nBufPos]++;
								lpRowBuf[nBufPos + lpRowBuf[nBufPos]] = (uint8_t)(imageData[i + x]);
							}
							if (imageData[i + x] >= 0 && x == frameHeader->left + frameHeader->width - 1 &&
							    lpRowBuf[nBufPos] < 0x3F) {
								lpRowBuf[nBufPos]++;
								lpRowBuf[nBufPos + lpRowBuf[nBufPos]] = (uint8_t)(imageData[i + x]);
							}
							x--;
							if (nLastOffset + nBufPos + 1 + lpRowBuf[nBufPos] <= 0xFFFF)
								nBufPos += 1 + lpRowBuf[nBufPos];
						}
					} else {
						if (imageData[i + x] < 0) {
							lpRowBuf[nBufPos] = 0x81;
							if (nLastOffset + nBufPos + 1 <= 0xFFFF)
								nBufPos++;
						} else {
							lpRowBuf[nBufPos] = 1;
							lpRowBuf[nBufPos + 1] = (uint8_t)(imageData[i + x]);
							if (nLastOffset + nBufPos + 2 <= 0xFFFF)
								nBufPos += 2;
						}
					}
				} else {
					lpRowBuf[nBufPos] = (uint8_t)(imageData[i + x]);
					if (nLastOffset + nBufPos + 1 <= 0xFFFF)
						nBufPos++;
				}
			}
		}

		frameData->lpRowOffsets[y] = nLastOffset;
		nLastOffset = frameData->lpRowOffsets[y] + nBufPos;

		frameData->lpRowSizes[y] = nBufPos;
		frameData->lpRowData[y] = (uint8_t *)malloc(nBufPos);
		memcpy(frameData->lpRowData[y], lpRowBuf, nBufPos);
	}

	frameData->size = nLastOffset;

	free(lpRowBuf);
}