//Reads a file from the game's MPQs (the mod exe's first) through storm.dll's
//own file API, as the game does. Used by the loaders of the mod's data files.
#pragma once
#include "../../types.h"
#include <vector>

namespace mpqread {

enum Result {
	OK,
	MISSING,	//not in any MPQ
	FAILED,		//there, but could not be read
	NO_STORM	//storm.dll's file functions were not found
};

Result read(const char* name, std::vector<u8>& out);

} //mpqread
