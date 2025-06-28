/// This is where to put various behaviors that cannot fit in a hook

#define _USE_MATH_DEFINES
#include "game_hooks.h"
#include <cmath>

#include <graphics/graphics.h>

#include <SCBW/UnitFinder.h>
#include <SCBW/api.h>

#include "../psi_field.h"

#include <algorithm>
#include <cstdio>
#include <vector>

int16_t *buffer = new int16_t[17 * 255 * 255 * 2];

int16_t *generateBeamAngle(int x, int y, float angle, int length, int thickness, int16_t *buffer);
uint8_t *createGRP(int16_t *imageData, uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, bool noCompress,
                   uint32_t *grpSize);

namespace utils {
	void loopThroughVisibleUnits(void (*callback)(CUnit *)) {
		// Loop through all units in the game.
		for (CUnit *unit = *firstVisibleUnit; unit; unit = unit->link.next) {
			callback(unit);
		}
	}

	bool isMineral(const CUnit *resource) {
		return resource->id >= UnitId::ResourceMineralField && resource->id <= UnitId::ResourceMineralFieldType3;
	}
} // namespace utils

namespace plugins {
	class HarvestTargetFinder : public scbw::UnitFinderCallbackMatchInterface {
		CUnit *mainHarvester;

	  public:
		void setMainHarvester(CUnit *mainHarvester) {
			this->mainHarvester = mainHarvester;
		}
		bool match(CUnit *unit) {
			if (!unit)
				return false;

			if (mainHarvester->getDistanceToTarget(unit) > (16 << 5)) // Harvest distance
				return false;

			if (!(utils::isMineral(unit)))
				return false;

			return true;
		}
	};
	HarvestTargetFinder harvestTargetFinder;

	void exploreMap() {
		for (int x = 0; x < mapTileSize->width; x++) {
			for (int y = 0; y < mapTileSize->height; y++) {

				if ((x + y) % 2) {
					ActiveTile *currentTile = &(*activeTileArray)[(x) + mapTileSize->width * (y)];
					currentTile->exploredFlags = 0;
				}
			}
		}
		// By RavenWolf:
		// if vespene geyser or a mineral field, reveal it to all players
		for (CUnit *unit = *firstVisibleUnit; unit; unit = unit->link.next) {
			if (utils::isMineral(unit) || unit->id == UnitId::ResourceVespeneGeyser) {
				unit->sprite->visibilityFlags = 0xFF;
			}
		}
	}

	void initializeGame() {
		if (*elapsedTimeFrames == 0) {
			scbw::printText(PLUGIN_NAME ": Hello, world!");

			if (*GAME_TYPE != GameType::UseMapSettings) {
				exploreMap();
				CUnit *firstMineral[8];
				for (CUnit *base = *firstVisibleUnit; base; base = base->link.next) {
					if (units_dat::BaseProperty[base->id] & UnitProperty::ResourceDepot) {
						harvestTargetFinder.setMainHarvester(base);
						firstMineral[base->playerId] = scbw::UnitFinder::getNearestTarget(
						    base->getX() - 512, base->getY() - 512, base->getX() + 512, base->getY() + 512, base,
						    harvestTargetFinder);
					}
				}

				for (CUnit *worker = *firstVisibleUnit; worker; worker = worker->link.next) {
					if (units_dat::BaseProperty[worker->id] & UnitProperty::Worker) {
						if (firstMineral[worker->playerId]) {
							worker->orderTo(OrderId::Harvest1, firstMineral[worker->playerId]);
						}
					}
				}
			}
		}
	}

