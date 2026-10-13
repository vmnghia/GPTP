# Test files

`buttonsets-bad-position.bin`: a `Manifold\buttonsets.bin` the loader must refuse. The
header and sizes are right, but set 0 (the Marine) has a button at position 16, outside
1-15. In game, the plugin should print `buttonsets.bin: a position outside 1-15` once at
game start and keep the sets FireGraft's runtime applied, so the cards look like the
FireGraft project's, not the editor's. The editor refuses it the same way when opening.

Use it on a spare copy of the exe: add it with PyMPQ as `Manifold\buttonsets.bin`,
uncompressed or PKWARE (imploded); never zlib, which 1.16.1's Storm can't read.

Checked 2026-10-09: the plugin's parser (`SCBW/buttonsets_file.cpp`, built with g++)
returns "a position outside 1-15"; the editor's reader returns "set 0: position 16".
Don't save from the editor while that copy is open: it would fall back to vanilla plus
FireGraft's sets and write those.
