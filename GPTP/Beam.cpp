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

    // Size of the GRP allocation. The custom render function is handed a
    // GrpFrame* and nothing else, so this range is how it works out which beam
    // that frame belongs to.
    uint32_t grpSize = 0;

    // Endpoints in map coordinates, and uncapped - unlike the rasterized GRP,
    // which has to fit inside a 255px canvas. The blitter converts to screen
    // space itself via screenX/screenY, so it never has to know what the
    // image's screenPosition is anchored to.
    int mapStartX = 0, mapStartY = 0;
    int mapEndX = 0, mapEndY = 0;
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

#if BEAM_DEBUG_RENDERFN_PROBE

namespace
{

// The engine's own render function for our overlay's image, captured before we
// replace it so the probe can hand control back.
u32 originalRenderFunction = 0;

// The single image the probe is attached to. Kept so the capture can read that
// image's own screenPosition at the exact instant the engine calls the render
// function, which is what turns "ECX looks like an x" into a yes/no answer.
// Only one image carries the probe at a time, so this can never point at a
// different image than the one calling in.
CImage *probeImage = NULL;

// Written unconditionally by the naked thunk on every call - it has nowhere else
// to put them before it can call into C. Never report these directly: by the
// time reporting happens they hold the *last* call's values, not the captured
// one. That mismatch is why the first probe run reported a register set and a
// stack snapshot that came from different calls.
u32 rawEax, rawEcx, rawEdx, rawEbx, rawEsi, rawEdi, rawEsp;

// One coherent snapshot: registers, stack and the image's own state, all copied
// in the same call.
u32 probeEax, probeEcx, probeEdx, probeEbx, probeEsi, probeEdi, probeEsp;
u32 probeStack[6];
int probeImgX, probeImgY;
u32 probeImgFrame, probeImgGrp;
u8 probeRetBytes[8];
bool probePending = false;

// Runs inside the game's draw loop, so it does nothing but copy memory - no
// game API calls, no allocation, nothing re-entrant. Reporting happens later
// from a safe context.
void captureRenderProbe()
{
    static int capturesLeft = 4;
    if (capturesLeft <= 0 || probePending)
        return;
    --capturesLeft;

    probeEax = rawEax;
    probeEcx = rawEcx;
    probeEdx = rawEdx;
    probeEbx = rawEbx;
    probeEsi = rawEsi;
    probeEdi = rawEdi;
    probeEsp = rawEsp;

    const u32 *stack = (const u32 *)probeEsp;
    for (int i = 0; i < 6; ++i)
        probeStack[i] = stack[i];

    // The bytes the caller executes on return. If it cleans the arguments off
    // itself the first instruction is an ADD ESP, imm (83 C4 xx); if the callee
    // is expected to clean - __fastcall / __stdcall - it is anything else. That
    // settles the half of the convention register values cannot show.
    const u8 *ret = (const u8 *)probeStack[0];
    for (int i = 0; i < 8; ++i)
        probeRetBytes[i] = ret[i];

    // Ground truth for the two register arguments, read at the same instant the
    // engine passed them. Point16 stores these unsigned; screen positions go
    // negative for a partly off-screen image, so widen through s16.
    probeImgX = probeImgY = -32768;
    probeImgFrame = probeImgGrp = 0;
    if (probeImage != NULL)
    {
        probeImgX = (s16)probeImage->screenPosition.x;
        probeImgY = (s16)probeImage->screenPosition.y;
        probeImgGrp = (u32)probeImage->grpOffset;
        if (probeImage->grpOffset != NULL)
            probeImgFrame = (u32)&probeImage->grpOffset->frames[probeImage->frameIndex];
    }

    probePending = true;
}

// Called from the weapon fire path, which is ordinary game logic rather than
// the middle of a frame being drawn.
void reportRenderProbe()
{
    if (!probePending)
        return;
    probePending = false;

    char msg[200];

    // The two candidate register arguments against what the image actually held
    // at that instant. Equal on both -> args 1 and 2 are screenPosition.x/y,
    // passed in registers, and the __fastcall half of the convention is settled.
    sprintf_s(msg, sizeof(msg), "rfn reg cx=%X dx=%X img=%d,%d", probeEcx, probeEdx, probeImgX, probeImgY);
    scbw::printText(msg);

    // probeStack[0] is the return address; anything past it is a stack argument.
    sprintf_s(msg, sizeof(msg), "rfn stk a=%X b=%X c=%X d=%X", probeStack[1], probeStack[2], probeStack[3],
              probeStack[4]);
    scbw::printText(msg);

    sprintf_s(msg, sizeof(msg), "rfn img frm=%X grp=%X ax=%X", probeImgFrame, probeImgGrp, probeEax);
    scbw::printText(msg);

    sprintf_s(msg, sizeof(msg), "rfn ret %X: %02X %02X %02X %02X %02X %02X", probeStack[0], probeRetBytes[0],
              probeRetBytes[1], probeRetBytes[2], probeRetBytes[3], probeRetBytes[4], probeRetBytes[5]);
    scbw::printText(msg);
}

// Naked so no prologue runs before the registers are captured, and so nothing
// assumes a convention we have not established yet. Chains to the engine's
// function via push/ret (the idiom the *_inject.cpp thunks use), which leaves
// the stack exactly as it was on entry - so whatever convention the caller
// used, the original function still sees what it expects.
void __declspec(naked) beamRenderProbe()
{
    __asm {
        MOV rawEax, EAX
        MOV rawEcx, ECX
        MOV rawEdx, EDX
        MOV rawEbx, EBX
        MOV rawEsi, ESI
        MOV rawEdi, EDI
        MOV rawEsp, ESP
        PUSHAD
        PUSHFD
    }

    captureRenderProbe();

    __asm {
        POPFD
        POPAD
        PUSH originalRenderFunction
        RET
    }
}

#if BEAM_DEBUG_RENDERFN_MARKER
// Signature per the probe's findings: __fastcall puts the first two arguments in
// ECX/EDX and the rest on the stack, with coloringData last.
//
// This replaces the engine's drawing for the image rather than chaining to it,
// so nothing but the marker appears. Bitmap's public draw methods clip against
// the surface (the unclipped variants are the private *Unsafe ones), so a wrong
// guess about x/y should put the marker somewhere visibly wrong rather than
// corrupt memory. A wrong guess about the *convention*, though, unbalances the
// stack - so only turn this on once the probe above has confirmed both halves.
void __fastcall beamRenderMarker(int x, int y, void *frame, void *drawRect, int coloringData)
{
    gameScreenBuffer->drawFilledBox(x - 3, y - 3, x + 3, y + 3, graphics::WHITE);
}
#endif

} // namespace

