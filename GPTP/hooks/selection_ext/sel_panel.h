//The selection panel's pages (stage 3): the StatData wireframes show one page
//of the client selection; PgUp/PgDn change it. Local UI only.
#pragma once
#include <SCBW/api.h>

namespace selpanel {

//Fills the wireframes with the current page (0x425960).
void fill(BinDlg* dialog);
//Whether a shown unit's hit points or type changed, or the page is past the
//selection's end (0x424660).
bool changed();
//A click on a wireframe: plain selects its unit, Shift removes it, Ctrl
//selects every unit of its type in the whole selection (0x458220).
void click(BinDlg* control);
//The in-game KEYDOWN proc (0x484350): Ctrl+PgUp/PgDn change the page.
void keyDown(const u8* event);
//StatData's interact table, indexed by control id - 1 (for 0x4584C0).
const u32* interactTable();
u32 interactTableBytes();

} //selpanel
