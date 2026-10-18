#include "scbwdata.h"

//The scbwdata.h entries the .dat extender may move (SCBW_DATA_REF), defined
//here. Ported from saintofidiocy/GPTP@DatExtender (0a4516b), cut to the
//tables this port extends: images, sprites and flingy (DatExtend/datext.h).

//A reference that reads its pointer from the variable each time it is used.
#define SCBW_DATA_DEF(type, name, var)  type& name = (type&)var;
#define SCBW_DATA_VAR(type, name, init) type name = (type)init;

//Vanilla's five .lo? tables, 999 pointers each, one after another.
ImagesDatExtraOverlayLO_Files lo_files_dat = {
	(LO_Header**)0x0051F2A8, (LO_Header**)0x00520244, (LO_Header**)0x005211E0,
	(LO_Header**)0x0052217C, (LO_Header**)0x00523118 };

//The variables DatExtend/images_dat.cpp re-points (DATEXT_VAR_REF there).
SCBW_DATA_VAR(const LO_Header* const*,        shieldOverlays_var,     0x0052E5C8);
SCBW_DATA_VAR(u8*,                            damageOverlayFrame_var, 0x005240D0);
SCBW_DATA_VAR(GrpHead* const*,                imageGrpGraphics_var,   0x0051CED0);
SCBW_DATA_VAR(ImagesDatExtraOverlayLO_Files*, lo_files_var,           &lo_files_dat);

SCBW_DATA_DEF(const LO_Header* const*,        shieldOverlays,         shieldOverlays_var);
SCBW_DATA_DEF(GrpHead* const*,                imageGrpGraphics,       imageGrpGraphics_var);
SCBW_DATA_DEF(ImagesDatExtraOverlayLO_Files*, lo_files,               lo_files_var);

//Each .dat property through its load table, which the extender re-points.
namespace flingy_dat {
SCBW_DATA_DEF(u16*, SpriteID,        flingyDat[0].address);
SCBW_DATA_DEF(u32*, TopSpeed,        flingyDat[1].address);
SCBW_DATA_DEF(u16*, Acceleration,    flingyDat[2].address);
SCBW_DATA_DEF(u32*, HaltDistance,    flingyDat[3].address);
SCBW_DATA_DEF(u8*,  TurnSpeed,       flingyDat[4].address);
SCBW_DATA_DEF(u8*,  MovementControl, flingyDat[6].address);
} //flingy_dat

namespace sprites_dat {
SCBW_DATA_DEF(u16*, ImageId,   spritesDat[0].address);
SCBW_DATA_DEF(s8*,  HpBarSize, spritesDat[1].address);
SCBW_DATA_DEF(u8*,  IsVisible, spritesDat[3].address);
} //sprites_dat

namespace images_dat {
SCBW_DATA_DEF(const u8*,  IsTurnable,    imagesDat[1].address);
SCBW_DATA_DEF(const u8*,  IsClickable,   imagesDat[2].address);
SCBW_DATA_DEF(const u8*,  RLE_Function,  imagesDat[5].address);
SCBW_DATA_DEF(const u8*,  Remapping,     imagesDat[6].address);
SCBW_DATA_DEF(const u32*, DamageOverlay, imagesDat[10].address);
} //images_dat
