//Terrain for the larger view.
//
//Brood War draws terrain into a ring buffer of 32x32 tiles (the "terrain
//cache", pointer at 0x00628454, 672 x 448 pixels) and copies the visible part
//to the screen. Every routine that touches the cache hardcodes its geometry:
//672 (0x2A0), 448, 0x49800, 21 x 14 tiles, 0x5400 per tile row. They are
//replaced here at their entry points with transcriptions of the originals,
//geometry read from the res_* globals and the cache moved to res_tileCache.
//The original's address and its constants are noted on each.

#include "resolution.h"
#include <hook_tools.h>

//BW globals used by the terrain code.
#define MAP_TILES_W        0x0057F1D4  //u16 map width in tiles
#define MAP_TILES_H        0x0057F1D6  //u16 map height in tiles
#define SCREEN_TILE_X      0x0057F1D0  //u16 screen left, in tiles
#define SCREEN_TILE_Y      0x0057F1D2  //u16 screen top, in tiles
#define MOVE_TO_X          0x0062848C  //screen left, in pixels
#define MOVE_TO_Y          0x006284A8  //screen top, in pixels
#define TILE_VR4           0x00628444  //minitile pixel data
#define TILE_VX4           0x00628458  //megatile -> minitile references
#define CACHED_TILES       0x00628494  //u16 per map tile: what the cache holds
#define BLIT_WIDTH         0x0050CEF0  //row width for the cache-to-screen copy

const u32 Func_TileTransition = 0x00413B30;  //called with ebx = 0x00414440

//Scratch for the overlay blitter (the original kept these at 0x0050CEC5..D9).
static u8* ovlCacheEnd;
static s32 ovlWidth;
static s32 ovlRows;
static s32 ovlRowSkip;

//-------- Cache -> screen --------//

//0x0040C200. eax = cache offset, esi = source, edi = destination,
//edx = rows, [BLIT_WIDTH] = bytes per row. Wraps the source at the cache end.
void __declspec(naked) copyCacheRows_40C200() {
  __asm {
  ROW:
    mov ecx, dword ptr ds:[BLIT_WIDTH]
    mov ebp, ecx
    mov ebx, eax
    add ebx, ecx
    cmp ebx, res_cacheSize
    jl NOWRAP
    cmp eax, res_cacheSize
    jge WRAPPED
    sub ebx, res_cacheSize
    sub ecx, ebx
    shr ecx, 2
    rep movsd
    mov ecx, ebx
  WRAPPED:
    sub esi, res_cacheSize
    sub eax, res_cacheSize
  NOWRAP:
    shr ecx, 2
    rep movsd
    add esi, res_cacheW
    add eax, res_cacheW
    sub esi, ebp
    add edi, res_w
    sub edi, ebp
    dec edx
    jne ROW
    retn
  }
}

//0x0040C253 blitMapTiles: the whole view, from the cache to the screen.
void __declspec(naked) blitMapTiles_40C253() {
  __asm {
    push eax
    push ebx
    push ecx
    push edx
    push esi
    push edi
    push ebp
    mov eax, res_w
    mov dword ptr ds:[BLIT_WIDTH], eax
    mov edi, res_screenBmp.data
    mov eax, dword ptr ds:[MOVE_TO_Y]
    imul eax, res_cacheW
    add eax, dword ptr ds:[MOVE_TO_X]
  WRAPLOOP:
    cmp eax, res_cacheSize
    jl INRANGE
    sub eax, res_cacheSize
    jmp WRAPLOOP
  INRANGE:
    mov edx, res_viewH
    mov esi, eax
    add esi, res_tileCache
    call copyCacheRows_40C200
    pop ebp
    pop edi
    pop esi
    pop edx
    pop ecx
    pop ebx
    pop eax
    retn
  }
}

//0x0040C2BD: one 16-row strip. ecx = screen y, edx = screen x,
//[esp+4] = width, [esp+8] = cache offset. RET 8.
void __declspec(naked) blitCacheStrip_40C2BD() {
  __asm {
    push ebp
    mov ebp, esp
    push ebx
    push ecx
    push edx
    push edi
    push esi
    xor eax, eax
    mov edi, res_screenBmp.data
    mov ax, word ptr ds:[0x006CEFF0]  //gameScreenBuffer width
    add edi, edx
    mul ecx
    add edi, eax
    mov eax, [ebp+0x0C]
    mov edx, 0x10
    mov esi, eax
    mov ebx, [ebp+8]
    add esi, res_tileCache
    mov dword ptr ds:[BLIT_WIDTH], ebx
    call copyCacheRows_40C200
    pop esi
    pop edi
    pop edx
    pop ecx
    pop ebx
    pop ebp
    retn 8
  }
}

