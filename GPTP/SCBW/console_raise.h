//Raising the selection box in the console art (24 px taller selection
//panel). Pure, so the host test covers it; resolution_hud.cpp applies it to
//the console image and to the decorative pieces (StatFluf).
//Design: docs/superpowers/specs/2026-10-04-selection-panel-pages-design.md
//section 6; documented for art replacements in docs/resolution.md
//("Console art reference").
#pragma once
#include "../types.h"

namespace consoleraise {

//Columns [left, right) of the vanilla 640x480 art.
struct Span {
	s32 left, right;
};

//The row the raise cuts at: inside the box's black area in every console.
const s32 CUT_ROW = 420;

//The raised columns of each console, indexed like consoleRace(): 0 Zerg,
//1 Terran, 2 Protoss, 3 replays (nconsole.pcx). Each lies inside StatData's
//vanilla columns [138, 408), so StatData repaints the rows the raise
//rewrites below the old box top.
extern const Span SPANS[4];

//In art (width x height, row-major), within span: every row above cutRow
//moves up raise rows (those pushed above row 0 are dropped), and the raise
//rows just above cutRow repeat cutRow.
void raiseArt(u8* art, s32 width, s32 height, Span span, s32 cutRow, s32 raise);

//A rect of the art, right and bottom exclusive.
struct Piece {
	s32 left, top, right, bottom;
};

//A decorative piece for the raised art: split at the span's edges, and the
//part inside grown raise rows upward (top clamped at 0) if it starts above
//cutRow. Writes up to 3 pieces to out; returns how many.
u32 raisePiece(Piece piece, Span span, s32 cutRow, s32 raise, Piece out[3]);

} //consoleraise