#endif // BEAM_DEBUG_RENDERFN_PROBE

//-------- Custom render function: we own the blit --------//

#if BEAM_USE_CUSTOM_RENDER

namespace
{

// graphics::Bitmap keeps these private and exposes only clipped, flat-color
// drawing. Blending against the destination pixel needs the raw surface, so
// mirror the layout rather than widen upstream's class. Asserted against
// sizeof(graphics::Bitmap) below so a change upstream breaks the build rather
// than the game.
struct ScreenSurface
{
    u16 width;
    u16 height;
    u8 *data;
};

static_assert(sizeof(ScreenSurface) == sizeof(graphics::Bitmap),
              "ScreenSurface must mirror graphics::Bitmap's layout");

// Brightest at the core, falling off to the edges. Same palette entries the GRP
// rasterizer works from, so the custom path reads like the one it replaces.
const u8 kBeamCoreRamp[] = {47, 45, 27, 27, 17, 11, 10, 10, 5, 5};
constexpr int kBeamCoreRampLen = sizeof(kBeamCoreRamp) / sizeof(kBeamCoreRamp[0]);

// Every slot that has ever held a beam GRP. Bounded by (firing units x
// kBeamRingDepth), and never erased - std::unordered_map keeps pointers to its
// mapped values valid across rehashing, so these stay good for the process.
vector<const BeamRingSlot *> beamSlotRegistry;

void registerBeamSlot(const BeamRingSlot *slot)
{
    for (size_t i = 0; i < beamSlotRegistry.size(); ++i)
        if (beamSlotRegistry[i] == slot)
            return;
    beamSlotRegistry.push_back(slot);
}

// The render function is handed a GrpFrame* and nothing else that identifies
// the image, so the frame's address is the key: it points inside the GRP
// allocation the beam owns.
const BeamRingSlot *findBeamForFrame(const void *frame)
{
    const u8 *p = (const u8 *)frame;

    for (size_t i = 0; i < beamSlotRegistry.size(); ++i)
    {
        const BeamRingSlot *slot = beamSlotRegistry[i];
        const u8 *base = (const u8 *)slot->grp;

        if (base != NULL && p >= base && p < base + slot->grpSize)
            return slot;
    }

    return NULL;
}

// rctDraw's layout is the one thing the probe did not settle - it reported the
// pointer, not the bytes behind it. Rather than spend a build round trip on it,
// try both plausible layouts and take whichever describes a sane rectangle
// inside the surface. The bytes are dumped either way (see
// reportBeamRenderDebug), so the next edit can replace this with a fact.
//
// The fallback is the whole surface. Drawing over the console is a visible bug;
// clipping against a misread rectangle is a write off the end of the surface,
// so erring wide is the safe direction.
bool decodeClipRect(const void *rect, int surfW, int surfH, int &left, int &top, int &right, int &bottom)
{
    if (rect != NULL)
    {
        const s32 *as32 = (const s32 *)rect;
        if (as32[0] >= 0 && as32[1] >= 0 && as32[2] > as32[0] && as32[3] > as32[1] && as32[2] <= surfW &&
            as32[3] <= surfH)
        {
            left = as32[0];
            top = as32[1];
            right = as32[2];
            bottom = as32[3];
            return true;
        }

        const s16 *as16 = (const s16 *)rect;
        if (as16[0] >= 0 && as16[1] >= 0 && as16[2] > as16[0] && as16[3] > as16[1] && as16[2] <= surfW &&
            as16[3] <= surfH)
        {
            left = as16[0];
            top = as16[1];
            right = as16[2];
            bottom = as16[3];
            return true;
        }
    }

    left = 0;
    top = 0;
    right = surfW;
    bottom = surfH;
    return false;
}

#if BEAM_DEBUG_PRINT
// Captured on the first call and reported from spawnBeamOverlay, the same way
// the probe reports: this runs mid-frame, so it copies and nothing more.
bool rectDumpPending = false;
u32 rectDumpAddr = 0;
s32 rectDump32[4];
bool rectDumpDecoded = false;
int rectDumpL, rectDumpT, rectDumpR, rectDumpB;
#endif

// Writes one beam pixel, clipped. Flat palette entries for now: coloringData is
// handed to us and is the remap table (the probe confirmed that), but the
// table's row stride is still unverified, and indexing a remap table by a
// guessed stride reads outside it. Blending is the next step, once the
// colorShift dump below says how the table is shaped.
inline void plotBeamPixel(ScreenSurface *surface, int x, int y, int left, int top, int right, int bottom, u8 color)
{
    if (x < left || x >= right || y < top || y >= bottom)
        return;

    surface->data[y * surface->width + x] = color;
}

// Signature established by the probe, not assumed: args 1-2 in ECX/EDX, args
// 3-5 on the stack, callee cleans (the return site at 0x00497D4A carries no
// ADD ESP). See docs/beam-weapons.md.
//
// screenX/screenY are the image's own screenPosition. They are deliberately
// unused: the beam's endpoints are kept in map coordinates and converted here,
// so nothing depends on what a 255px canvas anchors its position to.
void __fastcall beamRenderFunction(int imageScreenX, int imageScreenY, GrpFrame *frame, void *rctDraw,
                                   void *coloringData)
{
    // Deliberately unused - see the note above on working in map coordinates.
    (void)imageScreenX;
    (void)imageScreenY;

    // The remap table. Not used yet; blending is the next step (see
    // docs/beam-weapons.md), and this is the argument it will come from.
    (void)coloringData;

    const BeamRingSlot *slot = findBeamForFrame(frame);
    if (slot == NULL || slot->grp == NULL)
        return;

    ScreenSurface *surface = (ScreenSurface *)gameScreenBuffer;
    if (surface == NULL || surface->data == NULL)
        return;

    int left, top, right, bottom;
    const bool decoded = decodeClipRect(rctDraw, surface->width, surface->height, left, top, right, bottom);

#if BEAM_DEBUG_PRINT
    if (!rectDumpPending && rctDraw != NULL)
    {
        rectDumpAddr = (u32)rctDraw;
        for (int i = 0; i < 4; ++i)
            rectDump32[i] = ((const s32 *)rctDraw)[i];
        rectDumpDecoded = decoded;
        rectDumpL = left;
        rectDumpT = top;
        rectDumpR = right;
        rectDumpB = bottom;
        rectDumpPending = true;
    }
#else
    (void)decoded;
#endif

    // Which step of the fade this is. The engine hands us the frame it would
    // have drawn, so the iscript still drives the animation - we just read the
    // index off it instead of blitting its pixels.
    const int frameIndex = (int)(frame - slot->grp->frames);
    if (frameIndex < 0 || frameIndex >= kBeamFrames)
        return;

    const int sx = slot->mapStartX - *screenX;
    const int sy = slot->mapStartY - *screenY;
    const int ex = slot->mapEndX - *screenX;
    const int ey = slot->mapEndY - *screenY;

    const int dx = ex - sx;
    const int dy = ey - sy;

    const double len = sqrt((double)dx * dx + (double)dy * dy);
    if (len < 1.0)
        return;

    // Unit perpendicular, for stepping across the beam's width.
    const double px = -dy / len;
    const double py = dx / len;

    // The beam thins and dims as the frame index rises, which is the pulse the
    // rasterized frames produce by dropping a colour per frame.
    const int half = max(1, (kBeamThickness * (kBeamFrames - frameIndex)) / (2 * kBeamFrames));
    const int fade = frameIndex;

    // One pixel per unit of length, which is what the scanline rasterizer this
    // replaces effectively did. Cost is proportional to the beam's length on
    // screen rather than to a fixed canvas.
    const int steps = (int)len;

    for (int t = -half; t <= half; ++t)
    {
        // Ramp index from the core outwards, then pushed further along by the
        // fade so later frames are dimmer as well as thinner.
        const int across = (t < 0) ? -t : t;
        int shade = (across * kBeamCoreRampLen) / (half + 1) + fade;
        if (shade >= kBeamCoreRampLen)
            continue;

        const u8 color = kBeamCoreRamp[shade];

        const int ox = (int)(px * t);
        const int oy = (int)(py * t);

        for (int i = 0; i <= steps; ++i)
        {
            const int x = sx + (dx * i) / steps + ox;
            const int y = sy + (dy * i) / steps + oy;
            plotBeamPixel(surface, x, y, left, top, right, bottom, color);
        }
    }
}

#if BEAM_DEBUG_PRINT
// Called from ordinary game logic, not mid-frame.
void reportBeamRenderDebug()
{
    static int printsLeft = 2;

    if (!rectDumpPending || printsLeft <= 0)
        return;
    rectDumpPending = false;
    --printsLeft;

    char msg[200];

    // Printed both ways: a Box32 reads as four plausible coordinates, a Box16
    // packed into the same bytes reads as two huge numbers and two zeros.
    sprintf_s(msg, sizeof(msg), "rct %X 32:%d,%d,%d,%d", rectDumpAddr, rectDump32[0], rectDump32[1], rectDump32[2],
              rectDump32[3]);
    scbw::printText(msg);

    const s16 *as16 = (const s16 *)rectDump32;
    sprintf_s(msg, sizeof(msg), "rct 16:%d,%d,%d,%d use %d,%d,%d,%d %s", as16[0], as16[1], as16[2], as16[3],
              rectDumpL, rectDumpT, rectDumpR, rectDumpB, rectDumpDecoded ? "ok" : "FALLBACK");
    scbw::printText(msg);

    // Shape of the remap tables, so blending can be added against facts rather
    // than against an assumed 256-byte stride.
    sprintf_s(msg, sizeof(msg), "shift bfire i=%u d=%X ofire i=%u d=%X", colorShift[ColorRemapping::BFire].index,
              (u32)colorShift[ColorRemapping::BFire].data, colorShift[ColorRemapping::OFire].index,
              (u32)colorShift[ColorRemapping::OFire].data);
    scbw::printText(msg);
}
#endif

} // namespace

