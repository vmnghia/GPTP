//A debugging aid: names the image whose frame is past the end of its GRP.
//The game's GRP drawers read such a frame's line table from whatever follows
//the GRP's frame table, and crash on it (0x40B74C). The check in front of the
//image draw (0x497CE0) skips that image's draw and notes it, and nextFrame
//prints the notes. Cosmetic: it decides only whether an image is drawn.
#pragma once
#include <SCBW/api.h>

namespace framecheck {

//Answers 1 when the image may be drawn; 0 when its frame is past its GRP's
//last (it is noted, at most a few times per image and frame).
u32 imageDrawable(CImage* image);

//Prints what imageDrawable noted since the last call. Game thread.
void printPending();

} //framecheck

namespace hooks {

void injectFrameCheckHooks();

} //hooks