//0x004BCDC0: copies the dirty cells of the view from the cache to the screen.
//(0x2A0, 0x49800, 40 cells per row at 0x006CEFF8, 0x2780 = 672*16 - 640, 400.)
void __declspec(naked) blitDirtyCells_4BCDC0() {
  __asm {
    push ebp
    mov ebp, esp
    sub esp, 0x0C
    mov eax, dword ptr ds:[MOVE_TO_Y]
    mov edx, dword ptr ds:[MOVE_TO_X]
    imul eax, res_cacheW
    push ebx
    push esi
    add eax, edx
    xor edx, edx
    mov esi, res_cacheSize
    div esi
    mov ecx, res_cells
    push edi
    mov [ebp-4], ecx
    mov dword ptr [ebp-0x0C], 0
    mov edi, edx
  ROWSTART:
    xor ebx, ebx
  CELL:
    cmp edi, res_cacheSize
    jl C1
    sub edi, res_cacheSize
  C1:
    cmp byte ptr [ecx], 1
    jne NEXTCELL
    lea eax, [ebx+1]
    cmp eax, res_cellCols
    mov esi, 1
    mov [ebp-8], esi
    jge RUNDONE
  RUN:
    cmp byte ptr [ecx], 0
    je RUNEND
    inc ecx
    inc esi
    inc eax
    cmp eax, res_cellCols
    jl RUN
  RUNEND:
    mov [ebp-8], esi
    mov [ebp-4], ecx
  RUNDONE:
    mov ecx, [ebp-0x0C]
    push edi
    shl esi, 4
    mov edx, ebx
    push esi
    shl edx, 4
    call blitCacheStrip_40C2BD
    mov eax, [ebp-8]
    mov ecx, [ebp-4]
    lea edi, [edi+esi-0x10]
    cmp edi, res_cacheSize
    lea ebx, [ebx+eax-1]
    jl NEXTCELL
    sub edi, res_cacheSize
  NEXTCELL:
    inc ebx
    add edi, 0x10
    inc ecx
    cmp ebx, res_cellCols
    mov [ebp-4], ecx
    jl CELL
    add edi, res_cacheSkip16
    cmp edi, res_cacheSize
    jl C2
    sub edi, res_cacheSize
  C2:
    mov eax, [ebp-0x0C]
    add eax, 0x10
    cmp eax, res_viewH
    mov [ebp-0x0C], eax
    jl ROWSTART
    pop edi
    pop esi
    pop ebx
    mov esp, ebp
    pop ebp
    retn
  }
}

//-------- Drawing into the cache --------//

//0x0040C3B0: one 8x8 minitile. ecx = minitile (bit 0 = mirrored),
//edx = cache offset. (0x2A0, 0x49800. The vertically flipped variants in the
//original are unreachable and left out.)
void __declspec(naked) drawMinitile_40C3B0() {
  __asm {
    push ebp
    push ebx
    push esi
    push edi
    mov esi, ecx
    mov ebp, res_tileCache
    and esi, 0xFFFE
    mov eax, dword ptr ds:[TILE_VR4]
    shl esi, 5
    add esi, eax
    test ecx, 1
    je PLAIN

    mov eax, 8
  MIRRORED:
    cmp edx, res_cacheSize
    jl M1
    sub edx, res_cacheSize
  M1:
    lea edi, [ebp+edx]
    mov ch, [esi+4]
    mov bh, [esi]
    mov cl, [esi+5]
    mov bl, [esi+1]
    shl ecx, 0x10
    add edx, res_cacheW
    shl ebx, 0x10
    mov ch, [esi+6]
    mov bh, [esi+2]
    mov cl, [esi+7]
    mov bl, [esi+3]
    mov [edi], ecx
    add esi, 8
    mov [edi+4], ebx
    dec eax
    jne MIRRORED
    jmp DONE

  PLAIN:
    mov eax, 8
  P0:
    cmp edx, res_cacheSize
    jl P1
    sub edx, res_cacheSize
  P1:
    lea edi, [ebp+edx]
    mov ecx, [esi]
    mov ebx, [esi+4]
    mov [edi], ecx
    mov [edi+4], ebx
    add esi, 8
    add edx, res_cacheW
    dec eax
    jne P0

  DONE:
    pop edi
    pop esi
    pop ebx
    pop ebp
    retn
  }
}

//0x0040AAE0: a tile overlay (run-length encoded) into the cache.
//ecx = overlay entry {u8 x, u8 y, u8 w, u8 h, u8* data}, edx = cache offset.
//(0x2A0, 0x49800, [0x00628454].)
void __declspec(naked) drawTileOverlay_40AAE0() {
  __asm {
    push ebp
    push esi
    push edi
    push ebx
    mov esi, res_tileCache
    mov edi, res_tileCache
    add esi, res_cacheSize
    xor eax, eax
    mov ovlCacheEnd, esi
    mov ebx, res_cacheW
    mov al, [ecx+1]
    add edi, edx
    mul ebx
    mov dl, [ecx]
    add edi, eax
    mov eax, ovlCacheEnd
    add edi, edx
    cmp edi, eax
    jl START
    sub edi, res_cacheSize
  START:
    xor eax, eax
    xor edx, edx
    mov al, [ecx+2]
    mov dl, [ecx+3]
    mov ovlWidth, eax
    mov ovlRows, edx
    sub ebx, eax
    mov esi, [ecx+4]
    mov ovlRowSkip, ebx
    xor edx, edx
    mov ebp, esi
  ROW:
    xor ecx, ecx
    mov ebx, ovlWidth
    mov cx, [esi]
    add esi, 2
    add ecx, ebp
  RUN:
    mov dl, [ecx]
    inc ecx
    test dl, dl
    js SKIP
    test dl, 0x40
    jne REPEAT
    sub ebx, edx
  COPY1:
    mov al, [ecx]
    inc ecx
    mov [edi], al
    dec edx
    lea edi, [edi+1]
    jne COPY1
    test ebx, ebx
    jg RUN
    jmp ROWDONE
  REPEAT:                    //one byte, repeated
    and dl, 0xBF
    sub ebx, edx
    mov al, [ecx]
    inc ecx
  REPEATLOOP:
    mov [edi], al
    dec edx
    lea edi, [edi+1]
    jne REPEATLOOP
    test ebx, ebx
    jg RUN
    jmp ROWDONE
  SKIP:
    and dl, 0x7F
    sub ebx, edx
    add edi, edx
    test ebx, ebx
    jg RUN
  ROWDONE:
    mov ecx, ovlRowSkip
    mov eax, ovlCacheEnd
    add edi, ecx
    mov ebx, ovlRows
    cmp edi, eax
    jl NEXTROW
    sub edi, res_cacheSize
  NEXTROW:
    dec ebx
    mov ovlRows, ebx
    jne ROW
    pop ebx
    pop edi
    pop esi
    pop ebp
    retn
  }
}