	void drawBuildProgress(CUnit *unit) {
		int width = unit->getRight() - unit->getLeft();
		int height = 4;
		int left = unit->getLeft();
		int top = unit->getTop() - 24;
		int right = unit->getRight();
		int bottom = top + height;
		int innerLeft = left + 1;
		int innerTop = top + 1;
		int innerRight = right - 1;
		int innerBottom = bottom;
		float progress = 0;
		int progressWidth = 0;

		int queueLength = 0;

		bool isTraining = unit->secondaryOrderId == OrderId::Train;
		bool isUpgradingOrResearching =
		    unit->mainOrderId == OrderId::ResearchTech || unit->mainOrderId == OrderId::Upgrade && !isTraining;
		bool isBuildingSelf = unit->mainOrderId == OrderId::BuildSelf1 || unit->mainOrderId == OrderId::BuildSelf2 ||
		                      unit->mainOrderId == OrderId::Morph2 ||
		                      (unit->mainOrderId == OrderId::Nothing2 && unit->remainingBuildTime > 0);

		if (units_dat::BaseProperty[unit->id] & UnitProperty::Addon) {
			left += 8;
			innerLeft += 8;
			right += 8;
			innerRight += 8;
			top += 4;
			innerTop += 4;
			bottom += 4;
			innerBottom += 4;
		}

		for (int i = 0; i < 5; i++) {
			if (unit->buildQueue[i] != UnitId::None) {
				queueLength++;
			}
		}

		if (isTraining) {
			for (int i = 0; i < 5; i++) {
				// draw empty boxes border and background
				graphics::drawBox(left + 8 * i, top + 6, left + 8 * i + 8, bottom + 4, graphics::COBALT,
				                  graphics::ON_MAP);
				graphics::drawFilledBox(left + 8 * i + 1, innerTop + 5, left + 8 * i + 7, innerBottom + 3,
				                        graphics::CHARCOAL, graphics::ON_MAP);

				// draw filled boxes
				if (queueLength > 0 && i < queueLength) {
					graphics::drawFilledBox(left + 8 * i + 1, innerTop + 5, left + 8 * i + 7, innerBottom + 3, 126,
					                        graphics::ON_MAP);
				}
			}
		}

		// draw empty progress bar border and background
		if (isTraining || isUpgradingOrResearching || isBuildingSelf) {
			graphics::drawBox(left, top, right, bottom, graphics::COBALT, graphics::ON_MAP);
			graphics::drawFilledBox(innerLeft, innerTop, innerRight, innerBottom - 1, graphics::CHARCOAL,
			                        graphics::ON_MAP);
		}

		int timeCost = 1;
		int elapsedBuildTime = 0;

		if (unit->currentBuildUnit && isTraining) {
			CUnit *unitInQueue = unit->currentBuildUnit;
			timeCost = units_dat::TimeCost[unitInQueue->id];
			elapsedBuildTime = timeCost - unitInQueue->remainingBuildTime;
		} else if (isUpgradingOrResearching) {
			switch (unit->mainOrderId) {
				case OrderId::ResearchTech:
					timeCost = techdata_dat::TimeCost[unit->building.techType];
					break;
				case OrderId::Upgrade:
					timeCost =
					    upgrades_dat::TimeCostBase[unit->building.upgradeType] +
					    (upgrades_dat::TimeCostFactor[unit->building.upgradeType] * (unit->building.upgradeLevel - 1));
					break;
			}
			elapsedBuildTime = timeCost - unit->building.upgradeResearchTime;
		} else if (isBuildingSelf) {
			timeCost = units_dat::TimeCost[unit->id];
			top = std::max(unit->getTop(), (short)unit->sprite->position.y);
			if (playerTable[unit->playerId].race == RaceId::Protoss) {
				timeCost += 24; // Protoss buildings warp flash animation time
			}
			elapsedBuildTime = timeCost - unit->remainingBuildTime;
		}

		// draw progress
		progress = (float)elapsedBuildTime / timeCost;
		progressWidth = (int)std::round(progress * (innerRight - innerLeft));
		if (isTraining || isUpgradingOrResearching || isBuildingSelf) {
			graphics::drawFilledBox(innerLeft, innerTop, innerLeft + progressWidth, innerBottom - 1, graphics::AQUA,
			                        graphics::ON_MAP);
		}
	}

