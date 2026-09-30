//The save-file chunk of the extended selection. It follows the last vanilla
//write of a save (screenY) and is read after the last vanilla read.
#pragma once
#include <cstdio>

namespace selsave {

//Replaces the fwrite of screenY at 0x4C2E0A: writes it, then the chunk.
//Returns 0 on failure, which takes vanilla's failure path.
size_t __cdecl writeLastAndExtension(const void* data, size_t size, size_t count, FILE* file);
//Replaces the fread of screenY at 0x4D0225: reads it, then the chunk. An old
//save without the chunk loads with the vanilla 12; a damaged chunk returns 0,
//which ends the load with vanilla's error.
size_t __cdecl readLastAndExtension(void* data, size_t size, size_t count, FILE* file);

} //selsave