//0x0049B9F0: one megatile (4x4 minitiles) plus its overlay.
//bx = tile (bit 15 = has overlay), edi = cache offset, [esp+4] = tile x,
//[esp+8] = tile y. RET 8. (Row steps 0x1500/0x2A00/0x3F00 = 672 * 8/16/24.)
void __declspec(naked) drawMegatile_49B9F0() {
  __asm {
    push ebp
    mov ebp, esp
    sub esp, 8               //[ebp-4] row offset, [ebp-8] rows left
    push esi
    mov eax, dword ptr ds:[TILE_VX4]
    mov esi, ebx
    and esi, 0x7FFF
    shl esi, 5
    add esi, eax
    mov dword ptr [ebp-4], 0
    mov dword ptr [ebp-8], 4
  MINIROW:
    mov edx, [ebp-4]
    add edx, edi
    movzx ecx, word ptr [esi]
    call drawMinitile_40C3B0
    mov edx, [ebp-4]
    lea edx, [edi+edx+8]
    movzx ecx, word ptr [esi+2]
    call drawMinitile_40C3B0
    mov edx, [ebp-4]
    lea edx, [edi+edx+0x10]
    movzx ecx, word ptr [esi+4]
    call drawMinitile_40C3B0
    mov edx, [ebp-4]
    lea edx, [edi+edx+0x18]
    movzx ecx, word ptr [esi+6]
    call drawMinitile_40C3B0
    add esi, 8
    mov eax, res_cacheW
    shl eax, 3
    add [ebp-4], eax
    dec dword ptr [ebp-8]
    jne MINIROW
    pop esi

    test bh, bh
    jns DONE
    mov eax, dword ptr ds:[0x006D0F08]
    imul eax, [ebp+0x0C]
    mov edx, dword ptr ds:[0x006D0E80]
    mov ecx, [ebp+8]
    add eax, edx
    mov al, [eax+ecx]
    test al, al
    je DONE
    movzx edx, al
    mov eax, dword ptr ds:[0x006D0C64]
    lea ecx, [eax+edx*8-2]
    mov edx, edi
    call drawTileOverlay_40AAE0
  DONE:
    mov esp, ebp
    pop ebp
    retn 8
  }
}

