# FireGraft requirements, applied by the plugin

Status: [BUILT] 2026-10-29, host-tested, tested in game 2026-10-30 (round 2, all passed: the
load message, Build Viking from a Starport, a changed Marine requirement taking effect,
untouched units, upgrades and research, the Manifold Editor's buttons, a second game).

## Why

FireGraft's Dat Requirements (units, upgrades, research, tech use, orders) never reached the
game in Manifold. Build Viking (unit 91, the Cargo Ship's slot) stayed hidden, and a Marine
set to need an Academy still trained from a Barracks.

FireGraft's runtime (inside the mod exe's loader DLL, `StubDll.dll`) hooks StarCraft.exe's
import of storm.dll's `SFileOpenFileEx` (ordinal 268, slot 0x4FE570). When the game opens
`font\font8.fnt` at startup, the hook applies the project: requirements, button sets, exe
edits. Upstream GPTP's "campaign fix" (end of `initialize.cpp`, since GPTP 5.9) jumps the
game's stub at 0x4100C4 straight to storm's own function (0x15017960), so FireGraft's hook
never sees that open and applies nothing. The fix exists because FireGraft's hook drops the
archive handle it is given (searches every archive), which breaks campaign maps. It stays.

So none of the FireGraft project's data applies: requirements, its button sets (the Manifold
Editor's `buttonsets.bin` loader replaced those), and its Exe Edits.

## What the plugin does

`hooks/interface/requirements_loader.cpp`, called at game start (0x4EED10, the same wrapper
as the button set loader), once per run:

1. `Firegraft\datalist.txt` (FireGraft's own list) names the project; read it from the MPQ.
2. Build each table from its section (`UntR`, `UpgR`, `TecR`, `TecU`, `OrdR`), as FireGraft
   does (0x10008910 in its DLL): word 0, then each record whose "Used" byte is set, its id
   and its words, then 0xFFFF. Parser: `SCBW/fg_requirements*`, host test
   `tests/fg_requirements_test.bat`.
3. Check all 39 places the exe holds a table's address (or address + 2), found by scanning
   1.16.1's image: all in code. FireGraft finds the same ones by scanning 0x400000-0x500000.
   One that holds anything else stops the whole apply, with its address printed.
4. Point them at the plugin's tables, and set each table's offset cache back to 0xFFFF
   (units 0x660A70 ×228, upgrades 0x6558C0 ×61, research 0x656198 ×44, use 0x6562F8 ×44,
   orders 0x665580 ×189), so the game fills them from the new tables.

No size limit: the tables live in the plugin. A section missing from the project leaves that
table vanilla. Any problem prints one yellow line and leaves every table vanilla; success
prints "FireGraft requirements: N tables from <file>".

Synced data, but every client runs the same exe, so every client builds the same tables.

## Not done

FireGraft's Exe Edits (`ExEd`, 8 entries in the current project) and its `Unit` tab data
still don't apply. Neither did they before.
