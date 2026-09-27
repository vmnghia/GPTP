//Fog of war for the larger view.
//
//Brood War shades fog in three steps, all sized for 640x480:
//  0x0047FC50  per-tile darkness for the tiles around the view  (24 x 17 bytes, [0x006D5C14])
//  0x0047FD9B  smoothed copy                                     (same size, [0x006D5C0C];
//                                                                 last frame's in [0x006D5C10])
//  0x0047FE10  interpolated to 8x8-pixel cells                   (88 x 60 bytes, [0x006D5C18])
//then 0x004808E0 walks the dirty cells and 0x004805F0 shades them on screen.
//
//Nearly every constant involved is an immediate or displacement inside the
//game's own instructions, so most of this rewrites those values in place,
//including the buffer sizes the game allocates at map load (0x00480960).
//The Expander instead swapped in its own buffers every frame; enlarging the
//game's allocations keeps its pointers valid and lets it free them normally.

#include "resolution.h"
#include <hook_tools.h>

#define FOG_FINE     0x006D5C18  //u8* 8x8-pixel darkness, res_fogW per row

namespace {

s32 fogCoarseBytes;   //24 * 17 = 408 (0x198)
s32 fogCoarseDwords;  //0x66
s32 fogFineBytes;     //88 * 60 = 0x14A0
s32 fogFineDwords;    //0x528

template <typename T>
void patchValue(u32 address, T value) {
  memoryPatch(address, value);
}

} //unnamed namespace

//0x0047FEAB: add edx, 0x58; mov [ebp-4], edx (6 bytes). Next row of the fine
//grid. The width no longer fits the original 8-bit immediate.
void __declspec(naked) nextFineRow_47FEAB() {
  __asm {
    add edx, res_fogW
    mov [ebp-4], edx
    inc dword ptr [esp]
    retn
  }
}

//0x004805F0: shades the fog over a screen rect, 8x8 pixels at a time.
//eax = bottom, edx = top, [esp+4] = left, [esp+8] = right. RET 8.
//(0x58 fine-grid width, y * 640, 0x1400 = 640 * 8.)
const u32 Func_FogBlockClear = 0x004800A0;
const u32 Func_FogBlockSolid = 0x00480000;
const u32 Func_FogBlockBlend = 0x0047FF10;

void __declspec(naked) shadeFogRect_4805F0() {
  __asm {
    push ebp
    mov ebp, esp
    sub esp, 0x10
    mov ecx, dword ptr ds:[0x006284A8]  //MoveToY
    push ebx
    mov ebx, dword ptr ds:[0x0062848C]  //MoveToX
    push esi
    shr ecx, 3
    and ecx, 3
    mov esi, edx
    sar esi, 3
    add ecx, esi
    mov esi, dword ptr ds:[FOG_FINE]
    imul ecx, res_fogW
    shr ebx, 3
    and ebx, 3
    add ebx, esi
    add ebx, ecx
    push edi
    mov edi, [ebp+8]
    mov esi, edi
    sar esi, 3
    add esi, ebx
    mov ebx, res_screenBmp.data
    mov ecx, edx
    imul ecx, res_w
    add ecx, ebx
    add ecx, edi
    cmp edx, eax
    mov [ebp-0x0C], ecx
    jge EXIT
    sub eax, edx
    add esi, res_fogW
    dec eax
    shr eax, 3
    inc eax
    mov [ebp-8], esi
    mov [ebp-0x10], eax
  ROW:
    mov ebx, [ebp+0x0C]
    cmp edi, ebx
    mov [ebp-4], ecx
    jge ROWDONE
    sub ebx, edi
    dec ebx
    shr ebx, 3
    inc ebx
  BLOCK:
    mov edx, esi
    sub edx, res_fogW
    movzx eax, byte ptr [edx+1]
    movzx edx, byte ptr [edx]
    cmp edx, eax
    movzx ecx, byte ptr [esi]
    movzx edi, byte ptr [esi+1]
    jne BLEND
    cmp edx, ecx
    jne BLEND
    cmp edx, edi
    jne BLEND
    test edx, edx
    jne NOTCLEAR
    mov ecx, [ebp-4]
    call Func_FogBlockClear
    jmp NEXTBLOCK
  NOTCLEAR:
    cmp edx, 0x1F
    je NEXTBLOCK
    mov ecx, [ebp-4]
    call Func_FogBlockSolid
    jmp NEXTBLOCK
  BLEND:
    push edi
    push ecx
    mov ecx, [ebp-4]
    push eax
    call Func_FogBlockBlend
  NEXTBLOCK:
    mov ecx, [ebp-4]
    add ecx, 8
    inc esi
    dec ebx
    mov [ebp-4], ecx
    jne BLOCK
    mov ecx, [ebp-0x0C]
    mov edi, [ebp+8]
  ROWDONE:
    mov esi, [ebp-8]
    mov eax, [ebp-0x10]
    add esi, res_fogW
    mov edx, res_w
    shl edx, 3
    add ecx, edx
    dec eax
    mov [ebp-8], esi
    mov [ebp-0x0C], ecx
    mov [ebp-0x10], eax
    jne ROW
  EXIT:
    pop edi
    pop esi
    pop ebx
    mov esp, ebp
    pop ebp
    retn 8
  }
}