//0x0049B8D0: clips a tile rect to the map and the cache, converting it to
//cache-relative tiles. eax = &x, ecx = &y, [esp+4] = &w, [esp+8] = &h.
//Returns eax = 1 if anything is left. RET 8. (21, 14.)
void __declspec(naked) clipTileRect_49B8D0() {
  __asm {
    push ebp
    mov ebp, esp
    push ecx
    mov edx, dword ptr ds:[MOVE_TO_Y]
    push ebx
    mov ebx, [ebp+8]
    shr edx, 5
    push esi
    push edi
    mov edi, dword ptr ds:[MOVE_TO_X]
    mov [ebp-4], edx
    mov edx, [eax]
    shr edi, 5
    test edx, edx
    jge X_OK
    mov dword ptr [eax], 0
    jmp X_DONE
  X_OK:
    mov ebx, [ebx]
    movzx esi, word ptr ds:[MAP_TILES_W]
    add ebx, edx
    cmp ebx, esi
    mov ebx, [ebp+8]
    jl X_DONE
    sub esi, edx
    mov [ebx], esi
  X_DONE:
    mov esi, [ecx]
    test esi, esi
    jge Y_OK
    mov dword ptr [ecx], 0
    jmp Y_DONE
  Y_OK:
    mov ebx, [ebp+0x0C]
    mov ebx, [ebx]
    movzx edx, word ptr ds:[MAP_TILES_H]
    add ebx, esi
    cmp ebx, edx
    mov ebx, [ebp+8]
    jl Y_DONE
    sub edx, esi
    mov esi, [ebp+0x0C]
    mov [esi], edx
  Y_DONE:
    sub [eax], edi
    mov edx, [ebp-4]
    sub [ecx], edx
    mov edx, [eax]
    test edx, edx
    jge CX_OK
    add [ebx], edx
    mov dword ptr [eax], 0
  CX_OK:
    mov edx, [eax]
    mov esi, [ebx]
    add esi, edx
    cmp esi, res_cacheCols
    jl CW_OK
    mov esi, res_cacheCols
    sub esi, edx
    mov [ebx], esi
  CW_OK:
    mov edx, [ecx]
    test edx, edx
    mov esi, [ebp+0x0C]
    jge CY_OK
    add [esi], edx
    mov dword ptr [ecx], 0
  CY_OK:
    mov edx, [ecx]
    mov esi, [esi]
    add esi, edx
    cmp esi, res_cacheRows
    jl CH_OK
    mov esi, res_cacheRows
    sub esi, edx
    mov edx, [ebp+0x0C]
    mov [edx], esi
  CH_OK:
    cmp dword ptr [ebx], 0
    jle NOTHING
    mov edx, [ebp+0x0C]
    cmp dword ptr [edx], 0
    jle NOTHING
    mov edx, [eax]
    cmp edx, res_cacheCols
    jge NOTHING
    mov esi, res_cacheRows
    cmp [ecx], esi
    jge NOTHING
    add edx, edi
    mov [eax], edx
    mov edi, [ecx]
    mov edx, [ebp-4]
    add edi, edx
    mov [ecx], edi
    movzx edx, word ptr ds:[MAP_TILES_W]
    cmp [eax], edx
    mov ecx, edi
    jge NOTHING
    movzx eax, word ptr ds:[MAP_TILES_H]
    cmp ecx, eax
    jge NOTHING
    pop edi
    pop esi
    mov eax, 1
    pop ebx
    mov esp, ebp
    pop ebp
    retn 8
  NOTHING:
    pop edi
    pop esi
    xor eax, eax
    pop ebx
    mov esp, ebp
    pop ebp
    retn 8
  }
}

//Cache offset of the tile at eax = tile x, ecx = tile y: ((y * cacheW + x) * 32)
//mod cacheSize, in eax. Clobbers edx. (The original unrolls this as a binary
//long division by 0x49800.)
void __declspec(naked) tileCacheOffset() {
  __asm {
    imul ecx, res_cacheW
    add eax, ecx
    shl eax, 5
    xor edx, edx
    div res_cacheSize
    mov eax, edx
    retn
  }
}

//0x0049BC20: redraws a rect of tiles. stdcall (x, y, w, h), in map tiles.
//(0x2A0, 0x49800 division, 0x5400 per tile row.)
void __declspec(naked) drawTileRect_49BC20() {
  __asm {
    push ebp
    mov ebp, esp
    sub esp, 8
    lea eax, [ebp+0x14]
    push eax
    lea ecx, [ebp+0x10]
    push ecx
    lea ecx, [ebp+0x0C]
    lea eax, [ebp+8]
    call clipTileRect_49B8D0
    test eax, eax
    je EXIT
    movzx edx, word ptr ds:[MAP_TILES_W]
    mov eax, [ebp+0x0C]
    mov ecx, [ebp+8]
    imul edx, eax
    add edx, ecx
    push esi
    mov esi, dword ptr ds:[CACHED_TILES]
    lea esi, [esi+edx*2]
    push edx
    mov ecx, eax
    mov eax, [ebp+8]
    call tileCacheOffset
    pop edx
    mov [ebp-4], eax
    mov eax, [ebp+0x14]
    test eax, eax
    je POPESI
    push ebx
    mov [ebp-8], eax
    push edi
  ROW:
    mov eax, [ebp+0x10]
    test eax, eax
    mov edi, [ebp-4]
    je ROWDONE
    mov [ebp+0x14], eax
  TILE:
    mov eax, [ebp+0x0C]
    mov ecx, [ebp+8]
    mov bx, word ptr [esi]
    push eax
    push ecx
    call drawMegatile_49B9F0
    add edi, 0x20
    mov eax, edi
    sub eax, res_cacheSize
    jl T1
    mov edi, eax
  T1:
    mov ecx, [ebp+8]
    mov eax, [ebp+0x14]
    add esi, 2
    inc ecx
    dec eax
    mov [ebp+8], ecx
    mov [ebp+0x14], eax
    jne TILE
    mov eax, [ebp+0x10]
  ROWDONE:
    mov ecx, [ebp-4]
    add ecx, res_cacheRow32
    mov [ebp-4], ecx
    add ecx, res_cacheSizeNeg
    js R1
    mov [ebp-4], ecx
  R1:
    movzx edx, word ptr ds:[MAP_TILES_W]
    mov ecx, [ebp+0x0C]
    sub edx, eax
    lea esi, [esi+edx*2]
    mov edx, [ebp+8]
    sub edx, eax
    mov eax, [ebp-8]
    inc ecx
    dec eax
    mov [ebp+8], edx
    mov [ebp+0x0C], ecx
    mov [ebp-8], eax
    jne ROW
    pop edi
    pop ebx
  POPESI:
    pop esi
  EXIT:
    mov esp, ebp
    pop ebp
    retn 0x10
  }
}

