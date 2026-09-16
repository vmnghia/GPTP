#include "Beam.h"

#include <SCBW/api.h>
#include <SCBW/enumerations.h>
#include <SCBW/structures/CImage.h>
#include <SCBW/structures/CSprite.h>
#include <SCBW/structures/CUnit.h>
#include <graphics/graphics.h>

#include <cstdio>
#include <unordered_map>

using std::max;
using std::min;
using std::vector;

// Returns the four corners of the beam quad. Point32 (signed) rather than
// Point16 (u16): a beam pointing left or up from the canvas origin, or a
// thickness offset that pushes a corner past an edge, produces negative
// coordinates. Wrapping those through u16 turned them into ~65535, which fed
// garbage edges to the scanline fill below and painted a full-canvas rectangle
// instead of a beam.
vector<Point32> getThickLineRect(int x1, int y1, int x2, int y2, int thickness)
{
    vector<Point32> rect;

    // Compute the directional vector.
    float dx = static_cast<float>(x2 - x1);
    float dy = static_cast<float>(y2 - y1);
    float len = sqrt(dx * dx + dy * dy);

    // If the line is degenerate, return an empty polygon.
    if (len == 0)
        return rect;

    // Compute the unit normal vector (perpendicular to the line).
    // For a line vector (dx, dy), a perpendicular is (-dy, dx).
    float nx = -dy / len;
    float ny = dx / len;

    // Half the thickness (as float value for accurate offset computation).
    float half = thickness / 2.0f;

    // Compute the four vertices.
    // Offset one endpoint by the unit normal times half thickness for one side,
    // and by the negative of that for the other side.
    Point32 p1 = {static_cast<int>(round(x1 + nx * half)), static_cast<int>(round(y1 + ny * half))};
    Point32 p2 = {static_cast<int>(round(x1 - nx * half)), static_cast<int>(round(y1 - ny * half))};
    Point32 p3 = {static_cast<int>(round(x2 - nx * half)), static_cast<int>(round(y2 - ny * half))};
    Point32 p4 = {static_cast<int>(round(x2 + nx * half)), static_cast<int>(round(y2 + ny * half))};

    // Return the vertices in order.
    // The order here is p1, p2, p3, p4 which (for many cases) forms a clockwise polygon.
    rect.push_back(p1);
    rect.push_back(p2);
    rect.push_back(p3);
    rect.push_back(p4);

    return rect;
}

int16_t *generateBeam(int x1, int y1, int x2, int y2, int thickness, int16_t *buffer, int nColors = 10)
{
    if (nColors > 10)
    {
        nColors = 10;
    }
    if (nColors == 0)
    {
        return buffer;
    }
    int width = 255;
    int height = 255;
    vector<GradientStop> stops = {{0.0f, nColors - 1}, {0.5f, 0}, {1.0f, nColors - 1}};
    vector<Point32> rect = getThickLineRect(x1, y1, x2, y2, thickness);
    double lineAngle = atan2(y2 - y1, x2 - x1);

    int rowSize = ((width + 3) / 4) * 4; // row size in bytes
    std::vector<int> colors = {47, 45, 27, 27, 17, 11, 10, 10, 5, 5};
    if (nColors < 10)
    {
        std::vector<int>(colors.begin() + 10 - nColors, colors.end()).swap(colors);
    }
    double gradX = cos(lineAngle + M_PI_2);
    double gradY = sin(lineAngle + M_PI_2);

    float minProj = FLT_MAX, maxProj = -FLT_MAX;
    for (const auto &p : rect)
    {
        float proj = p.x * gradX + p.y * gradY;
        minProj = min(minProj, proj);
        maxProj = max(maxProj, proj);
    }

    // Determine the vertical extent (bounding box) of the polygon, then clamp
    // it to the canvas. Signed throughout: a quad that starts above the canvas
    // has negative y, and clamping that as unsigned inverts the range.
    int ymin = height, ymax = -1;
    for (const auto &p : rect)
    {
        ymin = min(ymin, p.y);
        ymax = max(ymax, p.y);
    }
    ymin = max(ymin, 0);
    ymax = min(ymax, height - 1);

    // Process each scanline within the polygon's vertical bounds.
    for (int y = ymin; y <= ymax; y++)
    {
        vector<int> intersections;

        // For each edge of the polygon, compute the x-coordinate where the edge intersects the scanline.
        for (size_t i = 0; i < rect.size(); i++)
        {
            size_t j = (i + 1) % rect.size();
            int x1 = rect[i].x, y1 = rect[i].y;
            int x2 = rect[j].x, y2 = rect[j].y;

            // Check if the scanline at y intersects the edge.
            if ((y1 <= y && y < y2) || (y2 <= y && y < y1))
            {
                float xIntersect = x1 + (float)(y - y1) * (x2 - x1) / (float)(y2 - y1);
                // Round the intersection point.
                intersections.push_back(static_cast<int>(round(xIntersect)));
            }
        }

        if (intersections.empty())
            continue;

        // Sort the intersection x-coordinates.
        sort(intersections.begin(), intersections.end());

        // Fill between pairs of intersections using the gradient color.
        for (size_t i = 0; i + 1 < intersections.size(); i += 2)
        {
            int xStart = max(intersections[i], 0);
            int xEnd = min(intersections[i + 1], width - 1);
            for (int x = xStart; x <= xEnd; x++)
            {
                // Compute projection of (x, y) on the gradient axis.
                float proj = x * gradX + y * gradY;
                // Normalize t between 0 and 1.
                float t = (maxProj - minProj != 0) ? (proj - minProj) / (maxProj - minProj) : 0;
                t = std::max(0.0f, std::min(t, 1.0f));

                int pixelColor = 0;
                if (t <= stops.front().position)
                {
                    pixelColor = stops.front().color;
                }
                else if (t >= stops.back().position)
                {
                    pixelColor = stops.back().color;
                }
                else
                {
                    // Iterate through stops to find the proper interval.
                    for (size_t s = 0; s < stops.size() - 1; s++)
                    {
                        if (t >= stops[s].position && t <= stops[s + 1].position)
                        {
                            float localT = (t - stops[s].position) / (stops[s + 1].position - stops[s].position);
                            // Linear interpolation between the two stop colors.
                            pixelColor =
                                static_cast<int>(round(stops[s].color * (1 - localT) + stops[s + 1].color * localT));
                            break;
                        }
                    }
                }

                // unsigned char *dest = pixelData + (height - 1 - y) * rowSize; // Assuming 4 bytes per pixel (RGBA)
                buffer[y * 255 + x] = colors[pixelColor];
            }
        }
    }

    return buffer;
}