	void drawRallyPoint(CUnit *unit) {
		u16 rallyX, rallyY;
		CUnit *rallyTarget = unit->rally.unit;
		graphics::ColorId color = 83;

		if (rallyTarget != NULL && units_dat::BaseProperty[rallyTarget->id] & UnitProperty::ResourceContainer) {
			color = graphics::YELLOW;
		}

		if (unit->rally.pt.x == 0 && unit->rally.pt.y == 0) { // If the rally point is not set
			rallyX = unit->position.x;
			rallyY = unit->position.y;
		} else { // If the rally point is set
			rallyX = unit->rally.pt.x;
			rallyY = unit->rally.pt.y;
		}

		graphics::drawChevronLine(unit->position.x, unit->position.y, rallyX, rallyY, color, graphics::ON_MAP);
		graphics::drawDottedEllipse(rallyX - 8, rallyY - 4, rallyX + 8, rallyY + 4, color, graphics::ON_MAP);
		graphics::drawDot(rallyX, rallyY, color, graphics::ON_MAP);
	}

	void drawOrderQueue(CUnit *unit) {
		COrder *currentOrder = unit->orderQueueHead;
		graphics::ColorId color = graphics::GREEN;

		if (currentOrder != NULL) {
			// SCV destination while constructing
			if (currentOrder->target.pt.x == 0 && currentOrder->target.pt.y == 0) {
				if (unit->orderQueueTail != NULL)
					graphics::drawDottedLine(unit->position.x, unit->position.y, unit->orderQueueTail->target.pt.x,
					                         unit->orderQueueTail->target.pt.y, unit->getColor(),
					                         graphics::CoordType::ON_MAP);
			} else {
				switch (currentOrder->orderId) {
					case OrderId::AttackMove:
					case OrderId::AttackUnit:
						color = graphics::RED;
						break;
					case OrderId::Patrol:
						color = graphics::BLUE;
						break;
					default:
						color = graphics::GREEN;
						break;
				}
				graphics::drawChevronLine(unit->position.x, unit->position.y, unit->orderTarget.pt.x,
				                          unit->orderTarget.pt.y, color, graphics::CoordType::ON_MAP);
				graphics::drawDottedEllipse(unit->orderTarget.pt.x - 8, unit->orderTarget.pt.y - 4,
				                            unit->orderTarget.pt.x + 8, unit->orderTarget.pt.y + 4, color,
				                            graphics::ON_MAP);

				graphics::drawChevronLine(unit->orderTarget.pt.x, unit->orderTarget.pt.y, currentOrder->target.pt.x,
				                          currentOrder->target.pt.y, color, graphics::CoordType::ON_MAP);
				graphics::drawDottedEllipse(currentOrder->target.pt.x - 8, currentOrder->target.pt.y - 4,
				                            currentOrder->target.pt.x + 8, currentOrder->target.pt.y + 4, color,
				                            graphics::ON_MAP);

				currentOrder = currentOrder->next;

				while (currentOrder != NULL && (currentOrder->target.pt.x != 0 && currentOrder->target.pt.y != 0)) {
					if (currentOrder->orderId == OrderId::AttackMove || currentOrder->orderId == OrderId::AttackUnit) {
						color = graphics::RED;
					} else {
						color = graphics::GREEN;
					}

					graphics::drawChevronLine(currentOrder->prev->target.pt.x, currentOrder->prev->target.pt.y,
					                          currentOrder->target.pt.x, currentOrder->target.pt.y, color,
					                          graphics::CoordType::ON_MAP);
					graphics::drawDottedEllipse(currentOrder->target.pt.x - 8, currentOrder->target.pt.y - 4,
					                            currentOrder->target.pt.x + 8, currentOrder->target.pt.y + 4, color,
					                            graphics::ON_MAP);

					currentOrder = currentOrder->next;
				}
			}
		}
	}

} // namespace plugins

namespace hooks {