//0x0049BD40: redraws one column of the cache. eax = tile y (first row),
//[esp+4] = pointer into CACHED_TILES, [esp+8] = tile x. RET 8.
//(14 rows, 0x2A0, 0x44400 = last tile row, 0xFFFB6800 = -0x49800, 0x5400.)
void __declspec(naked) drawTileColumn_49BD40() {
  __asm {
    push ebp
    mov ebp, esp
    push ecx
    push ebx
    mov ebx, [ebp+0x0C]
    push esi
    mov esi, eax
    movzx eax, word ptr ds:[MAP_TILES_W]
    cmp ebx, eax
    jae EXIT
    movzx ecx, word ptr ds:[MAP_TILES_H]
    sub ecx, esi
    inc ecx
    cmp ecx, res_cacheRows
    jbe COUNT_OK
    mov ecx, res_cacheRows
  COUNT_OK:
    push ecx
    mov eax, ebx
    mov ecx, esi
    call tileCacheOffset
    pop ecx
    test ecx, ecx
    push edi
    mov edi, eax
    je POPEDI
    mov [ebp-4], ecx
    jmp TILE
  NEXT:
    mov ebx, [ebp+0x0C]
  TILE:
    movzx ecx, word ptr ds:[MAP_TILES_H]
    cmp esi, ecx
    push esi
    push ebx
    jb INMAP
    xor ebx, ebx
    jmp DRAW
  INMAP:
    mov edx, [ebp+8]
    mov bx, word ptr [edx]
  DRAW:
    call drawMegatile_49B9F0
    movzx eax, word ptr ds:[MAP_TILES_W]
    mov ecx, [ebp+8]
    lea edx, [ecx+eax*2]
    xor eax, eax
    inc esi
    cmp edi, res_cacheLastRow
    setl al
    mov [ebp+8], edx
    dec eax
    and eax, res_cacheSizeNeg
    add eax, res_cacheRow32
    add edi, eax
    dec dword ptr [ebp-4]
    jne NEXT
  POPEDI:
    pop edi
  EXIT:
    pop esi
    pop ebx
    mov esp, ebp
    pop ebp
    retn 8
  }
}

//0x0049BE20: redraws one row of the cache. eax = tile x (first column),
//[esp+4] = pointer into CACHED_TILES, [esp+8] = tile y. RET 8. (21, 0x2A0, 0x49800.)
void __declspec(naked) drawTileRow_49BE20() {
  __asm {
    push ebp
    mov ebp, esp
    push ecx
    mov ecx, [ebp+0x0C]
    push ebx
    push esi
    push edi
    mov esi, eax
    push ecx
    call tileCacheOffset
    pop ecx
    mov edi, eax
    movzx eax, word ptr ds:[MAP_TILES_H]
    cmp ecx, eax
    jb INMAP

    //Below the map: blank tiles.
    mov eax, res_cacheCols
    mov [ebp+8], eax
    jmp BLANK
  BLANKNEXT:
    mov ecx, [ebp+0x0C]
  BLANK:
    push ecx
    push esi
    xor ebx, ebx
    call drawMegatile_49B9F0
    add edi, 0x20
    mov eax, edi
    sub eax, res_cacheSize
    jl B1
    mov edi, eax
  B1:
    mov eax, [ebp+8]
    inc esi
    dec eax
    mov [ebp+8], eax
    jne BLANKNEXT
    jmp EXIT

  INMAP:
    movzx eax, word ptr ds:[MAP_TILES_W]
    sub eax, esi
    cmp eax, res_cacheCols
    jbe COUNT_OK
    mov eax, res_cacheCols
    mov [ebp-4], eax
    jmp TILE
  COUNT_OK:
    test eax, eax
    je EXIT
    mov [ebp-4], eax
    jmp TILE
  NEXT:
    mov ecx, [ebp+0x0C]
  TILE:
    push ecx
    mov ecx, [ebp+8]
    mov bx, word ptr [ecx]
    push esi
    call drawMegatile_49B9F0
    add edi, 0x20
    cmp edi, res_cacheSize
    jb T1
    sub edi, res_cacheSize
  T1:
    mov edx, [ebp+8]
    mov eax, [ebp-4]
    add edx, 2
    inc esi
    dec eax
    mov [ebp+8], edx
    mov [ebp-4], eax
    jne NEXT
  EXIT:
    pop edi
    pop esi
    pop ebx
    mov esp, ebp
    pop ebp
    retn 8
  }
}

