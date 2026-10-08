//Applies Manifold\buttonsets.bin, the Manifold Editor's button sets, at each
//game start. Spec: docs/superpowers/specs/2026-10-05-button-set-editor-design.md §4.
#pragma once

namespace bsloader {

//Reads and checks the file; only a good file changes the button set table.
//A missing file changes nothing and prints nothing; a bad one prints its
//reason once.
void applyAtGameStart();

} //bsloader