	/// This hook is called every frame.
	bool nextFrame() {

		if (!scbw::isGamePaused()) { // If the game is not paused

			scbw::setInGameLoopState(true); // Needed for scbw::random() to work
			graphics::resetAllGraphics();
			hooks::updatePsiFieldProviders();
			plugins::initializeGame();

			u32 idleWorkerCount = 0;

			for (CUnit *unit = *firstVisibleUnit; unit; unit = unit->link.next) {
				if ((unit->playerId == *LOCAL_NATION_ID || scbw::isInReplay()) &&
				    units_dat::BaseProperty[unit->id] & UnitProperty::Worker &&
				    unit->mainOrderId == OrderId::PlayerGuard) {
					++idleWorkerCount;
				}

				switch (unit->id) {
					case UnitId::TerranSCV:
						/*int x1 = unit->position.x;
						int y1 = unit->position.y;

						int length = 50;

						for (int i = 0; i < 34; i++) {
						    double pi = 3.14159265358979323846;
						    float angle = pi * i / 16 - pi / 2;
						    int x2 = x1 + (length * cos(angle));
						    int y2 = y1 + (length * sin(angle));
						    graphics::drawLine(x1, y1, x2, y2, graphics::GREEN, graphics::ON_MAP);
						}*/

						break;
				}

				plugins::drawBuildProgress(unit);
			}

			for (int i = 0; i < SELECTION_ARRAY_LENGTH; ++i) {
				CUnit *selUnit = clientSelectionGroup->unit[i];

				if (selUnit == nullptr)
					continue;

				if (selUnit->playerId == *LOCAL_NATION_ID || !scbw::isInReplay()) {
					if (units_dat::GroupFlags[selUnit->id].isFactory) {
						plugins::drawRallyPoint(selUnit);
					}

					if (selUnit != NULL) {
						plugins::drawOrderQueue(selUnit);
					}
				}
			}

			/*utils::loopThroughVisibleUnits([](CUnit *unit) {

			});*/

			/*char buffer[200];
			sprintf(buffer, "actStringID: %05d; %05d; %05d; %05d; %05d; %05d",
			        buttonSetTable[UnitId::TerranMedic].firstButton[5].actStringID,
			        buttonSetTable[UnitId::TerranMedic].firstButton[5].actVar,
			        buttonSetTable[UnitId::TerranMedic].firstButton[5].iconID,
			        buttonSetTable[UnitId::TerranMedic].firstButton[5].position,
			        buttonSetTable[UnitId::TerranMedic].firstButton[5].reqStringID,
			        buttonSetTable[UnitId::TerranMedic].firstButton[5].reqVar);
			graphics::drawText(0, 0, buffer);*/

			if (idleWorkerCount != 0) {
				char idleworkers[64];
				sprintf_s(idleworkers, "Idle Workers: %d", idleWorkerCount);
				graphics::drawText(5, 5, idleworkers, graphics::FONT_MEDIUM, graphics::ON_SCREEN);
			}

			scbw::setInGameLoopState(false);
		}

		return true;
	}

	;

	bool gameOn() {
		return true;
	}

	;

	bool gameEnd() {
		return true;
	}

	;

} // namespace hooks

namespace plugins {}

namespace helpers {

	const u32 Helper_CreateBullet = 0x0048C260;
	void createBullet(u8 weaponId, const CUnit *source, s16 x, s16 y, u8 attackingPlayer, u8 direction) {
		u32 attackingPlayer_ = attackingPlayer, direction_ = direction;
		s32 x_ = x, y_ = y;

		__asm {
    PUSHAD
    PUSH direction_
    PUSH attackingPlayer_
    PUSH y_
    PUSH x_
    MOV EAX, source
    MOVZX ECX, weaponId
    CALL Helper_CreateBullet
    POPAD
		}
	}
} // namespace helpers

using std::max;
using std::min;
using std::vector;

vector<Point16> getThickLineRect(int x1, int y1, int x2, int y2, int thickness) {
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

int16_t *generateBeam(int x1, int y1, int x2, int y2, int thickness, int16_t *buffer) {
	int width = 255;
	int height = 255;
	vector<GradientStop> stops = {{0.0f, 9}, {0.5f, 0}, {1.0f, 9}};
	vector<Point16> rect = getThickLineRect(x1, y1, x2, y2, thickness);
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
	u16 ymin = height, ymax = 0;
	for (const auto &p : rect) {
		ymin = min(ymin, p.y);
		ymax = max(ymax, p.y);
	}
	ymin = max(ymin, (u16)0);
	ymax = min(ymax, (u16)(height - 1));

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

				// unsigned char *dest = pixelData + (height - 1 - y) * rowSize; // Assuming 4 bytes per pixel (RGBA)
				buffer[y * 255 + x] = colors[pixelColor];
			}
		}
	}

	return buffer;
}