//0x0049C620: redraws the tiles of one cache row whose terrain changed (creep,
//doodads). eax = pointer into CACHED_TILES, [esp+4] = pointer into the map's
//tiles (0x005993C4), [esp+8] = pointer into activeTileArray, [esp+0x0C] = tile y,
//[esp+0x10] = cache offset. RET 0x10. (21, 0x497E0 = 0x49800 - 32.)
void __declspec(naked) updateTileRow_49C620() {
  __asm {
    push ebp
    mov ebp, esp
    sub esp, 8
    mov ecx, [ebp+0x10]
    push esi
    mov esi, eax
    movzx eax, word ptr ds:[MAP_TILES_H]
    cmp ecx, eax
    jge EXIT
    movzx ecx, word ptr ds:[SCREEN_TILE_X]
    movzx eax, word ptr ds:[MAP_TILES_W]
    push edi
    sub eax, ecx
    xor edi, edi
    cmp eax, res_cacheCols
    mov [ebp-4], ecx
    jbe COUNT_OK
    mov eax, res_cacheCols
    jmp COUNT_SET
  COUNT_OK:
    test eax, eax
    je POPEDI
  COUNT_SET:
    mov [ebp-8], eax
    push ebx
  TILE:
    mov eax, dword ptr ds:[0x006D0F14]  //replay
    test eax, eax
    je NOT_REPLAY
    mov edx, [ebp+0x0C]
    mov eax, [edx]
    mov dl, byte ptr ds:[0x006D0F18]
    not eax
    test al, dl
    je SKIP
    jmp CHECK
  NOT_REPLAY:
    mov eax, [ebp+0x0C]
    mov edx, dword ptr ds:[0x0057F0B0]
    test [eax], edx
    jne SKIP
  CHECK:
    mov edx, [ebp+8]
    movzx eax, word ptr [edx]
    mov ebx, eax
    shr ebx, 4
    and ebx, 0x7FF
    imul ebx, ebx, 0x1A
    and eax, 0x0F
    add ebx, eax
    mov eax, dword ptr ds:[0x006D5EC8]
    mov ax, word ptr [eax+ebx*2+0x14]
    xor ebx, ebx
    mov bx, word ptr [esi]
    and ebx, 0xFFFF7FFF
    cmp ax, bx
    je SKIP
    mov word ptr [esi], ax
    movzx edx, word ptr [edx]
    and edx, 0x7FF0
    cmp edx, 0x10
    mov edx, [ebp+0x0C]
    mov eax, [edx]
    jne CLEARFLAG
    or eax, 0x40000000
    jmp SETFLAG
  CLEARFLAG:
    and eax, 0xBFFFFFFF
  SETFLAG:
    mov [edx], eax
    mov eax, [ebp+0x10]
    push eax
    push ecx
    xor edi, edi
    mov ebx, 0x00414440
    call Func_TileTransition
    mov ecx, [ebp+0x10]
    mov edx, [ebp-4]
    mov edi, [ebp+0x14]
    mov bx, word ptr [esi]
    push ecx
    push edx
    call drawMegatile_49B9F0
    mov ecx, [ebp-4]
    mov edi, 1
  SKIP:
    mov eax, [ebp+0x0C]
    mov edx, [ebp+8]
    add edx, 2
    add eax, 4
    mov [ebp+8], edx
    mov [ebp+0x0C], eax
    mov eax, [ebp+0x14]
    xor edx, edx
    inc ecx
    add esi, 2
    push ecx
    mov ecx, res_cacheSize
    sub ecx, 0x20
    cmp eax, ecx
    pop ecx
    setl dl
    mov [ebp-4], ecx
    dec edx
    and edx, res_cacheSizeNeg
    add edx, 0x20
    add eax, edx
    mov [ebp+0x14], eax
    dec dword ptr [ebp-8]
    jne TILE
    test edi, edi
    pop ebx
    je POPEDI
    mov byte ptr ds:[0x0059CB58], 1
  POPEDI:
    pop edi
  EXIT:
    pop esi
    mov esp, ebp
    pop ebp
    retn 0x10
  }
}

//0x0049C780 drawMapTiles: per frame, redraws changed tiles in every cache row.
//(0x2A0, 0x49800 division, 14 rows, 0x44400, 0xFFFB6800, 0x5400.)
void __declspec(naked) drawMapTiles_49C780() {
  __asm {
    push ebp
    mov ebp, esp
    sub esp, 0x0C
    movzx eax, word ptr ds:[SCREEN_TILE_Y]
    movzx ecx, word ptr ds:[MAP_TILES_W]
    movzx edx, word ptr ds:[SCREEN_TILE_X]
    imul ecx, eax
    push ebx
    push esi
    mov esi, dword ptr ds:[CACHED_TILES]
    mov ebx, eax
    add ecx, edx
    lea esi, [esi+ecx*2]
    mov [ebp-4], esi
    mov esi, dword ptr ds:[0x005993C4]
    lea esi, [esi+ecx*2]
    mov [ebp-8], esi
    mov esi, dword ptr ds:[0x006D1260]  //activeTileArray
    push edi
    lea edi, [esi+ecx*4]
    push ecx
    mov eax, edx
    mov ecx, ebx
    call tileCacheOffset
    pop ecx
    mov esi, eax
    mov eax, res_cacheRows
    mov [ebp-0x0C], eax
  ROW:
    mov edx, [ebp-8]
    mov eax, [ebp-4]
    push esi
    push ebx
    push edi
    push edx
    call updateTileRow_49C620
    mov edx, [ebp-4]
    xor eax, eax
    cmp esi, res_cacheLastRow
    setl al
    dec eax
    and eax, res_cacheSizeNeg
    add eax, res_cacheRow32
    add esi, eax
    movzx eax, word ptr ds:[MAP_TILES_W]
    lea ecx, [eax+eax]
    add edx, ecx
    mov [ebp-4], edx
    mov edx, [ebp-8]
    lea edi, [edi+eax*4]
    mov eax, [ebp-0x0C]
    add edx, ecx
    inc ebx
    dec eax
    mov [ebp-8], edx
    mov [ebp-0x0C], eax
    jne ROW
    pop edi
    pop esi
    pop ebx
    mov esp, ebp
    pop ebp
    retn
  }
}

