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

	void executeFirstFrameRoutine() {
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
					// KYSXD Send all units to harvest on first run
					if (units_dat::BaseProperty[worker->id] & UnitProperty::Worker) {
						if (firstMineral[worker->playerId]) {
							worker->orderTo(OrderId::Harvest1, firstMineral[worker->playerId]);
						}
					}
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
			plugins::executeFirstFrameRoutine();

			utils::loopThroughVisibleUnits([](CUnit *unit) {
				switch (unit->id) {
					case UnitId::TerranSCV:
						unit->id;
						break;
				}
			});

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
