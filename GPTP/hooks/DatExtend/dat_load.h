#pragma once
#include <SCBW/scbwdata.h>

namespace hooks {

//The game's .dat loader, with the .dat extender's resize first (DatExtend/datext.h).
void LoadGameData(DatLoad* datTable, const char* filename);	//4D2E80

void injectDatLoadHooks();

} //hooks
