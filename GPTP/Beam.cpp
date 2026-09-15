#include "Beam.h"

using std::max;
using std::min;
using std::vector;

vector<Point16> getThickLineRect(int x1, int y1, int x2, int y2, int thickness)
{
    vector<Point16> rect;

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
    Point16 p1 = {static_cast<u16>(round(x1 + nx * half)), static_cast<u16>(round(y1 + ny * half))};
    Point16 p2 = {static_cast<u16>(round(x1 - nx * half)), static_cast<u16>(round(y1 - ny * half))};
    Point16 p3 = {static_cast<u16>(round(x2 - nx * half)), static_cast<u16>(round(y2 - ny * half))};
    Point16 p4 = {static_cast<u16>(round(x2 + nx * half)), static_cast<u16>(round(y2 + ny * half))};

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
    vector<Point16> rect = getThickLineRect(x1, y1, x2, y2, thickness);
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

    // Determine the vertical extent (bounding box) of the polygon.
    u16 ymin = height, ymax = 0;
    for (const auto &p : rect)
    {
        ymin = min(ymin, p.y);
        ymax = max(ymax, p.y);
    }
    ymin = max(ymin, (u16)0);
    ymax = min(ymax, (u16)(height - 1));

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
    vector<Point16> rect = getThickLineRect(x1, y1, x2, y2, thickness);
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

    // Determine the vertical extent (bounding box) of the polygon.
    u16 ymin = height, ymax = 0;
    for (const auto &p : rect)
    {
        ymin = min(ymin, p.y);
        ymax = max(ymax, p.y);
    }
    ymin = max(ymin, (u16)0);
    ymax = min(ymax, (u16)(height - 1));

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
