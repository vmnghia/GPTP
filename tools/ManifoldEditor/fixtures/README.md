# Test fixtures for the Manifold Editor

Inputs the editor's tests read, copied here so a session without the user's
`D:\SC Modding` folder can use them. See
`docs/superpowers/specs/2026-10-05-button-set-editor-design.md` §2.

| File | Source |
|---|---|
| `SCManifold.fgp` | `Firegraft\SCManifold.fgp` from `SCManifold.exe`'s MPQ (2026-10-05). The user's FireGraft project, "FgPa" v7. Its `Buts` section starts at 0x250: 18 sets, 2438 bytes. |
| `reqlist.txt`, `actlist.txt` | `Firegraft\reqlist.txt` and `Firegraft\actlist.txt` from the same MPQ: FireGraft's condition and action display lists (`<n>Entry=name`, `<n>Button`, `<n>Unit`, `<n>VarType`). 68 conditions, 60 actions. |
| `FireGraftConFunc.txt`, `FireGraftActFunc.txt` | From the old FireGraftEx skeleton: `name<TAB>address[<TAB>var]`, one line per FireGraft index from 0. 67 conditions, 60 actions. |
| `Icons.txt` | PyMS `PyMS/Data/Icons.txt`: one name per `cmdicons.grp` frame. |
| `Icons.pal` | PyMS `Palettes/Icons.pal` (768 bytes, raw RGB): the palette PyMS draws `cmdicons.grp` with. PyMS is MIT-licensed, (c) Zach Zahos (poiuyqwert). |
| `pydat/*.txt` | PyMS `PyMS/Data/*.txt` at bfc5d3a: PyDAT's name lists and `Hints.txt`. MIT, (c) Zach Zahos (poiuyqwert); `pydat/LICENSE.md`. |
| `pydat-txt/` | random-byte .dat files (no Blizzard data) and PyMS bfc5d3a's TXT exports of them (`make_goldens.py`), the goldens for `DatText`. Seed 20261025; the boolean fields are set to 0 and 1 on two entries in three, as the script's header says. Nothing was patched: every PyMS class exports under this machine's Python 3.11 as is. Regenerate: `python3 -I make_goldens.py <PyMS clone>/src` (the 11 vanilla-size files, `units-expanded` with 252 entries and `images-expanded` with 1001). |

Not here, because they can't be shared: `StarCraft.exe` and the vanilla MPQs. Tests that
need them skip when `MANIFOLD_SC_DIR` is unset.
