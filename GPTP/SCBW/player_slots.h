//One pointer a playing player, for work done once per player (the mineral
//field a player's workers start on).
//
//Pure: no game memory. tests/player_slots_test.cpp builds it on its own.
#pragma once
#include "../types.h"
#include <cstddef>

namespace playerslots {

//Players 0 to 7 play. The game also has players 8 to 11 (neutral and the
//like), who own units and take no part.
const u32 PLAYING_PLAYERS = 8;

//Every slot starts as NULL. Nothing is kept for a player past the playing
//ones, and such a player reads as NULL.
template <typename T>
class Slots {
  public:
	Slots() {
		for (u32 i = 0; i < PLAYING_PLAYERS; i++)
			values[i] = NULL;
	}
	void set(u32 player, T* value) {
		if (player < PLAYING_PLAYERS)
			values[player] = value;
	}
	T* get(u32 player) const {
		return player < PLAYING_PLAYERS ? values[player] : NULL;
	}

  private:
	T* values[PLAYING_PLAYERS];
};

} //playerslots