#endif // BEAM_USE_CUSTOM_RENDER

//-------- Attaching a render function to the overlay --------//

namespace
{

// Points the overlay's per-instance render function at ours. Which "ours" is
// depends on the switches in Beam.h, in order of precedence: the real blitter,
// the position marker, then the register/stack probe.
void attachBeamRenderFunction(CImage *overlay)
{
    if (overlay == NULL || overlay->renderFunction == NULL)
        return;

#if BEAM_USE_CUSTOM_RENDER

    // Idempotent: the same overlay can be handed here more than once, and
    // chaining our own function into itself would recurse.
    if (overlay->renderFunction == (void *)&beamRenderFunction)
        return;

    const u32 engineRenderFunction = (u32)overlay->renderFunction;
    overlay->renderFunction = (void *)&beamRenderFunction;

#elif BEAM_DEBUG_RENDERFN_PROBE

    // A freshly created overlay always carries the engine's function, but guard
    // anyway: capturing our own probe here would chain it to itself.
    if (overlay->renderFunction == (void *)&beamRenderProbe)
        return;

    // One probed image at a time. Two at once would share a single
    // originalRenderFunction global - and, worse, would make probeImage a coin
    // flip, which is the whole basis of the x/y comparison. A CImage is
    // recycled rather than destroyed, so once the probed one is reused the
    // engine overwrites renderFunction; that is the signal that a later shot
    // may take the probe over.
    if (probeImage != NULL && probeImage->renderFunction == (void *)&beamRenderProbe)
        return;
    probeImage = overlay;

    originalRenderFunction = (u32)overlay->renderFunction;
    const u32 engineRenderFunction = originalRenderFunction;

#if BEAM_DEBUG_RENDERFN_MARKER
    overlay->renderFunction = (void *)&beamRenderMarker;
#else
    overlay->renderFunction = (void *)&beamRenderProbe;
#endif

#else

    // Nothing of ours to attach: the engine draws the generated GRP.
    return;

#endif // BEAM_USE_CUSTOM_RENDER

#if BEAM_DEBUG_PRINT
    static int printsLeft = 3;
    if (printsLeft > 0)
    {
        --printsLeft;

        char msg[200];
        // pal is images_dat::RLE_Function for this image id - the engine picks
        // the render function from it, so it is what a differing orig would
        // have to be explained by.
        sprintf_s(msg, sizeof(msg), "rfn set color=%X grp=%X orig=%X pal=%d", (u32)overlay->coloringData,
                  (u32)overlay->grpOffset, engineRenderFunction, (int)overlay->paletteType);
        scbw::printText(msg);
    }
#else
    (void)engineRenderFunction;
#endif
}

} // namespace