int16_t *generateBeamAngle(int x, int y, float angle, int length, int thickness, int16_t *buffer) {
	int x2 = x + static_cast<int>(length * sin(angle));
	int y2 = y - static_cast<int>(length * cos(angle));

	return generateBeam(x, y, x2, y2, thickness, buffer);
}

void encodeFrameData(int16_t *imageData, uint16_t frame, GrpHead *grpHeader, FrameHeader *frameHeader,
                     FrameData *frameData, bool noCompress) {
	int x, y, i, j, nBufPos, nRepeat;
	uint8_t *lpRowBuf;
	uint16_t nLastOffset = 0;

	frameData->lpRowOffsets = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
	frameData->lpRowSizes = (uint16_t *)malloc(frameHeader->height * sizeof(uint16_t));
	frameData->lpRowData = (LPBYTE *)malloc(frameHeader->height * sizeof(LPBYTE));
	lpRowBuf = (uint8_t *)malloc(frameHeader->width * 2);

	if (!noCompress)
		nLastOffset = frameHeader->height * sizeof(uint16_t);

	for (y = 0; y < frameHeader->height; y++) {
		i = frame * grpHeader->width * grpHeader->height + (frameHeader->top + y) * grpHeader->width;

		if (!noCompress) {
			// Search for duplicate rows
			for (x = 0; x < y; x++) {
				j = frame * grpHeader->width * grpHeader->height + (frameHeader->top + x) * grpHeader->width;
				if (memcmp(&imageData[i + frameHeader->left], &imageData[j + frameHeader->left],
				           grpHeader->width * sizeof(short)) == 0)
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
		frameData->lpRowData[y] = (LPBYTE)malloc(nBufPos);
		memcpy(frameData->lpRowData[y], lpRowBuf, nBufPos);
	}

	frameData->size = nLastOffset;

	free(lpRowBuf);
}

uint8_t *createGRP(int16_t *imageData, uint16_t frames, uint16_t maxWidth, uint16_t maxHeight, bool noCompress,
                   uint32_t *grpSize) {
	GrpHead grpHead;
	FrameHeader *frameHeaders;
	FrameData *frameData;
	uint8_t *lpGrpData;
	int i, j, x, y, x1, x2, y1, y2;
	unsigned long lastOffset;

	if (!imageData || !grpSize) {
		return (uint8_t *)(-1);
	}

	grpHead.frameCount = frames;
	grpHead.width = maxWidth;
	grpHead.height = maxHeight;

	// frameHeaders = (FrameHeader*)malloc(sizeof(FrameHeader) * frames);
	frameHeaders = new FrameHeader[frames];
	// frameData = (FrameData*)malloc(sizeof(FrameData) * frames);
	frameData = new FrameData[frames];
	lastOffset = sizeof(GrpHead) + sizeof(FrameHeader) * frames;

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
			if (frameData[j].lpRowOffsets && frameHeaders[i].width == frameHeaders[j].width &&
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
			frameData[i].lpRowOffsets = 0;
			frameData[i].lpRowSizes = 0;
			frameData[i].lpRowData = 0;
			frameData[i].size = 0;
			continue;
		}

		encodeFrameData(imageData, i, &grpHead, &frameHeaders[i], &frameData[i], noCompress);
		lastOffset = frameHeaders[i].offset + frameData[i].size;
	}

	// lpGrpData = (uint8_t *)malloc(lastOffset);
	lpGrpData = new uint8_t[lastOffset];

	// Write completed GRP to buffer
	memcpy(lpGrpData, &grpHead, sizeof(GrpHead));
	memcpy(lpGrpData + sizeof(GrpHead), frameHeaders, frames * sizeof(FrameHeader));

	for (i = 0; i < frames; i++) {
		if (frameData[i].lpRowOffsets) {
			if (!noCompress)
				memcpy(lpGrpData + frameHeaders[i].offset, frameData[i].lpRowOffsets,
				       frameHeaders[i].height * sizeof uint16_t);

			for (y = 0; y < frameHeaders[i].height; y++) {
				if (frameData[i].lpRowData[y]) {
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