//-------- Tile counts inside other routines --------//

//0x0049BF46: mov ebx, 0x0E (5 bytes). Rows when refilling the whole cache.
void __declspec(naked) refillRows_49BF46() {
  __asm {
    mov ebx, res_cacheRows
    retn
  }
}

//0x0049C155: movzx edx, word ptr [0x57F1D4]; add eax, 0x15 (10 bytes).
//Scrolling right: the column that just came into the cache.
void __declspec(naked) scrollRightColumn_49C155() {
  __asm {
    movzx edx, word ptr ds:[MAP_TILES_W]
    add eax, res_cacheCols
    add dword ptr [esp], 5
    retn
  }
}

//0x0049C315: movzx edx, word ptr [0x57F1D4]; add eax, 0x0E (10 bytes).
//Scrolling down: the row that just came into the cache.
void __declspec(naked) scrollDownRow_49C315() {
  __asm {
    movzx edx, word ptr ds:[MAP_TILES_W]
    add eax, res_cacheRows
    add dword ptr [esp], 5
    retn
  }
}

//0x0049C543: sub ebx, eax; cmp ebx, 0x15 (5 bytes). Whether a changed tile is
//in the cache, horizontally. Flags feed the jae that follows.
void __declspec(naked) inCacheX_49C543() {
  __asm {
    sub ebx, eax
    cmp ebx, res_cacheCols
    retn
  }
}

//0x0049C554: sub ebx, eax; cmp ebx, 0x0E (5 bytes). Same, vertically.
void __declspec(naked) inCacheY_49C554() {
  __asm {
    sub ebx, eax
    cmp ebx, res_cacheRows
    retn
  }
}

//0x0049BBCD: add eax, 8; movzx ecx, dx (6 bytes). At map load, the largest
//screen top: (mapH - 12) * 32 + 8, i.e. mapH * 32 - 376. That stops the
//map's bottom edge at screen y = 376, just above the console's selection box,
//with the 104 rows below it behind the console. The console sits at the bottom
//of the larger screen the same way, so the limit becomes mapH * 32 - (res_h -
//104). si holds the map height in tiles.
void __declspec(naked) maxScrollY_49BBCD() {
  __asm {
    movzx eax, si
    shl eax, 5
    sub eax, res_h
    add eax, 104
    movzx ecx, dx
    inc dword ptr [esp]
    retn
  }
}

//0x0049BBE6: sub ecx, 0x14; shl ecx, 5 (6 bytes). The largest screen left:
//(mapW - 20) * 32 becomes mapW * 32 - res_w.
void __declspec(naked) maxScrollX_49BBE6() {
  __asm {
    shl ecx, 5
    sub ecx, res_w
    inc dword ptr [esp]
    retn
  }
}

//0x004A3F6C: mov eax, 0x0D (5 bytes). Height of the minimap's view box, in
//tiles, before the minimap scale is applied.
//0x004A3F7E: mov eax, 0x14 (5 bytes). Its width. The Expander replaced the
//scaled results instead, which ignores the scale on maps that aren't 128
//tiles; changing the tile counts keeps BW's scaling.
static s32 minimapBoxTilesH;
static s32 minimapBoxTilesW;

void __declspec(naked) minimapBoxH_4A3F6C() {
  __asm {
    mov eax, minimapBoxTilesH
    retn
  }
}

void __declspec(naked) minimapBoxW_4A3F7E() {
  __asm {
    mov eax, minimapBoxTilesW
    retn
  }
}

//-------- Sprite blitters --------//

//The 16 GRP blitters (0x0040ABC4 ... 0x0040C0B4) start with
//mov esi, [0x006CF4A8]; mov edi, [esi+4] (9 bytes): the draw target bitmap and
//its pixels. When the target is gameScreenBuffer, draw into the enlarged
//buffer, which also covers frames drawn before the buffer is swapped in.
void __declspec(naked) blitterTarget() {
  __asm {
    mov esi, dword ptr ds:[0x006CF4A8]
    mov edi, [esi+4]
    cmp edi, dword ptr ds:[0x006CEFF4]
    jne DONE
    lea esi, res_screenBmp
    mov edi, res_screenBmp.data
  DONE:
    add dword ptr [esp], 4
    retn
  }
}

//-------- Space Platform starfield --------//

//On Space Platform maps, parallax stars are drawn onto the empty (index 0)
//pixels of the view. There are 5 layers, each scrolled by its own offset,
//of stars placed on a 648 x 488 field that wraps. The originals clip to
//640 x 400, write with a 640-byte pitch and read BW's 40-column grid; the
//replacement repeats the field across the whole view, keeping the density.