void spawnBeamOverlay(CUnit *unit)
{
#if BEAM_DEBUG_RENDERFN_PROBE
    // Whatever the probe captured during the last frame's drawing, reported now
    // that we are back in ordinary game logic rather than mid-frame.
    reportRenderProbe();
#endif

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

    // The full shot, uncapped. Siege mode alone is 12 tiles (~384px) and a
    // mod-defined weapon can reach further still, so this is the number the
    // beam should actually be drawn at.
    const int reach = (int)scbw::getDistanceFast(unit->position.x, unit->position.y, unit->orderTarget.pt.x,
                                                 unit->orderTarget.pt.y);

    // What fits in the GRP. A frame's width and height are byte fields, so the
    // rasterized copy cannot leave the 255px canvas however far the shot goes.
    // With BEAM_USE_CUSTOM_RENDER on, the GRP is no longer what gets drawn - it
    // supplies the image's bounds, the frame count the iscript animates
    // through, and a degraded but valid fallback if our function is ever not
    // attached.
    const int length = min(reach, kBeamOrigin);

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

    uint32_t grpSize = 0;
    slot.grp = reinterpret_cast<GrpHead *>(
        generateGrp(slot.rasterBuffer, kBeamFrames, kBeamCanvas, kBeamCanvas, false, &grpSize));
    slot.grpSize = grpSize;

    // Map coordinates, at full reach. The render function converts to screen
    // space itself, so the beam it draws is independent of both the 255px
    // canvas and whatever the image's screenPosition is anchored to.
    slot.mapStartX = unit->position.x;
    slot.mapStartY = unit->position.y;
#if BEAM_DEBUG_FIXED_AIM
    slot.mapEndX = slot.mapStartX + 96;
    slot.mapEndY = slot.mapStartY;
#else
    slot.mapEndX = slot.mapStartX + scbw::getPolarX(reach, direction);
    slot.mapEndY = slot.mapStartY + scbw::getPolarY(reach, direction);
#endif

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
        sprintf_s(msg, sizeof(msg), "beam dir=%u reach=%d len=%d map=%d,%d->%d,%d", (unsigned)direction, reach,
                  length, slot.mapStartX, slot.mapStartY, slot.mapEndX, slot.mapEndY);
        scbw::printText(msg);
    }
#endif

    overlay->grpOffset = slot.grp;

#if BEAM_USE_CUSTOM_RENDER
    // Registered before the render function is attached: the first call can
    // come as early as the next frame, and a beam the registry does not know
    // about simply is not drawn.
    registerBeamSlot(&slot);
#endif

    // Attached last, so coloringData and grpOffset are already set and get
    // reported alongside the engine's original render function pointer.
    attachBeamRenderFunction(overlay);

#if BEAM_USE_CUSTOM_RENDER && BEAM_DEBUG_PRINT
    // What the render function saw of rctDraw last frame, reported now that we
    // are back in ordinary game logic.
    reportBeamRenderDebug();
#endif
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