int16_t *generateBeamAngle(int x, int y, float angle, int length, int thickness, int16_t *buffer, int nColors)
{
    int x2 = x + static_cast<int>(length * sin(angle));
    int y2 = y - static_cast<int>(length * cos(angle));

    return generateBeam(x, y, x2, y2, thickness, buffer, nColors);
}

void encodeFrameData(int16_t *imageData, uint16_t frame, GrpHeader *grpHeader, GrpFrame *frameHeader,
                     FrameData *frameData, bool noCompress)
{
    int x, y, i, j, nBufPos, nRepeat;
    uint8_t *lpRowBuf;
    uint16_t nLastOffset = 0;

    frameData->lpRowOffsets = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
    frameData->lpRowSizes = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
    frameData->lpRowData = (LPBYTE *)malloc(frameHeader->height * sizeof(LPBYTE));
    lpRowBuf = (uint8_t *)malloc(frameHeader->width * 2);

    if (!noCompress)
        nLastOffset = frameHeader->height * sizeof(uint16_t);

    for (y = 0; y < frameHeader->height; y++)
    {
        i = frame * grpHeader->width * grpHeader->height + (frameHeader->y + y) * grpHeader->width;

        if (!noCompress)
        {
            // Search for duplicate rows
            for (x = 0; x < y; x++)
            {
                j = frame * grpHeader->width * grpHeader->height + (frameHeader->y + x) * grpHeader->width;
                if (memcmp(&imageData[i + frameHeader->x], &imageData[j + frameHeader->x],
                           grpHeader->width * sizeof(short)) == 0)
                    break;
            }

            if (x < y)
            {
                frameData->lpRowOffsets[y] = frameData->lpRowOffsets[x];
                frameData->lpRowSizes[y] = 0;
                frameData->lpRowData[y] = 0;

                continue;
            }
        }

        nBufPos = 0;
        if (frameHeader->width > 0)
        {
            for (x = frameHeader->x; x < frameHeader->x + frameHeader->width; x++)
            {
                if (!noCompress)
                {
                    if (x < frameHeader->x + frameHeader->width - 1)
                    {
                        if (imageData[i + x] < 0)
                        {
                            lpRowBuf[nBufPos] = 0x80;
                            for (; imageData[i + x] < 0 && x < frameHeader->x + frameHeader->width &&
                                   lpRowBuf[nBufPos] < 0xFF;
                                 x++)
                            {
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
                                          x + nRepeat < frameHeader->x + frameHeader->width - 1 && nRepeat < 0x3E;
                             nRepeat++)
                        {
                        }

                        if (nRepeat > 2)
                        {
                            lpRowBuf[nBufPos] = 0x41 + nRepeat;
                            lpRowBuf[nBufPos + 1] = (uint8_t)(imageData[i + x]);
                            x += nRepeat;
                            if (nLastOffset + nBufPos + 2 <= 0xFFFF)
                                nBufPos += 2;
                        }
                        else
                        {
                            lpRowBuf[nBufPos] = 0;
                            for (; imageData[i + x] >= 0 && x < frameHeader->x + frameHeader->width &&
                                   lpRowBuf[nBufPos] < 0x3F;
                                 x++)
                            {
                                // Count repeating pixels, ignore if there are less than 4 duplicates
                                for (nRepeat = 0; imageData[i + x + nRepeat] == imageData[i + x + nRepeat + 1] &&
                                                  x + nRepeat < frameHeader->x + frameHeader->width - 1 && nRepeat < 3;
                                     nRepeat++)
                                {
                                }
                                if (nRepeat > 2)
                                    break;

                                lpRowBuf[nBufPos]++;
                                lpRowBuf[nBufPos + lpRowBuf[nBufPos]] = (uint8_t)(imageData[i + x]);
                            }
                            if (imageData[i + x] >= 0 && x == frameHeader->x + frameHeader->width - 1 &&
                                lpRowBuf[nBufPos] < 0x3F)
                            {
                                lpRowBuf[nBufPos]++;
                                lpRowBuf[nBufPos + lpRowBuf[nBufPos]] = (uint8_t)(imageData[i + x]);
                            }
                            x--;
                            if (nLastOffset + nBufPos + 1 + lpRowBuf[nBufPos] <= 0xFFFF)
                                nBufPos += 1 + lpRowBuf[nBufPos];
                        }
                    }
                    else
                    {
                        if (imageData[i + x] < 0)
                        {
                            lpRowBuf[nBufPos] = 0x81;
                            if (nLastOffset + nBufPos + 1 <= 0xFFFF)
                                nBufPos++;
                        }
                        else
                        {
                            lpRowBuf[nBufPos] = 1;
                            lpRowBuf[nBufPos + 1] = (uint8_t)(imageData[i + x]);
                            if (nLastOffset + nBufPos + 2 <= 0xFFFF)
                                nBufPos += 2;
                        }
                    }
                }
                else
                {
                    lpRowBuf[nBufPos] = (uint8_t)(imageData[i + x]);
                    if (nLastOffset + nBufPos + 1 <= 0xFFFF)
                        nBufPos++;
                }
            }
        }

        frameData->lpRowOffsets[y] = nLastOffset;
        nLastOffset = frameData->lpRowOffsets[y] + nBufPos;

        frameData->lpRowSizes[y] = nBufPos;
        frameData->lpRowData[y] = (LPBYTE)malloc(nBufPos);
        memcpy(frameData->lpRowData[y], lpRowBuf, nBufPos);
    }

    frameData->size = nLastOffset;

    free(lpRowBuf);
}

GrpHead *createGRP(int16_t *imageData, uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, bool noCompress,
                   uint32_t *grpSize)
{
    if (!imageData || !grpSize)
        return nullptr;

    GrpHeader grpHead;
    GrpFrame *grpFrames = new GrpFrame[frames];
    FrameData *frameData = new FrameData[frames];
    uint8_t *lpGrpData;
    int i, j, x, y, x1, x2, y1, y2;
    unsigned long lastOffset;

    grpHead.frameCount = frames;
    grpHead.width = maxWidth;
    grpHead.height = maxHeight;

    lastOffset = sizeof(GrpHeader) + sizeof(GrpFrame) * frames;

    for (i = 0; i < frames; i++)
    {
        grpFrames[i].dataOffset = lastOffset;

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
        grpFrames[i].x = x1;
        grpFrames[i].y = y1;
        grpFrames[i].width = x2;
        grpFrames[i].height = y2;

        // Search for duplicate frames
        for (j = 0; j < i; j++)
        {
            if (frameData[j].lpRowOffsets && grpFrames[i].width == grpFrames[j].width &&
                grpFrames[i].height == grpFrames[j].height)
            {
                y1 = i * maxWidth * maxHeight + grpFrames[i].y * maxWidth + grpFrames[i].x;
                y2 = j * maxWidth * maxHeight + grpFrames[j].y * maxWidth + grpFrames[j].x;

                for (y = 0; y < grpFrames[i].height; y++)
                {
                    if (memcmp(&imageData[y1], &imageData[y2], grpFrames[i].width * sizeof(short)) != 0)
                        break;

                    y1 += maxWidth;
                    y2 += maxWidth;
                }

                if (y == grpFrames[i].height)
                {
                    break;
                }
            }
        }

        if (j < i)
        {
            // Duplicate frame found, set offset and flag as duplicate
            grpFrames[i].dataOffset = grpFrames[j].dataOffset;
            frameData[i].lpRowOffsets = 0;
            frameData[i].lpRowSizes = 0;
            frameData[i].lpRowData = 0;
            frameData[i].size = 0;
            continue;
        }

        encodeFrameData(imageData, i, &grpHead, &grpFrames[i], &frameData[i], noCompress);
        lastOffset = grpFrames[i].dataOffset + frameData[i].size;
    }

    lpGrpData = new uint8_t[lastOffset];

    // Write completed GRP to buffer
    memcpy(lpGrpData, &grpHead, sizeof(GrpHeader));
    memcpy(lpGrpData + sizeof(GrpHeader), grpFrames, frames * sizeof(GrpFrame));

    for (i = 0; i < frames; i++)
    {
        if (frameData[i].lpRowOffsets)
        {
            if (!noCompress)
                memcpy(lpGrpData + grpFrames[i].dataOffset, frameData[i].lpRowOffsets,
                       grpFrames[i].height * sizeof uint16_t);

            for (y = 0; y < grpFrames[i].height; y++)
            {
                if (frameData[i].lpRowData[y])
                {
                    memcpy(lpGrpData + grpFrames[i].dataOffset + frameData[i].lpRowOffsets[y],
                           frameData[i].lpRowData[y], frameData[i].lpRowSizes[y]);
                    free(frameData[i].lpRowData[y]);
                }
            }

            free(frameData[i].lpRowOffsets);
            free(frameData[i].lpRowSizes);
            free(frameData[i].lpRowData);
        }
    }

    delete[] grpFrames;
    delete[] frameData;

    *grpSize = lastOffset;

    std::ofstream outFile("D:\\SC Modding\\bitmap-io\\x64\\Debug\\beammm.grp", std::ios::binary);
    outFile.write(reinterpret_cast<const char *>(lpGrpData), *grpSize);
    outFile.close();

    return reinterpret_cast<GrpHead *>(lpGrpData);
}

void EncodeFrameData(int16_t *imageData, uint16_t frame, GRPHeader *grpHeader, FrameHeader *frameHeader,
                     FrameData *frameData, bool noCompress)
{
    int x, y, i, j, nBufPos, nRepeat;
    uint8_t *lpRowBuf;
    uint16_t nLastOffset = 0;

    frameData->lpRowOffsets = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
    frameData->lpRowSizes = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
    frameData->lpRowData = (LPBYTE *)malloc(frameHeader->height * sizeof(LPBYTE));
    lpRowBuf = (uint8_t *)malloc(frameHeader->width * 2);

    if (!noCompress)
        nLastOffset = frameHeader->height * sizeof(uint16_t);

    for (y = 0; y < frameHeader->height; y++)
    {
        i = frame * grpHeader->maxWidth * grpHeader->maxHeight + (frameHeader->top + y) * grpHeader->maxWidth;

        if (!noCompress)
        {
            // Search for duplicate rows
            for (x = 0; x < y; x++)
            {
                j = frame * grpHeader->maxWidth * grpHeader->maxHeight + (frameHeader->top + x) * grpHeader->maxWidth;
                if (memcmp(&imageData[i + frameHeader->left], &imageData[j + frameHeader->left],
                           grpHeader->maxWidth * sizeof(short)) == 0)
                    break;
            }

            if (x < y)
            {
                frameData->lpRowOffsets[y] = frameData->lpRowOffsets[x];
                frameData->lpRowSizes[y] = 0;
                frameData->lpRowData[y] = 0;

                continue;
            }
        }

        nBufPos = 0;
        if (frameHeader->width > 0)
        {
            for (x = frameHeader->left; x < frameHeader->left + frameHeader->width; x++)
            {
                if (!noCompress)
                {
                    if (x < frameHeader->left + frameHeader->width - 1)
                    {
                        if (imageData[i + x] < 0)
                        {
                            lpRowBuf[nBufPos] = 0x80;
                            for (; imageData[i + x] < 0 && x < frameHeader->left + frameHeader->width &&
                                   lpRowBuf[nBufPos] < 0xFF;
                                 x++)
                            {
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
                             nRepeat++)
                        {
                        }

                        if (nRepeat > 2)
                        {
                            lpRowBuf[nBufPos] = 0x41 + nRepeat;
                            lpRowBuf[nBufPos + 1] = (uint8_t)(imageData[i + x]);
                            x += nRepeat;
                            if (nLastOffset + nBufPos + 2 <= 0xFFFF)
                                nBufPos += 2;
                        }
                        else
                        {
                            lpRowBuf[nBufPos] = 0;
                            for (; imageData[i + x] >= 0 && x < frameHeader->left + frameHeader->width &&
                                   lpRowBuf[nBufPos] < 0x3F;
                                 x++)
                            {
                                // Count repeating pixels, ignore if there are less than 4 duplicates
                                for (nRepeat = 0;
                                     imageData[i + x + nRepeat] == imageData[i + x + nRepeat + 1] &&
                                     x + nRepeat < frameHeader->left + frameHeader->width - 1 && nRepeat < 3;
                                     nRepeat++)
                                {
                                }
                                if (nRepeat > 2)
                                    break;

                                lpRowBuf[nBufPos]++;
                                lpRowBuf[nBufPos + lpRowBuf[nBufPos]] = (uint8_t)(imageData[i + x]);
                            }
                            if (imageData[i + x] >= 0 && x == frameHeader->left + frameHeader->width - 1 &&
                                lpRowBuf[nBufPos] < 0x3F)
                            {
                                lpRowBuf[nBufPos]++;
                                lpRowBuf[nBufPos + lpRowBuf[nBufPos]] = (uint8_t)(imageData[i + x]);
                            }
                            x--;
                            if (nLastOffset + nBufPos + 1 + lpRowBuf[nBufPos] <= 0xFFFF)
                                nBufPos += 1 + lpRowBuf[nBufPos];
                        }
                    }
                    else
                    {
                        if (imageData[i + x] < 0)
                        {
                            lpRowBuf[nBufPos] = 0x81;
                            if (nLastOffset + nBufPos + 1 <= 0xFFFF)
                                nBufPos++;
                        }
                        else
                        {
                            lpRowBuf[nBufPos] = 1;
                            lpRowBuf[nBufPos + 1] = (uint8_t)(imageData[i + x]);
                            if (nLastOffset + nBufPos + 2 <= 0xFFFF)
                                nBufPos += 2;
                        }
                    }
                }
                else
                {
                    lpRowBuf[nBufPos] = (uint8_t)(imageData[i + x]);
                    if (nLastOffset + nBufPos + 1 <= 0xFFFF)
                        nBufPos++;
                }
            }
        }

        frameData->lpRowOffsets[y] = nLastOffset;
        nLastOffset = frameData->lpRowOffsets[y] + nBufPos;

        frameData->lpRowSizes[y] = nBufPos;
        frameData->lpRowData[y] = (LPBYTE)malloc(nBufPos);
        memcpy(frameData->lpRowData[y], lpRowBuf, nBufPos);
    }

    frameData->size = nLastOffset;

    free(lpRowBuf);
}

uint8_t *generateGrp(int16_t *imageData, uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, bool noCompress,
                     uint32_t *grpSize)
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
        // An all-transparent frame never updates the sentinels (x1 stays at
        // 0x10000, x2 stays at -1). The old clamps cast to uint16_t first,
        // which wraps 0x10000 and -65536 both to 0, so they never fired and the
        // garbage bounds truncated into the u8 frame header fields below.
        // Collapse that case to an honest 0x0 frame, and compare as signed.
        if (x2 < x1 || y2 < y1)
        {
            x1 = y1 = x2 = y2 = 0;
        }
        else
        {
            x2 = x2 - x1 + 1;
            y2 = y2 - y1 + 1;
        }

        if (x1 > 255)
            x1 = 255;
        if (y1 > 255)
            y1 = 255;
        if (x2 > 255)
            x2 = 255;
        if (y2 > 255)
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

    delete[] frameHeaders;
    delete[] frameData;

    *grpSize = lastOffset;
    return lpGrpData;
}

Beam::Beam()
{
    this->start.x = 0;
    this->start.y = 0;
    this->end.x = 0;
    this->end.y = 0;

    this->Initialize();
}

Beam::Beam(u16 x1, u16 y1, u16 x2, u16 y2)
{
    this->start.x = x1;
    this->start.y = y1;
    this->end.x = x2;
    this->end.y = y2;

    this->Initialize();
}

Beam::Beam(u16 x, u16 y, int length, double angle)
{
    this->start.x = x;
    this->start.y = y;
    this->end.x = x + static_cast<u16>(length * sin(angle));
    this->end.y = y - static_cast<u16>(length * cos(angle));

    this->Initialize();
}

Beam::~Beam()
{
    this->Cleanup();

}

void Beam::Initialize()
{
    this->framesData.assign(this->frames * 255 * 255 * 2, -1);
    this->GenerateFramesData();
    this->GenerateGrpData();
}

void Beam::Cleanup()
{

    if (this->grpData)
    {
        delete[] this->grpData;
        this->grpData = nullptr;
    }

    this->framesData.clear();
}

GrpHead *Beam::GetGrpHead()
{
	GrpHead *grpHead = reinterpret_cast<GrpHead *>(this->grpData);
	return grpHead;
}

void Beam::Update(int length, double angle)
{
    this->end.x = this->start.x + static_cast<u16>(length * sin(angle));
    this->end.y = this->start.y - static_cast<u16>(length * cos(angle));
    this->Cleanup();
    this->Initialize();
}

void Beam::GenerateFramesData(int nColors)
{
    int x1 = this->start.x;
    int y1 = this->start.y;
    int x2 = this->end.x;
    int y2 = this->end.y;
    int thickness = this->width;

    if (nColors > 10)
    {
        nColors = 10;
    }
    if (nColors == 0)
    {
        return;
    }
    int width = 255;
    int height = 255;
    vector<GradientStop> stops = {{0.0f, nColors - 1}, {0.5f, 0}, {1.0f, nColors - 1}};
    vector<Point32> rect = getThickLineRect(x1, y1, x2, y2, thickness);
    double lineAngle = atan2(y2 - y1, x2 - x1);

    int rowSize = ((width + 3) / 4) * 4; // row size in bytes
    std::vector<int> colors = {47, 45, 27, 27, 17, 11, 10, 10, 5, 5};
    if (nColors < 10)
    {
        std::vector<int>(colors.begin() + 10 - nColors, colors.end()).swap(colors);
    }
    float gradX = cos(lineAngle + M_PI_2);
    float gradY = sin(lineAngle + M_PI_2);

    float minProj = FLT_MAX, maxProj = -FLT_MAX;
    for (const auto &p : rect)
    {
        float proj = p.x * gradX + p.y * gradY;
        minProj = min(minProj, proj);
        maxProj = max(maxProj, proj);
    }

    // Determine the vertical extent (bounding box) of the polygon, then clamp
    // it to the canvas. Signed throughout: a quad that starts above the canvas
    // has negative y, and clamping that as unsigned inverts the range.
    int ymin = height, ymax = -1;
    for (const auto &p : rect)
    {
        ymin = min(ymin, p.y);
        ymax = max(ymax, p.y);
    }
    ymin = max(ymin, 0);
    ymax = min(ymax, height - 1);

    // Process each scanline within the polygon's vertical bounds.
    for (int y = ymin; y <= ymax; y++)
    {
        vector<int> intersections;

        // For each edge of the polygon, compute the x-coordinate where the edge intersects the scanline.
        for (size_t i = 0; i < rect.size(); i++)
        {
            size_t j = (i + 1) % rect.size();
            int x1 = rect[i].x, y1 = rect[i].y;
            int x2 = rect[j].x, y2 = rect[j].y;

            // Check if the scanline at y intersects the edge.
            if ((y1 <= y && y < y2) || (y2 <= y && y < y1))
            {
                float xIntersect = x1 + (float)(y - y1) * (x2 - x1) / (float)(y2 - y1);
                // Round the intersection point.
                intersections.push_back(static_cast<int>(round(xIntersect)));
            }
        }

        if (intersections.empty())
            continue;

        // Sort the intersection x-coordinates.
        sort(intersections.begin(), intersections.end());

        // Fill between pairs of intersections using the gradient color.
        for (size_t i = 0; i + 1 < intersections.size(); i += 2)
        {
            int xStart = max(intersections[i], 0);
            int xEnd = min(intersections[i + 1], width - 1);
            for (int x = xStart; x <= xEnd; x++)
            {
                // Compute projection of (x, y) on the gradient axis.
                float proj = x * gradX + y * gradY;
                // Normalize t between 0 and 1.
                float t = (maxProj - minProj != 0) ? (proj - minProj) / (maxProj - minProj) : 0;
                t = std::max(0.0f, std::min(t, 1.0f));

                int pixelColor = 0;
                if (t <= stops.front().position)
                {
                    pixelColor = stops.front().color;
                }
                else if (t >= stops.back().position)
                {
                    pixelColor = stops.back().color;
                }
                else
                {
                    // Iterate through stops to find the proper interval.
                    for (size_t s = 0; s < stops.size() - 1; s++)
                    {
                        if (t >= stops[s].position && t <= stops[s + 1].position)
                        {
                            float localT = (t - stops[s].position) / (stops[s + 1].position - stops[s].position);
                            // Linear interpolation between the two stop colors.
                            pixelColor =
                                static_cast<int>(round(stops[s].color * (1 - localT) + stops[s + 1].color * localT));
                            break;
                        }
                    }
                }

                // unsigned char *dest = pixelData + (height - 1 - y) * rowSize; // Assuming 4 bytes per pixel (RGBA)
                this->framesData[y * 255 + x] = colors[pixelColor];
            }
        }
    }

    return;
}

void Beam::GenerateGrpData()
{
    GRPHeader header;
    FrameHeader *frameHeaders;
    FrameData *frameData;
    // uint8_t *lpGrpData;
    int i, j, x, y, x1, x2, y1, y2;
    unsigned long lastOffset;
    bool noCompress = false;

    if (this->framesData.empty())
    {
        return;
    }

    uint16_t maxWidth = 255;
    uint16_t maxHeight = 255;

    header.frames = this->frames;
    header.maxWidth = maxWidth;
    header.maxHeight = maxHeight;

    frameHeaders = new FrameHeader[this->frames];
    frameData = new FrameData[this->frames];
    lastOffset = sizeof(GRPHeader) + sizeof(FrameHeader) * this->frames;

    for (i = 0; i < this->frames; i++)
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

                if (this->framesData[idx] >= 0)
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
                    if (memcmp(&this->framesData[y1], &this->framesData[y2], frameHeaders[i].width * sizeof(short)) !=
                        0)
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

        EncodeFrameData(this->framesData, i, &header, &frameHeaders[i], &frameData[i], true);
        lastOffset = frameHeaders[i].offset + frameData[i].size;
    }

    this->grpData = new uint8_t[lastOffset];

    FrameHeader *frameHeadersFinal = new FrameHeader[this->frames];

    for (i = 0; i < this->frames; i++)
    {
        frameHeadersFinal[i].left = frameHeaders[i].left;
        frameHeadersFinal[i].top = frameHeaders[i].top;
        frameHeadersFinal[i].width = frameHeaders[i].width;
        frameHeadersFinal[i].height = frameHeaders[i].height;
        frameHeadersFinal[i].offset = (uint32_t)(this->grpData + frameHeaders[i].offset);
    }

    // Write completed GRP to buffer
    memcpy(this->grpData, &header, sizeof(GRPHeader));
    memcpy(this->grpData + sizeof(GRPHeader), frameHeadersFinal, frames * sizeof(FrameHeader));

    for (i = 0; i < frames; i++)
    {
        if (frameData[i].lpRowOffsets)
        {
            if (!noCompress)
                memcpy(this->grpData + frameHeaders[i].offset, frameData[i].lpRowOffsets,
                       frameHeaders[i].height * sizeof uint16_t);

            for (y = 0; y < frameHeaders[i].height; y++)
            {
                if (frameData[i].lpRowData[y])
                {
                    memcpy(this->grpData + frameHeaders[i].offset + frameData[i].lpRowOffsets[y],
                           frameData[i].lpRowData[y], frameData[i].lpRowSizes[y]);
                    free(frameData[i].lpRowData[y]);
                }
            }

            free(frameData[i].lpRowOffsets);
            free(frameData[i].lpRowSizes);
            free(frameData[i].lpRowData);
        }
    }

    delete[] frameHeaders;
    delete[] frameData;
    delete[] frameHeadersFinal;

    return;
}