//0x004808E0: shades fog over the whole view (eax != 0) or over its dirty
//cells. (640, 400, 40-cell rows at 0x006CEFF8.)
void __declspec(naked) shadeFog_4808E0() {
  __asm {
    test eax, eax
    je DIRTY
    push res_w
    push 0
    mov eax, res_viewH
    xor edx, edx
    call shadeFogRect_4805F0
    retn
  DIRTY:
    push ebx
    push esi
    push edi
    mov edi, res_cells
    xor ebx, ebx
  ROW:
    xor edx, edx
  CELL:
    mov al, [edi]
    inc edi
    test al, al
    je NEXTCELL
    lea eax, [edx+0x10]
    cmp eax, res_w
    mov ecx, 0x10
    jge RUNDONE
  RUN:
    cmp byte ptr [edi], 0
    je RUNDONE
    add eax, 0x10
    inc edi
    add ecx, 0x10
    cmp eax, res_w
    jl RUN
  RUNDONE:
    lea esi, [ecx+edx]
    push esi
    push edx
    lea eax, [ebx+0x10]
    mov edx, ebx
    call shadeFogRect_4805F0
    lea edx, [esi-0x10]
  NEXTCELL:
    add edx, 0x10
    cmp edx, res_w
    jl CELL
    add ebx, 0x10
    cmp ebx, res_viewH
    jl ROW
    pop edi
    pop esi
    pop ebx
    retn
  }
}

