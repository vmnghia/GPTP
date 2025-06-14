/// This is where to put various behaviors that cannot fit in a hook

#include "game_hooks.h"

#include <graphics/graphics.h>

#include <SCBW/UnitFinder.h>
#include <SCBW/api.h>

#include "../psi_field.h"

#include <cstdio>

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
				graphics::drawBox(left + 8 * i, top + 6, left + 8 * i + 8, bottom + 4, graphics::COBALT, graphics::ON_MAP);
				graphics::drawFilledBox(left + 8 * i + 1, innerTop + 5, left + 8 * i + 7, innerBottom + 3, graphics::CHARCOAL,
				                        graphics::ON_MAP);

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
		progressWidth = progress * (innerRight - innerLeft);
		if (isTraining || isUpgradingOrResearching || isBuildingSelf) {
			graphics::drawFilledBox(innerLeft, innerTop, innerLeft + progressWidth, innerBottom - 1, graphics::AQUA,
			                        graphics::ON_MAP);
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
						break;
				}

				plugins::drawBuildProgress(unit);
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
