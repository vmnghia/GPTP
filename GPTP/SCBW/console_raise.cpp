#include "console_raise.h"

namespace consoleraise {

const Span SPANS[4] = {
	{ 138, 408 },	//Zerg
	{ 143, 407 },	//Terran
	{ 143, 408 },	//Protoss
	{ 150, 405 },	//replays
};

void raiseArt(u8* art, s32 width, s32 height, Span span, s32 cutRow, s32 raise) {
	if (cutRow >= height || raise <= 0)
		return;
	for (s32 x = span.left; x < span.right && x < width; x++) {
		for (s32 y = 0; y < cutRow - raise; y++)
			art[y * width + x] = art[(y + raise) * width + x];
		for (s32 y = cutRow - raise; y < cutRow; y++)
			if (y >= 0)
				art[y * width + x] = art[cutRow * width + x];
	}
}

u32 raisePiece(Piece piece, Span span, s32 cutRow, s32 raise, Piece out[3]) {
	if (piece.top >= cutRow || piece.right <= span.left || piece.left >= span.right) {
		out[0] = piece;
		return 1;
	}
	u32 n = 0;
	if (piece.left < span.left) {
		out[n] = piece;
		out[n].right = span.left;
		n++;
	}
	out[n] = piece;
	out[n].left = piece.left > span.left ? piece.left : span.left;
	out[n].right = piece.right < span.right ? piece.right : span.right;
	out[n].top = piece.top - raise > 0 ? piece.top - raise : 0;
	if (piece.bottom <= cutRow)
		out[n].bottom = piece.bottom - raise;
	n++;
	if (piece.right > span.right) {
		out[n] = piece;
		out[n].left = span.right;
		n++;
	}
	return n;
}

} //consoleraise