namespace {

struct StarImage {
  u16 width;
  u16 height;
  u8 pixels[1];
};

struct Star {
  u16 x;
  u16 y;
  const StarImage* image;
};

const Star* const* const starList = (const Star* const*)0x00658AA8;  //all layers, in order
const s32 STAR_FIELD_W = 0x288;
const s32 STAR_FIELD_H = 0x1E8;

s32 wrapStar(s32 pos, s32 field) {
  if (pos > field)
    pos -= field;
  else if (pos < 0)
    pos += field;
  return pos - 8;
}

//0x0047EA60: copies the star's pixels onto the screen's empty ones.
void blitStar(const StarImage* image, s32 x, s32 y, bool onlyDirty) {
  const ResBitmap* screen = (const ResBitmap*)0x006CEFF0;
  const s32 left = x < 0 ? 0 : x;
  const s32 top = y < 0 ? 0 : y;
  const s32 right = x + image->width < res_w ? x + image->width : res_w;
  const s32 bottom = y + image->height < res_viewH ? y + image->height : res_viewH;
  if (left >= right || top >= bottom)
    return;
  if (onlyDirty && !resolution::isRectDirty(left, top, right, bottom))
    return;

  for (s32 row = top; row < bottom; ++row) {
    const u8* src = image->pixels + (row - y) * image->width + (left - x);
    u8* dst = screen->data + row * screen->width + left;
    for (s32 col = left; col < right; ++col, ++src, ++dst) {
      if (*dst == 0)
        *dst = *src;
    }
  }
}

//The layer loop of both originals: layer j uses the scroll offsets at
//0x0062846C - 4j (y) and 0x00628484 - 4j (x), 24.8 fixed point, and takes the
//next [0x00658AD4 - 4j] stars from the list.
void drawStars(bool onlyDirty) {
  const Star* star = *starList;
  if (star == NULL)
    return;

  for (s32 j = 0; j < 5; ++j) {
    const s32 scrollY = *(const s32*)(0x0062846C - 4 * j) >> 8;
    const s32 scrollX = *(const s32*)(0x00628484 - 4 * j) >> 8;
    const s32 count = *(const s32*)(0x00658AD4 - 4 * j);
    for (s32 i = 0; i < count; ++i, ++star) {
      const s32 firstX = wrapStar(star->x + scrollX, STAR_FIELD_W);
      const s32 firstY = wrapStar(star->y + scrollY, STAR_FIELD_H);
      for (s32 y = firstY; y < res_viewH; y += STAR_FIELD_H) {
        for (s32 x = firstX; x < res_w; x += STAR_FIELD_W)
          blitStar(star->image, x, y, onlyDirty);
      }
    }
  }
}

} //unnamed namespace

//0x0047EBF0: stars over the dirty cells. No arguments.
void __cdecl drawStarsDirty_47EBF0() {
  drawStars(true);
}

//0x0047EE20: stars over the whole view. No arguments.
void __cdecl drawStarsAll_47EE20() {
  drawStars(false);
}

const u32 blitterSites[16] = {
  0x0040ABC4, 0x0040AD0A, 0x0040AE69, 0x0040AFDB, 0x0040B15B, 0x0040B2D9,
  0x0040B447, 0x0040B59C, 0x0040B6F6, 0x0040B82A, 0x0040B9AF, 0x0040BB34,
  0x0040BCA9, 0x0040BE0A, 0x0040BF66, 0x0040C0B4,
};

namespace resolution {

void injectTerrainHooks() {
  minimapBoxTilesH = (res_viewH + 31) / 32;
  minimapBoxTilesW = res_w / 32;

  //Cache to screen
  jmpPatch(copyCacheRows_40C200,    0x0040C200);
  jmpPatch(blitMapTiles_40C253,     0x0040C253);
  jmpPatch(blitCacheStrip_40C2BD,   0x0040C2BD);
  jmpPatch(blitDirtyCells_4BCDC0,   0x004BCDC0);

  //Drawing into the cache
  jmpPatch(drawMinitile_40C3B0,     0x0040C3B0);
  jmpPatch(drawTileOverlay_40AAE0,  0x0040AAE0);
  jmpPatch(drawMegatile_49B9F0,     0x0049B9F0);
  jmpPatch(clipTileRect_49B8D0,     0x0049B8D0);
  jmpPatch(drawTileRect_49BC20,     0x0049BC20);
  jmpPatch(drawTileColumn_49BD40,   0x0049BD40);
  jmpPatch(drawTileRow_49BE20,      0x0049BE20);
  jmpPatch(updateTileRow_49C620,    0x0049C620);
  jmpPatch(drawMapTiles_49C780,     0x0049C780);

  //Tile counts and scroll limits
  callPatch(refillRows_49BF46,        0x0049BF46);
  callPatch(scrollRightColumn_49C155, 0x0049C155);
  callPatch(scrollDownRow_49C315,     0x0049C315);
  callPatch(inCacheX_49C543,          0x0049C543);
  callPatch(inCacheY_49C554,          0x0049C554);
  callPatch(maxScrollY_49BBCD,        0x0049BBCD);
  callPatch(maxScrollX_49BBE6,        0x0049BBE6);
  callPatch(minimapBoxH_4A3F6C,       0x004A3F6C);
  callPatch(minimapBoxW_4A3F7E,       0x004A3F7E);

  //Sprite blitters
  for (u32 site : blitterSites)
    callPatch(blitterTarget, site);

  //Space Platform starfield
  jmpPatch(drawStarsDirty_47EBF0,   0x0047EBF0);
  jmpPatch(drawStarsAll_47EE20,     0x0047EE20);
}

} //resolution