void Beam::EncodeFrameData(vector<s16> imageData, uint16_t frame, GRPHeader *grpHeader, FrameHeader *frameHeader,
                           FrameData *frameData, bool noCompress)
{
    int x, y, i, j, nBufPos, nRepeat;
    uint8_t *lpRowBuf;
    uint16_t nLastOffset = 0;

    frameData->lpRowOffsets = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
    frameData->lpRowSizes = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
    frameData->lpRowData = (LPBYTE *)malloc(frameHeader->height * sizeof(LPBYTE));
    lpRowBuf = (uint8_t *)malloc(frameHeader->width * 2);

    if (!noCompress)
        nLastOffset = frameHeader->height * sizeof(uint16_t);

    for (y = 0; y < frameHeader->height; y++)
    {
        i = frame * grpHeader->maxWidth * grpHeader->maxHeight + (frameHeader->top + y) * grpHeader->maxWidth;

        if (!noCompress)
        {
            // Search for duplicate rows
            for (x = 0; x < y; x++)
            {
                j = frame * grpHeader->maxWidth * grpHeader->maxHeight + (frameHeader->top + x) * grpHeader->maxWidth;
                if (memcmp(&imageData[i + frameHeader->left], &imageData[j + frameHeader->left],
                           grpHeader->maxWidth * sizeof(short)) == 0)
                    break;
            }

            if (x < y)
            {
                frameData->lpRowOffsets[y] = frameData->lpRowOffsets[x];
                frameData->lpRowSizes[y] = 0;
                frameData->lpRowData[y] = 0;

                continue;
            }
        }

        nBufPos = 0;
        if (frameHeader->width > 0)
        {
            for (x = frameHeader->left; x < frameHeader->left + frameHeader->width; x++)
            {
                if (!noCompress)
                {
                    if (x < frameHeader->left + frameHeader->width - 1)
                    {
                        if (imageData[i + x] < 0)
                        {
                            lpRowBuf[nBufPos] = 0x80;
                            for (; imageData[i + x] < 0 && x < frameHeader->left + frameHeader->width &&
                                   lpRowBuf[nBufPos] < 0xFF;
                                 x++)
                            {
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
                             nRepeat++)
                        {
                        }

                        if (nRepeat > 2)
                        {
                            lpRowBuf[nBufPos] = 0x41 + nRepeat;
                            lpRowBuf[nBufPos + 1] = (uint8_t)(imageData[i + x]);
                            x += nRepeat;
                            if (nLastOffset + nBufPos + 2 <= 0xFFFF)
                                nBufPos += 2;
                        }
                        else
                        {
                            lpRowBuf[nBufPos] = 0;
                            for (; imageData[i + x] >= 0 && x < frameHeader->left + frameHeader->width &&
                                   lpRowBuf[nBufPos] < 0x3F;
                                 x++)
                            {
                                // Count repeating pixels, ignore if there are less than 4 duplicates
                                for (nRepeat = 0;
                                     imageData[i + x + nRepeat] == imageData[i + x + nRepeat + 1] &&
                                     x + nRepeat < frameHeader->left + frameHeader->width - 1 && nRepeat < 3;
                                     nRepeat++)
                                {
                                }
                                if (nRepeat > 2)
                                    break;

                                lpRowBuf[nBufPos]++;
                                lpRowBuf[nBufPos + lpRowBuf[nBufPos]] = (uint8_t)(imageData[i + x]);
                            }
                            if (imageData[i + x] >= 0 && x == frameHeader->left + frameHeader->width - 1 &&
                                lpRowBuf[nBufPos] < 0x3F)
                            {
                                lpRowBuf[nBufPos]++;
                                lpRowBuf[nBufPos + lpRowBuf[nBufPos]] = (uint8_t)(imageData[i + x]);
                            }
                            x--;
                            if (nLastOffset + nBufPos + 1 + lpRowBuf[nBufPos] <= 0xFFFF)
                                nBufPos += 1 + lpRowBuf[nBufPos];
                        }
                    }
                    else
                    {
                        if (imageData[i + x] < 0)
                        {
                            lpRowBuf[nBufPos] = 0x81;
                            if (nLastOffset + nBufPos + 1 <= 0xFFFF)
                                nBufPos++;
                        }
                        else
                        {
                            lpRowBuf[nBufPos] = 1;
                            lpRowBuf[nBufPos + 1] = (uint8_t)(imageData[i + x]);
                            if (nLastOffset + nBufPos + 2 <= 0xFFFF)
                                nBufPos += 2;
                        }
                    }
                }
                else
                {
                    lpRowBuf[nBufPos] = (uint8_t)(imageData[i + x]);
                    if (nLastOffset + nBufPos + 1 <= 0xFFFF)
                        nBufPos++;
                }
            }
        }

        frameData->lpRowOffsets[y] = nLastOffset;
        nLastOffset = frameData->lpRowOffsets[y] + nBufPos;

        frameData->lpRowSizes[y] = nBufPos;
        frameData->lpRowData[y] = (LPBYTE)malloc(nBufPos);
        memcpy(frameData->lpRowData[y], lpRowBuf, nBufPos);
    }

    frameData->size = nLastOffset;

    free(lpRowBuf);
}

// int16_t *createBeamGrp(int frames) {
//	int size = frames * 255 * 255;
//	int16_t *beam = new int16_t[size];
//
//	for (int i = 0; i < frames; ++i) {
//		generateBeamAngle(127, 127, M_PI * i / 16, 100, 20, beam + (i * 255 * 255));
//	}
//
//	return beam;
// }
//
// int16_t *beam = createBeamGrp(85);
// uint32_t grpSize = 0;
// GrpHead *beamGrp = createGRP(beam, 85, 255, 255, false, &grpSize);

//-------- Beam overlay: per-unit buffers, spawned on weapon fire --------//

namespace
{

constexpr int kBeamFrames = 9;
constexpr int kBeamCanvas = 255;
constexpr int kBeamThickness = 16;

// The beam is rasterized outward from the middle of the canvas, so only half
// the canvas is reachable in any one direction. A GRP frame's width/height are
// byte fields, so 255 is the hard ceiling and ~127 the practical reach; a siege
// tank outranges that (~224px), so long shots render short. Drawing into a
// tight bounding box and positioning it with the frame's own x/y offsets is
// what lifts this - see the bounding-box-limited encode in the handoff notes.
constexpr int kBeamOrigin = kBeamCanvas / 2;

// A slot's buffer is only freed once this many later shots have been fired,
// giving any overlay still animating off that slot time to finish. The overlay
// lifetime belongs to its iscript, not to us, so this is a safety margin rather
// than a guarantee - widen it first if beams ever flicker under sustained fire.
constexpr int kBeamRingDepth = 6;

struct BeamRingSlot
{
    int16_t *rasterBuffer = nullptr;
    GrpHead *grp = nullptr;
};

// One ring per firing unit, so simultaneous beams never share - and race on -
// the same buffer.
struct BeamState
{
    BeamRingSlot ring[kBeamRingDepth];
    int nextSlot = 0;
};

std::unordered_map<CUnit *, BeamState> beamStates;

// Draws kBeamFrames copies of the beam from the canvas origin out to
// (endX, endY), each one step dimmer than the last.
int16_t *rasterizeBeamFrames(int endX, int endY, int16_t *buffer)
{
    const int size = kBeamFrames * kBeamCanvas * kBeamCanvas;

    if (buffer != nullptr)
        delete[] buffer;

    buffer = new int16_t[size];
    std::fill(buffer, buffer + size, -1);

    for (int i = 0; i < kBeamFrames; ++i)
    {
        // Intensity ramps kBeamFrames..1 over the animation and must never
        // reach 0: generateBeam() early-returns on nColors == 0, which used to
        // leave the final frame fully transparent and hand a degenerate frame
        // to the GRP encoder.
        generateBeam(kBeamOrigin, kBeamOrigin, endX, endY, kBeamThickness,
                     buffer + (i * kBeamCanvas * kBeamCanvas), kBeamFrames - i);
    }

    return buffer;
}

} // namespace

void spawnBeamOverlay(CUnit *unit)
{
    if (unit == NULL || unit->sprite == NULL)
        return;

    // The image pool can run dry under enough simultaneous effects, in which
    // case createTopOverlay hands back null rather than an image.
    CImage *overlay = unit->sprite->createTopOverlay(ImageId::Explosion2_Small);
    if (overlay == NULL)
        return;

    // Aim along the firing unit's own facing. For a siege tank that is the
    // turret subunit, which is what fireWeaponHook hands us, so the beam tracks
    // the turret rather than the hull.
    const u8 direction = unit->currentDirection1;

    int length = (int)scbw::getDistanceFast(unit->position.x, unit->position.y, unit->orderTarget.pt.x,
                                            unit->orderTarget.pt.y);
    length = min(length, kBeamOrigin);

#if BEAM_DEBUG_FIXED_AIM
    // Straight east from the origin, with the endpoint written out literally so
    // this path holds up even if the angle table itself is what's broken.
    const int endX = kBeamOrigin + 96;
    const int endY = kBeamOrigin;
#else
    // Endpoint comes from Brood War's own angleDistance table rather than from
    // sin/cos over a radian conversion. This is the same call fireWeaponHook
    // uses to place the bullet, so the beam lines up with the turret by
    // construction instead of depending on which way a compass convention runs
    // - converting the direction byte to radians by hand still landed 90
    // degrees off in testing, for reasons the convention alone doesn't explain.
    const int endX = kBeamOrigin + scbw::getPolarX(length, direction);
    const int endY = kBeamOrigin + scbw::getPolarY(length, direction);
#endif

    BeamState &state = beamStates[unit];
    BeamRingSlot &slot = state.ring[state.nextSlot];
    state.nextSlot = (state.nextSlot + 1) % kBeamRingDepth;

    if (slot.grp != NULL)
    {
        delete[] reinterpret_cast<uint8_t *>(slot.grp);
        slot.grp = NULL;
    }

    slot.rasterBuffer = rasterizeBeamFrames(endX, endY, slot.rasterBuffer);

    uint32_t grpSize = 0; // out-param only, value unused
    slot.grp = reinterpret_cast<GrpHead *>(
        generateGrp(slot.rasterBuffer, kBeamFrames, kBeamCanvas, kBeamCanvas, false, &grpSize));

#if BEAM_DEBUG_PRINT
    // end == the origin (127,127) for a non-zero len means the angle table
    // lookup returned nothing, which would leave a zero-length beam. f0 of 0x0
    // means the rasterizer drew nothing, so the GRP encoder had nothing to
    // encode. Capped so a screen full of tanks doesn't bury the message area.
    static int debugPrintsLeft = 8;
    if (debugPrintsLeft > 0)
    {
        --debugPrintsLeft;

        char msg[160];
        sprintf_s(msg, sizeof(msg), "beam dir=%u len=%d end=%d,%d f0=%dx%d", (unsigned)direction, length, endX, endY,
                  slot.grp ? (int)(uint8_t)slot.grp->frames[0].width : -1,
                  slot.grp ? (int)(uint8_t)slot.grp->frames[0].height : -1);
        scbw::printText(msg);
    }
#endif

    overlay->grpOffset = slot.grp;
}

//-------- Beam shapes: queued per frame through the draw hook --------//

namespace
{

// How long an instantaneous beam stays visible. Queued shapes are cleared every
// frame, so this is just how many frames the beam gets re-queued for.
constexpr int kBeamVisibleFrames = 6;

// Most simultaneous beams tracked. Each costs kBeamThickness+1 shapes out of
// the graphics module's 10000 per frame, so neither limit is close.
constexpr int kMaxActiveBeams = 64;

// Brightest at the core, falling off to the edges. Same palette entries the GRP
// rasterizer uses, so the two paths read broadly alike - though this writes flat
// colors rather than blending against the background the way a remap does.
const graphics::ColorId kBeamRamp[] = {47, 45, 27, 27, 17, 11, 10, 10, 5, 5};
constexpr int kBeamRampLen = sizeof(kBeamRamp) / sizeof(kBeamRamp[0]);

struct ActiveBeam
{
    int startX, startY; // map coordinates
    int endX, endY;     // map coordinates
    int expiryFrame;
};

ActiveBeam activeBeams[kMaxActiveBeams];
int activeBeamCount = 0;

} // namespace

void fireBeam(CUnit *unit)
{
    if (unit == NULL || activeBeamCount >= kMaxActiveBeams)
        return;

    // Not clamped: the shape path has no length ceiling, so the beam reaches the
    // target however far off it is.
    const int length = (int)scbw::getDistanceFast(unit->position.x, unit->position.y, unit->orderTarget.pt.x,
                                                  unit->orderTarget.pt.y);
    const u8 direction = unit->currentDirection1;

    ActiveBeam &beam = activeBeams[activeBeamCount++];
    beam.startX = unit->position.x;
    beam.startY = unit->position.y;
    beam.endX = beam.startX + scbw::getPolarX(length, direction);
    beam.endY = beam.startY + scbw::getPolarY(length, direction);
    beam.expiryFrame = (int)*elapsedTimeFrames + kBeamVisibleFrames;
}

void drawActiveBeams()
{
    const int now = (int)*elapsedTimeFrames;

    // A new game restarts the frame counter, which would otherwise leave beams
    // from the previous one sitting at stale map coordinates until their expiry
    // frame came around again.
    static int lastSeenFrame = 0;
    if (now < lastSeenFrame)
        activeBeamCount = 0;
    lastSeenFrame = now;

    int live = 0;

    for (int i = 0; i < activeBeamCount; ++i)
    {
        const ActiveBeam beam = activeBeams[i];

        if (now >= beam.expiryFrame)
            continue;

        activeBeams[live++] = beam; // still alive - keep it for next frame

        const int dx = beam.endX - beam.startX;
        const int dy = beam.endY - beam.startY;
        const float len = sqrtf((float)(dx * dx + dy * dy));

        if (len < 1.0f)
            continue;

        // Walk the perpendicular to give the beam thickness. Coordinates stay in
        // map space and the graphics module converts them, so an off-screen beam
        // is rejected by the Bitmap's own clipping rather than by us.
        const float nx = -dy / len;
        const float ny = dx / len;
        const int half = kBeamThickness / 2;

        for (int w = -half; w <= half; ++w)
        {
            const int offsetX = (int)(nx * w);
            const int offsetY = (int)(ny * w);
            const int distanceFromCore = (w < 0) ? -w : w;
            const int rampIndex = (half > 0) ? (distanceFromCore * (kBeamRampLen - 1)) / half : 0;

            graphics::drawLine(beam.startX + offsetX, beam.startY + offsetY, beam.endX + offsetX,
                               beam.endY + offsetY, kBeamRamp[rampIndex], graphics::ON_MAP);
        }
    }

    activeBeamCount = live;
}
