#pragma once
#include <SCBW/api.h>

//Storm and file helpers the .dat extender needs (DatExtend/). Ported from
//saintofidiocy/GPTP@DatExtender (0a4516b), only the two it calls.

//storm.dll ordinal 403: frees memory Storm (or FastFileRead) allocated.
u32 SMemFree(void* ptr, const char* filename, u32 line, int idk);

//0x004D2D10: reads a file from the game's archives into memory.
//- @p buffer: where to write it, or NULL for Storm to allocate it (free with SMemFree)
//- @p returnIfNotFound: true returns NULL for a missing or empty file, else a fatal error
//- @p scope: passed to SFileOpenFileEx (usually 0)
//- @p filesize: receives the size, may be NULL
void* FastFileRead(const char* filename, void* buffer, bool returnIfNotFound, u32 scope, u32* filesize);
