#include "Canvas.h"

Canvas::Canvas(int width, int height, unsigned char *palette) {
	// Each pixel is 1 byte. Each row must be padded to a multiple of 4 bytes.
	int rowSize = ((width + 3) / 4) * 4; // row size in bytes
	int pixelDataSize = rowSize * height;

	// Palette: for 8-bit images, we need 256 entries (4 bytes each).
	int paletteSize = 256 * 4;
	int fileHeaderSize = sizeof(BitmapFileHeader);                 // 14 bytes
	int infoHeaderSize = sizeof(BitmapInfoHeader);                 // 40 bytes
	int bfOffBits = fileHeaderSize + infoHeaderSize + paletteSize; // Typically 14 + 40 + 1024 = 1078 bytes
	int fileSize = bfOffBits + pixelDataSize;

	fileHeader.bfType = 0x4D42; // 'BM'
	fileHeader.bfSize = fileSize;
	fileHeader.bfReserved1 = 0;
	fileHeader.bfReserved2 = 0;
	fileHeader.bfOffBits = bfOffBits;

	infoHeader.biSize = infoHeaderSize;
	infoHeader.biWidth = width;
	infoHeader.biHeight = height; // A positive height indicates BMP expects pixel rows bottom-up.
	infoHeader.biPlanes = 1;
	infoHeader.biBitCount = 8;    // 8-bit per pixel
	infoHeader.biCompression = 0; // BI_RGB, no compression
	infoHeader.biSizeImage = pixelDataSize;
	infoHeader.biXPelsPerMeter = 72; // ~96 DPI
	infoHeader.biYPelsPerMeter = 72;
	infoHeader.biClrUsed = 256;
	infoHeader.biClrImportant = 256;

	buffer = new unsigned char[fileSize];

	std::fill(buffer, buffer + fileSize, 0);
	memcpy(buffer, &fileHeader, fileHeaderSize);
	memcpy(buffer + fileHeaderSize, &infoHeader, infoHeaderSize);

	unsigned char *palettePtr = buffer + fileHeaderSize + infoHeaderSize;
	for (int i = 0; i < 256; i++) {
		palettePtr[i * 4 + 0] = palette[i * 4 + 0]; // Blue
		palettePtr[i * 4 + 1] = palette[i * 4 + 1]; // Green
		palettePtr[i * 4 + 2] = palette[i * 4 + 2]; // Red
		palettePtr[i * 4 + 3] = 0;                  // Reserved
	}

	pixelData = buffer + bfOffBits;
}

void Canvas::drawLine(int x1, int y1, int x2, int y2, int thickness) {
	vector<GradientStop> stops = {{0.0f, 9}, {0.5f, 0}, {1.0f, 9}};
	vector<Point> rect = getThickLineRect(x1, y1, x2, y2, thickness);
	int width = infoHeader.biWidth;
	int height = infoHeader.biHeight;
	float lineAngle = atan2(y2 - y1, x2 - x1);

	int rowSize = ((width + 3) / 4) * 4; // row size in bytes
	int colors[10] = {47, 45, 27, 27, 17, 11, 10, 10, 5, 5};
	float gradX = cos(lineAngle + M_PI_2);
	float gradY = sin(lineAngle + M_PI_2);

	float minProj = FLT_MAX, maxProj = -FLT_MAX;
	for (const auto &p : rect) {
		float proj = p.x * gradX + p.y * gradY;
		minProj = min(minProj, proj);
		maxProj = max(maxProj, proj);
	}

	// Determine the vertical extent (bounding box) of the polygon.
	int ymin = height, ymax = 0;
	for (const auto &p : rect) {
		ymin = min(ymin, p.y);
		ymax = max(ymax, p.y);
	}
	ymin = max(ymin, 0);
	ymax = min(ymax, height - 1);

	// Process each scanline within the polygon's vertical bounds.
	for (int y = ymin; y <= ymax; y++) {
		vector<int> intersections;

		// For each edge of the polygon, compute the x-coordinate where the edge intersects the scanline.
		for (size_t i = 0; i < rect.size(); i++) {
			size_t j = (i + 1) % rect.size();
			int x1 = rect[i].x, y1 = rect[i].y;
			int x2 = rect[j].x, y2 = rect[j].y;

			// Check if the scanline at y intersects the edge.
			if ((y1 <= y && y < y2) || (y2 <= y && y < y1)) {
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
		for (size_t i = 0; i + 1 < intersections.size(); i += 2) {
			int xStart = max(intersections[i], 0);
			int xEnd = min(intersections[i + 1], width - 1);
			for (int x = xStart; x <= xEnd; x++) {
				// Compute projection of (x, y) on the gradient axis.
				float proj = x * gradX + y * gradY;
				// Normalize t between 0 and 1.
				float t = (maxProj - minProj != 0) ? (proj - minProj) / (maxProj - minProj) : 0;
				t = std::max(0.0f, std::min(t, 1.0f));

				int pixelColor = 0;
				if (t <= stops.front().position) {
					pixelColor = stops.front().color;
				} else if (t >= stops.back().position) {
					pixelColor = stops.back().color;
				} else {
					// Iterate through stops to find the proper interval.
					for (size_t s = 0; s < stops.size() - 1; s++) {
						if (t >= stops[s].position && t <= stops[s + 1].position) {
							float localT = (t - stops[s].position) / (stops[s + 1].position - stops[s].position);
							// Linear interpolation between the two stop colors.
							pixelColor =
							    static_cast<int>(round(stops[s].color * (1 - localT) + stops[s + 1].color * localT));
							break;
						}
					}
				}

				unsigned char *dest = pixelData + (height - 1 - y) * rowSize; // Assuming 4 bytes per pixel (RGBA)
				dest[x] = colors[pixelColor];
			}
		}
	}
}

void Canvas::drawLineAngle(int x, int y, float angle, int length, int thickness) {
	int x2 = x + static_cast<int>(length * sin(angle));
	int y2 = y - static_cast<int>(length * cos(angle));

	std::cout << "Drawing line from (" << x << ", " << y << ") to (" << x2 << ", " << y2 << ") with angle " << angle
	          << " and length " << length << std::endl;

	drawLine(x, y, x2, y2, thickness);
}

vector<Point> Canvas::getThickLineRect(int x1, int y1, int x2, int y2, int thickness) {
	vector<Point> rect;

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
	Point p1 = {static_cast<int>(round(x1 + nx * half)), static_cast<int>(round(y1 + ny * half))};
	Point p2 = {static_cast<int>(round(x1 - nx * half)), static_cast<int>(round(y1 - ny * half))};
	Point p3 = {static_cast<int>(round(x2 - nx * half)), static_cast<int>(round(y2 - ny * half))};
	Point p4 = {static_cast<int>(round(x2 + nx * half)), static_cast<int>(round(y2 + ny * half))};

	// Return the vertices in order.
	// The order here is p1, p2, p3, p4 which (for many cases) forms a clockwise polygon.
	rect.push_back(p1);
	rect.push_back(p2);
	rect.push_back(p3);
	rect.push_back(p4);

	return rect;
}