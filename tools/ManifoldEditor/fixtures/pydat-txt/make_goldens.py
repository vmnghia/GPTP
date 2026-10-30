"""Makes the goldens for DatText: random-byte .dat files and PyMS's text export of them.

Usage: python3 -I make_goldens.py <path to the PyMS clone's src folder> [output folder]

PyMS (https://github.com/poiuyqwert/PyMS, bfc5d3a, MIT, (c) Zach Zahos) loads each file with
its own class and writes export_entries() (the text PyDAT's Export shows) next to it. The
.dat files are random bytes seeded below, so there is no Blizzard data in the fixtures,
except the boolean fields (random bytes are almost never 0 or 1): on entries 0, 3, 6, ... they
are set to 0 and on entries 1, 4, 7, ... to 1, so the True and False lines and the numbers
PyMS's bool coder passes through (2, 3, 5, ...) all appear.
The vanilla-size file of each of the 11 formats is written, and two expanded ones: units
with 252 entries and images with 1001.
"""
import os
import random
import sys

if len(sys.argv) < 2:
    sys.exit(__doc__)
sys.path.insert(0, sys.argv[1])
out = sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(os.path.abspath(__file__))

from PyMS.FileFormats.DAT import (UnitsDAT, WeaponsDAT, FlingyDAT, SpritesDAT, ImagesDAT, UpgradesDAT,
                                  TechDAT, SoundsDAT, PortraitsDAT, CampaignDAT, OrdersDAT)

CLASSES = [UnitsDAT, WeaponsDAT, FlingyDAT, SpritesDAT, ImagesDAT, UpgradesDAT, TechDAT, SoundsDAT,
           PortraitsDAT, CampaignDAT, OrdersDAT]
# The properties PyMS gives a DATBoolCoder (all of them bytes covering every entry).
BOOLS = {
    'images.dat': ['gfx_turns', 'clickable', 'use_full_iscript', 'draw_if_cloaked'],
    'orders.dat': ['use_weapon_targeting', 'unused_is_secondary', 'unused_allow_non_subunits',
                   'changes_subunit_order', 'unused_allow_subunits', 'interruptable', 'waypoints_slowdown',
                   'queueable', 'disabled_maintain_unit_target', 'obstructable', 'flee_unreturnable_damage',
                   'unused_requires_movable_unit'],
    'sprites.dat': ['unused', 'is_visible'],
    'techdata.dat': ['researched', 'broodwar_only'],
    'upgrades.dat': ['broodwar_only'],
}
EXPANDED = {'units': 252, 'images': 1001}

rng = random.Random(20261025)


def write(name, cls, size):
    data = bytearray(rng.randbytes(size))
    expanded = name.endswith('-expanded')
    count = size // cls.FORMAT.expanded_entry_size() if expanded else cls.FORMAT.entries
    offset = 0
    for prop in cls.FORMAT.properties:
        if prop.name in BOOLS.get(cls.FILE_NAME, []):
            assert prop.size(expanded) == 1 and prop.entry_count(count, expanded) == count
            for entry in range(count):
                if entry % 3 != 2:
                    data[offset + entry] = entry % 3
        offset += prop.total_size(count, expanded)
    data = bytes(data)
    dat = cls()
    dat.load_data(data)
    text = dat.export_entries()
    with open(os.path.join(out, name + '.dat'), 'wb') as f:
        f.write(data)
    with open(os.path.join(out, name + '.txt'), 'w', newline='\n', encoding='utf-8') as f:
        f.write(text)
    print('%s: %d entries, %d bytes' % (name, dat.entry_count(), size))


for cls in CLASSES:
    stem = cls.FILE_NAME[:-len('.dat')]
    write(stem, cls, cls.FORMAT.file_size())
    if stem in EXPANDED:
        write(stem + '-expanded', cls, cls.FORMAT.expanded_entry_size() * EXPANDED[stem])
