#pragma once
#define _USE_MATH_DEFINES
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <vector>

using namespace std;

struct Point {
	int x;
	int y;
};

struct Pixel {
	int index;       // Linear index: row * width + column
	int x;           // Column coordinate
	int y;           // Row coordinate (top row is 0)
	unsigned char r; // Red channel
	unsigned char g; // Green channel
	unsigned char b; // Blue channel
	unsigned char data;
};

struct GradientStop {
	float position;
	int color;
};

// Structure for the BMP file header (14 bytes)
#pragma pack(push, 1) // Ensures no padding between members
struct BitmapFileHeader {
	uint16_t bfType;      // Specifies the file type (0x4D42 for 'BM')
	uint32_t bfSize;      // Specifies the size of the file in bytes
	uint16_t bfReserved1; // Reserved; set to 0
	uint16_t bfReserved2; // Reserved; set to 0
	uint32_t bfOffBits;   // Specifies the offset to the pixel data
};
#pragma pack(pop)

// Structure for the BMP info header (40 bytes for BITMAPINFOHEADER)
#pragma pack(push, 1)
struct BitmapInfoHeader {
	uint32_t biSize;         // Specifies the size of the BITMAPINFOHEADER structure
	int32_t biWidth;         // Specifies the width of the bitmap in pixels
	int32_t biHeight;        // Specifies the height of the bitmap in pixels
	uint16_t biPlanes;       // Specifies the number of planes of the target device
	uint16_t biBitCount;     // Specifies the number of bits per pixel
	uint32_t biCompression;  // Specifies the type of compression
	uint32_t biSizeImage;    // Specifies the size of the image data in bytes
	int32_t biXPelsPerMeter; // Specifies the horizontal resolution
	int32_t biYPelsPerMeter; // Specifies the vertical resolution
	uint32_t biClrUsed;      // Specifies the number of color indexes in the color table
	uint32_t biClrImportant; // Specifies the number of color indexes required
	// for displaying the bitmap
};
#pragma pack(pop)

class Canvas {

	BitmapFileHeader fileHeader;
	BitmapInfoHeader infoHeader;
	unsigned char *buffer;
	unsigned char *pixelData;

  public:
	Canvas(int width, int height, unsigned char *palette);
	~Canvas() = default;
	void drawLine(int x1, int y1, int x2, int y2, int thickness = 1);
	void drawLineAngle(int x, int y, float angle, int length, int thickness = 1);
	unsigned char *getPixelData() const {
		return pixelData;
	};
	unsigned char *getBuffer() const {
		return buffer;
	};
	BitmapFileHeader getFileHeader() const {
		return fileHeader;
	};
	BitmapInfoHeader getInfoHeader() const {
		return infoHeader;
	};

  protected:
	vector<Point> getThickLineRect(int x1, int y1, int x2, int y2, int thickness);
};