namespace resolution {

void injectFogHooks() {
  const s32 cw = res_fogCW;   //24: coarse grid width, tiles across the cache + 3
  const s32 ch = res_fogCH;   //17

  fogCoarseDwords = (cw * ch + 3) / 4 + 16;
  fogCoarseBytes = fogCoarseDwords * 4;
  fogFineDwords = (res_fogW * res_fogH + 3) / 4 + 16;
  fogFineBytes = fogFineDwords * 4;
  res_fogCDwords = (cw * ch + 3) / 4;

  //Allocations at map load (0x00480960)
  patchValue<s32>(0x004809A5 + 1, fogCoarseBytes);   //push 0x198
  patchValue<s32>(0x004809CB + 1, fogCoarseDwords);  //mov ecx, 0x66
  patchValue<s32>(0x004809D0 + 1, fogCoarseBytes);
  patchValue<s32>(0x004809F1 + 1, fogCoarseDwords);
  patchValue<s32>(0x004809F6 + 1, fogCoarseBytes);
  patchValue<s32>(0x00480A17 + 1, fogCoarseDwords);
  patchValue<s32>(0x00480A1C + 1, fogFineBytes);     //push 0x14A0
  patchValue<s32>(0x00480A30 + 1, fogFineDwords);    //mov ecx, 0x528

  //Copying this frame's smoothed grid to last frame's (mov ecx, 0x66)
  patchValue<s32>(0x004BD5A8 + 1, res_fogCDwords);
  patchValue<s32>(0x004805E3 + 1, res_fogCDwords);

  //0x0047FC50, per-tile darkness
  patchValue<s32>(0x0047FCA6 + 3, ch);               //rows (17)
  patchValue<s32>(0x0047FCC3 + 3, cw);               //columns (24)
  patchValue<s8>(0x0047FD80 + 2, (s8)cw);            //add ecx, 0x18

  //0x0047FD9B, smoothing over the 3x3 neighbourhood
  patchValue<s8>(0x0047FD9B + 2, (s8)(cw + 1));      //add eax, 0x19
  patchValue<s8>(0x0047FD9E + 2, (s8)(cw + 1));      //add ecx, 0x19
  patchValue<s32>(0x0047FDA1 + 1, ch - 2);           //mov edi, 0x0F  rows
  patchValue<s32>(0x0047FDB0 + 1, cw - 2);           //mov esi, 0x16  columns
  patchValue<s8>(0x0047FDB5 + 3, (s8)-cw);           //[eax-0x18]
  patchValue<s8>(0x0047FDBF + 3, (s8)cw);            //[eax+0x18]
  patchValue<s8>(0x0047FDD1 + 3, (s8)-(cw + 1));     //[eax-0x19]
  patchValue<s8>(0x0047FDD8 + 3, (s8)(cw + 1));      //[eax+0x19]
  patchValue<s8>(0x0047FDDE + 3, (s8)-(cw - 1));     //[eax-0x17]
  patchValue<s8>(0x0047FDE4 + 3, (s8)(cw - 1));      //[eax+0x17]

  //0x0047FE10, interpolation to the fine grid
  patchValue<s8>(0x0047FE23 + 2, (s8)(cw + 1));      //add ebx, 0x19
  patchValue<s32>(0x0047FE2D + 3, res_cacheRows);    //rows (14)
  patchValue<s32>(0x0047FE40 + 3, res_cacheCols);    //columns (21)
  patchValue<s8>(0x0047FE53 + 3, (s8)cw);            //[ebx+0x18]
  patchValue<s8>(0x0047FE6B + 3, (s8)(cw + 1));      //[ebx+0x19]
  callPatch(nextFineRow_47FEAB, 0x0047FEAB, 1);      //add edx, 0x58; mov [ebp-4], edx
  patchValue<s32>(0x0047FEC7 + 2, res_fogW4m4);      //sub ecx, 0x15C
  patchValue<s32>(0x0047FEE4 + 2, res_fogW3p4);      //add ecx, 0x10C

  //0x004804D0, marking changed 32-pixel blocks dirty
  patchValue<s32>(0x004804F8 + 1, res_w64);          //mov ebx, 0x2C0
  patchValue<s32>(0x00480502 + 1, res_h);            //mov eax, 0x1E0
  patchValue<s32>(0x00480509 + 2, -res_h);           //lea edx, [eax-0x1E0]
  patchValue<s8>(0x0048050F + 2, (s8)(cw + 1));      //add edi, 0x19
  patchValue<s8>(0x00480512 + 2, (s8)(cw + 1));      //add esi, 0x19
  patchValue<s32>(0x00480523 + 2, -res_w64);         //lea ecx, [ebx-0x2C0]

  //Screen stride inside the 8x8 shading blocks
  patchValue<s32>(0x0047FFDE + 2, res_w);            //0x0047FF10: add esi, 0x280
  patchValue<s32>(0x00480087 + 2, res_w);            //0x00480000: add esi, 0x280
  for (s32 row = 1; row < 8; ++row) {                //0x004800A0: [ecx + row*0x280 (+4)]
    patchValue<s32>(0x004800B4 + (row - 1) * 12 + 2, row * res_w);
    patchValue<s32>(0x004800BA + (row - 1) * 12 + 2, row * res_w + 4);
  }

  jmpPatch(shadeFogRect_4805F0, 0x004805F0);
  jmpPatch(shadeFog_4808E0,     0x004808E0);
}

} //resolution
