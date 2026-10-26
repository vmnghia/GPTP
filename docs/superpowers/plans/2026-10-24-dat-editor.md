# Manifold Editor part 4 (.dat files, PyDAT's fields) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The 11 .dat files read and written byte for byte (vanilla and expanded layouts), every field of PyDAT's tabs on a Data page, undo, per-entry revert, and Save into the exe with to-repack copies.

**Architecture:** A table-driven format layer in `Manifold.Core/Data/Dat/` (one `DatFormat` per file, one `DatFile` reader/writer for all), names, lists, the tab table and checks in `Manifold.Core/Editor/`, the files joining `ButtonSetDocument`'s one history. The window gets a hand-built Data page in `ManifoldEditor.App/Dat/`, one class per PyDAT tab, built from a small set of field helpers.

**Tech Stack:** C# .NET 8, xUnit (+ Xunit.SkippableFact), WinUI 3 built in code (no .xaml).

**Spec:** `docs/superpowers/specs/2026-10-24-dat-editor-design.md`. The reference is PyMS at `bfc5d3a`, cloned at `/tmp/claude-0/-home-user-GPTP/766c9e66-d560-5026-8461-003701a27af9/scratchpad/pyms/src` (`PyMS/FileFormats/DAT/*DAT.py` for the formats, `PyMS/PyDAT/*Tab.py` for the tabs, `PyMS/Data/*.txt` for the lists and hints, `PyMS/MPQ/arr/*.dat` vanilla copies for the round-trip tests).

## Global Constraints

- Core checks: `dotnet test tools/ManifoldEditor/Manifold.Core.Tests` (all pass; the vanilla tests run with `MANIFOLD_ARR_DIR=<pyms>/PyMS/MPQ/arr`).
- App check: `/tmp/claude-0/-home-user-GPTP/766c9e66-d560-5026-8461-003701a27af9/scratchpad/appcheck.sh` (wraps `dotnet msbuild -restore -t:Compile -p:WindowsAppSDKSelfContained=false` in `ManifoldEditor.App`; no "error CS" lines).
- No `.xaml` files. In WinUI built in code: set `TextBox.AcceptsReturn` before `Text`; never add one element to two parents; build fresh items for a list instead of reusing UIElements; one ContentDialog at a time, at most three buttons.
- PyMS's property names are used unchanged as the property keys (`hit_points`, `special_ability_flags`, ...).
- The plugin extends only images.dat, sprites.dat and flingy.dat (`origin/feat/dat-extender`).
- Status texts copied verbatim from the spec: "arr\images.dat: the exe's copy was refused (reason); vanilla's is shown" and "arr\units.dat is extended (250 entries): the plugin refuses it and the game will show its invalid-file warning".
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01771DQsv5DDEKm1LjYbN2zB`. No model ids in code or docs.

## Review Focus

- A .dat file in the exe of the wrong size opens as vanilla's, with its note, and Save doesn't write it unless edited (Task 5 test `RefusedExeFileOpensVanillaAndStaysClean`).
- Undo after moving to another file or entry goes back to the file, entry and property it changed (Task 4 test `UndoReturnsTheDatPlace`).
- A number box given "", "-1", "abc", "0x10" or a value past the type's range records nothing (Task 2 test `ParseNumberRefusesWhatTheTypeCantHold`).
- An expanded file's entries past vanilla's count: named "Expanded Image #1000", covered by every property, Revert to vanilla refused (Task 1 `ExpandedCoversEveryEntry`, Task 3 `ExpandedEntriesAreNamedAsPyMS`, Task 4 `RevertToVanillaRefusedPastVanillaCount`).
- A to-repack folder without an `arr\` subfolder: Save creates it (Task 5 test `MirrorWriteCreatesArrFolder`).

---

### Task 1: The formats and `DatFile`

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Dat/DatFormat.cs` (`DatId`, `DatType`, `DatProperty`, `DatFormat`)
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Dat/DatFormats.cs` (the 11 tables)
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Dat/DatFile.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatFileTests.cs`, `tools/ManifoldEditor/Manifold.Core.Tests/DatVanillaTests.cs`

**Interfaces:**
- Produces:
  - `enum DatId { Units, Weapons, Flingy, Sprites, Images, Upgrades, Techdata, Sfxdata, Portdata, Mapdata, Orders }` (PyDAT's tab order).
  - `enum DatType { Byte, Short, Long, Size, Position, Extents, HitPoints, Supply }`; `static class DatTypes { int Bytes(DatType); ulong Max(DatType); }` (1/2/4/4/4/8/4/1 bytes; Max is the unsigned maximum of those bytes).
  - `sealed record DatProperty(string Name, DatType Type, int? VanillaFirst = null, int? VanillaCount = null, DatType? ExpandedType = null, DatId? Target = null)` with `DatType TypeIn(bool expanded)`, `bool Covers(int entry, bool expanded)`.
  - `sealed record DatFormat(DatId Id, string FileName, int Entries, IReadOnlyList<DatProperty> Properties, int? ExpandedMin = null, int? ExpandedMax = null, int? ExpandedMultiple = null, IReadOnlyList<int>? Reserved = null)` with `string ArchivePath` ("arr\\" + FileName), `int VanillaSize`, `int? ExpandedEntrySize` (null when `ExpandedMax` is null), `int IndexOf(string property)` (throws `ArgumentException` for an unknown name), `DatProperty this[string property]`.
  - `static class DatFormats { DatFormat Of(DatId); IReadOnlyList<DatFormat> All; bool PluginExtends(DatId) }` (`PluginExtends`: Images, Sprites, Flingy).
  - `sealed class DatFile` with `DatFormat Format`, `int Count`, `bool IsExpanded`, `static DatFile Parse(DatFormat, byte[])` (throws `InvalidDataException`), `ulong? Get(int entry, string property)`, `bool Covers(int entry, string property)`, `DatFile With(int entry, string property, ulong value)`, `DatFile WithEntryFrom(int entry, DatFile source)`, `bool SameAs(DatFile other)`, `bool EntrySameAs(int entry, DatFile other)`, `byte[] Save()`.

- [ ] **Step 1: Write the failing tests** (`DatFileTests`)

```csharp
[Fact] public void FormatsHaveVanillaSizes()
{
    var sizes = new Dictionary<DatId, int> { [DatId.Units] = 19876, [DatId.Weapons] = 5460, [DatId.Flingy] = 3135,
        [DatId.Sprites] = 3229, [DatId.Images] = 37962, [DatId.Upgrades] = 1281, [DatId.Techdata] = 836,
        [DatId.Sfxdata] = 10296, [DatId.Portdata] = 1320, [DatId.Mapdata] = 260, [DatId.Orders] = 4158 };
    foreach (var (id, size) in sizes) Assert.Equal(size, DatFormats.Of(id).VanillaSize);
    Assert.Equal(38, DatFormats.Of(DatId.Images).ExpandedEntrySize);
    Assert.Equal(7, DatFormats.Of(DatId.Sprites).ExpandedEntrySize);
    Assert.Equal(15, DatFormats.Of(DatId.Flingy).ExpandedEntrySize);
    Assert.Null(DatFormats.Of(DatId.Mapdata).ExpandedEntrySize);
}
[Fact] public void VanillaZerosRoundTrip()          // every format: new byte[VanillaSize] parses, Count == Entries, !IsExpanded, Save() equals the input
[Fact] public void RangedPropertyHasNoValueOutsideIt()
{   // sprites, vanilla: health_bar of 129 is null, of 130 is 0; units: infestation of 105 null, 106 not
}
[Fact] public void ExpandedCoversEveryEntry()
{   // sprites expanded with 600 entries (600*7 bytes): Count 600, IsExpanded, Get(5, "health_bar") == 0 (not null), Save() round-trips;
    // units expanded with 252 entries: graphics is a short (With(0, "graphics", 300) works; vanilla units refuses 300)
}
[Fact] public void RefusesOtherSizes()
{   // images 38001 bytes: InvalidDataException whose message is
    // "images.dat is 38001 bytes: not 37962, nor a whole number of 38-byte entries";
    // mapdata 264 bytes refused; units 249 expanded entries refused (below 250); units 251 refused (not a multiple of 12);
    // orders 256 expanded entries refused (max 255)
}
[Fact] public void WithIsImmutableAndChecksRange()
{   // a.With(0,"armor",3) leaves a.Get(0,"armor")==0; byte 256 -> ArgumentOutOfRangeException;
    // With on an uncovered entry (sprites vanilla health_bar of 5) -> ArgumentOutOfRangeException;
    // b.SameAs(a) false, a.With(0,"armor",0).SameAs(a) true; EntrySameAs(1, b) true
}
[Fact] public void WithEntryFromCopiesEveryCoveredProperty()
```

`DatVanillaTests` (each `[SkippableFact]`, `Skip.If(ArrDir is null, "MANIFOLD_ARR_DIR is not set")`, `ArrDir => Environment.GetEnvironmentVariable("MANIFOLD_ARR_DIR")`):

```csharp
[SkippableFact] public void EveryVanillaFileRoundTrips()   // for each format: Parse(File.ReadAllBytes(Path.Combine(dir, FileName))).Save() equals the bytes
[SkippableFact] public void SpotValues()
{   // units 0 (Marine): hit_points 10240, ground_weapon 0, build_time 360, mineral_cost 50, staredit_group_flags 10,
    //   supply_required 2, graphics 78, subunit1 228, sight_range 7, staredit_placement_size 1310737;
    // units 106: infestation 130; units 5: subunit1 6;
    // weapons 0: label 229, damage_amount 6, weapon_cooldown 15, maximum_range 128, icon 323, damage_upgrade 7, graphics 142;
    // sprites 0: image 589; sprites 130: health_bar 19; flingy 0: sprite 130, speed 1707, halt_distance 13616; images 0: grp_file 1
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter "FullyQualifiedName~DatFileTests|FullyQualifiedName~DatVanillaTests"`
Expected: build errors (`DatFormats` not defined).

- [ ] **Step 3: Implement the formats and `DatFile`**

`DatFormats` copies PyMS's `FORMAT` tables in order (property names, types, `entry_offset`/`entry_count` as `VanillaFirst`/`VanillaCount`, units `graphics` `ExpandedType = Short`, the limits from the spec §3 table, units' `Reserved` 228-250). `Target` is the PyMS comment's `.dat` (not the TBLs, not iscript.bin, not cmdicons): units graphics→Flingy, subunit1/2 and infestation→Units, construction_animation→Images, comp_ai_idle/human_ai_idle/return_to_idle/attack_unit/attack_move→Orders, ground_weapon/air_weapon→Weapons, armor_upgrade→Upgrades, the seven sound properties→Sfxdata, portrait→Portdata; weapons graphics→Flingy, unused_technology→Techdata, damage_upgrade→Upgrades; flingy sprite→Sprites; sprites image→Images (selection_circle_image is special, Task 6); orders targeting→Weapons, technology_energy→Techdata, obscured_order→Orders.

`DatFile` stores one `ulong[]` per property and the format's per-property entry offset (`VanillaFirst` in the vanilla layout, 0 expanded). Parse rules from spec §3: vanilla size → vanilla; else `ExpandedEntrySize` divides the size and the count is within `ExpandedMin`/`ExpandedMax` and a multiple of `ExpandedMultiple` → expanded; else throw with the message pattern above (when the table has no expanded layout: "mapdata.dat is 264 bytes: not 260"). Values are little-endian unsigned of `DatTypes.Bytes`. `With` copies only the edited property's array.

- [ ] **Step 4: Run the tests**

Run: `MANIFOLD_ARR_DIR=/tmp/claude-0/-home-user-GPTP/766c9e66-d560-5026-8461-003701a27af9/scratchpad/pyms/src/PyMS/MPQ/arr dotnet test tools/ManifoldEditor/Manifold.Core.Tests`
Expected: all pass, the vanilla tests not skipped.

- [ ] **Step 5: Commit** — `git commit -m "Manifold Editor: the .dat formats, vanilla and expanded"`

### Task 2: Values, the lists, iscript ids and entry search

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Dat/DatValues.cs`
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Dat/IscriptIds.cs`
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/DatLists.cs`
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/EntrySearch.cs`
- Create: `tools/ManifoldEditor/fixtures/pydat/` (PyMS's `Data/*.txt` except `Listfile.txt` and `SEMPQ.exe`, plus `LICENSE.md`); modify `tools/ManifoldEditor/fixtures/README.md` (one row)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/ManifoldEditor.App.csproj` (Content `..\fixtures\pydat\*` → `Data\pydat\`)
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatValuesTests.cs`

**Interfaces:**
- Produces:
  - `static class DatValues`: `ulong? ParseNumber(string text, DatType type)` (decimal digits only, trimmed; null when empty, not digits, or past `Max`); `ulong? ParseId(string text)` (decimal or `0x` hex, for Jump to ID); `(uint Whole, byte Fraction) HitPoints(ulong)`, `ulong HitPoints(uint whole, byte fraction)`; `(byte Whole, bool Half) Supply(ulong)`, `ulong Supply(byte whole, bool half)`; `ushort Part(ulong value, int index)` and `ulong WithPart(ulong value, int index, ushort part)` for size, position and extents (index 0 the lowest short: width, x, left; extents order left, up, right, down); `double Seconds(ulong frames)` = frames / 24.0 (PyDAT's Fastest); `double Pixels(ulong value)` = value / 256.0 (flingy speed and halt distance).
  - `static class IscriptIds { IReadOnlyList<int> Read(byte[] bin); IReadOnlyList<string> Names(IReadOnlyList<int> ids, IReadOnlyList<string> idList); }` — `Read`: u16 at 0 is the table's offset; (u16 id, u16 offset) pairs until id 0xFFFF and offset 0 or the end; sorted ids. `Names`: PyDAT's `ImagesTab` rule, indexed by id from 0 to the last: a gap is "*Unused*", an id within `idList` its name, else "Unnamed Custom Entry". (PyMS's pickled extra names are not read.)
  - `sealed class DatLists { static DatLists Load(string folder); static DatLists From(IReadOnlyDictionary<string, string[]> lists, IReadOnlyDictionary<string, string> hints); IReadOnlyList<string> this[string name]; string? Hint(string key); static readonly IReadOnlyList<string> Names; }` — `Names` lists every file the editor reads ("Units", "Weapons", "Flingy", "Sprites", "Images", "Upgrades", "Techdata", "Sfxdata", "Portdata", "Mapdata", "Orders", "IscriptIDList", "Icons", "Animations", "Behaviours", "DamTypes", "DrawList", "ElevationLevels", "Explosions", "FlingyControl", "Races", "Remapping", "Rightclick", "SelCircleSize", "ShieldSize", "UnitSize"); a list is the file's lines (trailing spaces trimmed, the last line kept without a newline); `Hints.txt` lines `Key=Text` (lines starting `[` or `#` skipped). A missing list is empty.
  - `static class EntrySearch { IReadOnlyList<int> Filter(IReadOnlyList<string> names, string query); }` — trimmed query; empty → all; else names containing it (ignoring case), or the id equal to `ParseId(query)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ParseNumberRefusesWhatTheTypeCantHold()
{
    Assert.Null(DatValues.ParseNumber("", DatType.Byte)); Assert.Null(DatValues.ParseNumber("-1", DatType.Byte));
    Assert.Null(DatValues.ParseNumber("abc", DatType.Byte)); Assert.Null(DatValues.ParseNumber("0x10", DatType.Byte));
    Assert.Null(DatValues.ParseNumber("256", DatType.Byte)); Assert.Equal(255UL, DatValues.ParseNumber(" 255 ", DatType.Byte));
    Assert.Equal(4294967295UL, DatValues.ParseNumber("4294967295", DatType.Long));
}
[Fact] public void ParseIdTakesHex() { Assert.Equal(16UL, DatValues.ParseId("0x10")); Assert.Equal(16UL, DatValues.ParseId("16")); Assert.Null(DatValues.ParseId("x")); }
[Fact] public void CompoundParts()
{
    Assert.Equal((40u, (byte)0), DatValues.HitPoints(10240)); Assert.Equal(10240UL + 127, DatValues.HitPoints(40, 127));
    Assert.Equal(((byte)1, false), DatValues.Supply(2)); Assert.Equal(3UL, DatValues.Supply(1, true));
    Assert.Equal((ushort)17, DatValues.Part(1310737, 0)); Assert.Equal((ushort)20, DatValues.Part(1310737, 1));
    Assert.Equal(1310737UL, DatValues.WithPart(DatValues.WithPart(0, 0, 17), 1, 20));
    Assert.Equal(15.0, DatValues.Seconds(360));
}
[Fact] public void IscriptIdsReadTheTable()   // a built bin: offset 4 at 0, pairs (0,x),(2,x),(0xFFFF,0) -> [0, 2]; Names([0,2], ["A","B"]) == ["A","*Unused*","Unnamed Custom Entry"]
[Fact] public void ListsAndHintsLoadFromFixtures()   // DatLists.Load(fixtures\pydat): Units has 228 names, Units[0] "Terran Marine", Hint("UnitHP") starts "Unit Hit Points"
[Fact] public void EveryListTheEditorReadsIsInFixtures()   // each DatLists.Names has fixtures\pydat\<name>.txt, and Hints.txt exists
[Fact] public void EntrySearchMatchesNamesAndIds()   // ["Marine","Ghost","Firebat"]: "gh" -> [1]; "0x2" -> [2]; "" -> [0,1,2]
```

- [ ] **Step 2: Run them to see them fail** — `dotnet test ... --filter DatValuesTests`; expected: build errors.
- [ ] **Step 3: Copy the lists, then implement.** `cp <pyms>/PyMS/Data/*.txt tools/ManifoldEditor/fixtures/pydat/` (then remove `Listfile.txt`), copy `<pyms>/LICENSE.md` there; README row: "`pydat/*.txt` | PyMS `PyMS/Data/*.txt` at bfc5d3a: PyDAT's name lists and `Hints.txt`. MIT, (c) Zach Zahos (poiuyqwert); `pydat/LICENSE.md`." The App's csproj gains `<Content Include="..\fixtures\pydat\*" Link="Data\pydat\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`.
- [ ] **Step 4: Run the tests** — all pass.
- [ ] **Step 5: Commit** — `"Manifold Editor: .dat values, PyMS's lists and hints, iscript ids"`

### Task 3: Entry names

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/DatNames.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatNamesTests.cs`

**Interfaces:**
- Consumes: `DatFile`, `DatLists`, `StringTable` (1-based `Segment(id)`), `TblText.Decompile`.
- Produces:
  - `enum NamesMode { Basic, Tbl, Combined }`.
  - `sealed record DatNameSources(Func<DatId, DatFile?> Dat, StringTable? StatTxt, StringTable? UnitNames, StringTable? ImagesTbl, StringTable? SfxdataTbl, StringTable? PortdataTbl, StringTable? MapdataTbl, DatLists Lists)`.
  - `static class DatNames { IReadOnlyList<string> Build(DatId file, DatNameSources sources, NamesMode mode, bool simple); bool HasSimple(DatId file); string TypeName(DatId file); string? TblString(StringTable? table, ulong id); }`.

Rules (PyMS `DATEntryName`, spec §6): the default name is `"{TypeName} #{id}"`, prefixed "Expanded " when `id >= Format.Entries`; type names Unit, Weapon, Flingy, Sprite, Image, Upgrade, Tech, Sound, Portrait, Map, Order. Units 228-250 of an expanded units.dat are "Reserved Unit #N" in every mode. Basic: the list's name when `id` is within it. Tbl: units, `UnitNames` segment `id+1` when present, else `StatTxt` segment `id+1` for `id < 228`; weapons, upgrades, techdata and orders, `StatTxt` segment `label` (0 → "None"); images, `ImagesTbl` segment `grp_file` (0 → none); sprites, their image's; flingy, their sprite's image's; sfxdata `sound_file` in `SfxdataTbl` (0 → "No sound"), portdata `idle_portrait_file` in `PortdataTbl` (0 → "None"), mapdata `map_file` in `MapdataTbl`. A TBL string is `TblText.Decompile` of the segment less one trailing NUL. Simple (only where `HasSimple`: units, weapons, upgrades, techdata, orders): units split on `<0>`, first part plus " (second)" unless it is "*", `<N>` codes removed; labels lose a leading hotkey `^.<0*[0-5]>` and `<N>` codes. Combined: "list (tbl)", or the one that exists, or one when they are equal. A name that comes out empty is the default name.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void BasicUsesTheList()               // units Basic: [0] "Terran Marine"
[Fact] public void TblUsesStatTxtAndFollowsEdits()   // a StatTxt whose segment 1 is "Baneling\0*\0Zerg\0": Tbl [0] "Baneling<0>*<0>Zerg", simple "Baneling"; after table.With(1, "Raptor\0*\0\0") the rebuilt name is "Raptor"
[Fact] public void UnitNamesTblWins()                // UnitNames with segment 1 "Hero" -> Tbl [0] starts "Hero"
[Fact] public void CombinedJoinsDifferentNames()     // list "Terran Marine", tbl "Baneling" -> "Terran Marine (Baneling)"; equal -> once
[Fact] public void LabelsStripHotkeysWhenSimple()    // weapons label 1 = "G<1>Gauss Rifle" style segment: simple -> "Gauss Rifle"
[Fact] public void ImagesSpritesFlingyChainThroughImagesTbl()  // images 0 grp_file 1 -> images.tbl string 1; sprite with image 0 and flingy with that sprite get it too
[Fact] public void ExpandedEntriesAreNamedAsPyMS()   // images expanded 1001 entries, Images.txt of 999 names: Basic names[1000] == "Expanded Image #1000"; units expanded 252: names[230] == "Reserved Unit #230"
[Fact] public void MissingSourcesFallBack()          // no StatTxt: Tbl gives "Unit #0"
```

- [ ] **Step 2: Run them to see them fail** — build errors.
- [ ] **Step 3: Implement `DatNames`** per the rules above.
- [ ] **Step 4: Run the tests** — all pass.
- [ ] **Step 5: Commit** — `"Manifold Editor: .dat entry names, PyDAT's three modes"`

### Task 4: The tab table, the document and the history

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/DatTabs.cs`
- Create: `tools/ManifoldEditor/Manifold.Core/Model/DatSet.cs`
- Modify: `tools/ManifoldEditor/Manifold.Core/Model/ButtonSetDocument.cs` (`Place`, `Change`, dats, `MarkSaved`)
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorState.cs` (`EditDat`, `RevertEntry`, `LastDatPlace`)
- Modify: `tools/ManifoldEditor/Manifold.Core/Data/UnitsDat.cs` (`Of(DatFile)`)
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatTabsTests.cs`, `tools/ManifoldEditor/Manifold.Core.Tests/DatDocumentTests.cs`

**Interfaces:**
- Produces:
  - `enum DatTabId { UnitsBasic, UnitsAdvanced, UnitsSounds, UnitsGraphics, UnitsStarEdit, UnitsAiActions, Weapons, Flingy, Sprites, Images, Upgrades, Techdata, Sounds, Portraits, Campaign, Orders }`.
  - `sealed record DatTab(DatTabId Id, DatId File, string Name, IReadOnlyList<string> Properties)`; `static class DatTabs { IReadOnlyList<DatTab> All; DatTab Of(DatTabId); IReadOnlyList<DatTab> For(DatId); DatTabId TabFor(DatId file, string property, DatTabId? current); string Property(DatTabId tab, string name); }` — `Property` returns the name, throwing `ArgumentException` when the tab doesn't list it (the App binds through it). `TabFor`: `current` when it lists the property, else the first tab that does. Names are PyDAT's ("Basic", "Advanced", "Sounds", "Graphics", "StarEdit", "AI Actions", "Weapons", "Flingy", "Sprites", "Images", "Upgrades", "Techdata", "Sound Effects", "Portraits", "Campaign", "Orders"). The property lists come from each PyDAT tab's `load_entry`/`save_entry` (the extraction in the brainstorm: Basic lists hit_points, shield_enabled, shield_amount, armor, armor_upgrade, mineral_cost, vespene_cost, build_time, broodwar_unit_flag, ground_weapon, max_ground_hits, air_weapon, max_air_hits, supply_required, supply_provided, staredit_group_flags, space_required, space_provided, build_score, destroy_score, unit_size, sight_range, target_acquisition_range; and so on for each tab file).
  - `readonly record struct DatPlace(DatId File, int Entry, string? Property)`; `Place(int? SetId, int? StringId, DatPlace? Dat = null)`.
  - `sealed record DatEdit(DatId File, DatFile Before, DatFile After, int Entry, string? Property)`; `Change(..., string Label, DatEdit? Dat = null)` with `Place` carrying it.
  - `sealed record DatSet(IReadOnlyDictionary<DatId, DatFile> Opened, IReadOnlyDictionary<DatId, DatFile> Vanilla)` (a file missing everywhere is absent from both).
  - `ButtonSetDocument` constructor gains `DatSet? dats = null`; members `DatFile? Dat(DatId)`, `DatFile? OpenedDat(DatId)`, `DatFile? VanillaDat(DatId)`, `bool DatDirty(DatId)` (current differs from the saved file as values), `bool EntryDiffersFromVanilla(DatId, int entry)` (false past vanilla's count or without vanilla), `bool ApplyDat(DatId file, Func<DatFile, DatFile> edit, int entry, string? property, string label)`, `void UseOpenedDat(DatId, DatFile)` (before any edit, like `UseOpenedStrings`: opened and current become the copy, saved stays the exe's). `IsDirty` includes any `DatDirty`; `MarkSaved` makes the current files the saved and opened ones.
  - `EditorState`: `bool EditDat(DatId file, int entry, string property, ulong value)` (label `$"Edit {property}"`; false when unchanged or refused), `bool RevertEntry(DatId file, int entry, bool toVanilla)` (labels "Revert entry" / "Revert entry to vanilla"; false when the entry matches or, to vanilla, `entry >= vanilla.Count`), `DatPlace? LastDatPlace` (set by `Undo`/`Redo` from the change's place, like `LastStringId`).
  - `UnitsDat.Of(DatFile units)`; `UnitsDat.Parse(byte[])` becomes `Of(DatFile.Parse(DatFormats.Of(DatId.Units), bytes))`.

- [ ] **Step 1: Write the failing tests**

```csharp
// DatTabsTests
[Fact] public void EveryPropertyShowsOnATab()   // for every format and property: DatTabs.For(format.Id).Any(t => t.Properties.Contains(p.Name))
[Fact] public void TabsListOnlyRealProperties()  // every tab property is in its file's format
[Fact] public void TabForPrefersTheCurrentTab()   // TabFor(Units, "ground_weapon", UnitsAiActions) == UnitsAiActions; with null == UnitsBasic
[Fact] public void PropertyRefusesOneTheTabDoesntShow()  // Property(UnitsBasic, "subunit1") throws ArgumentException

// DatDocumentTests (a document from Make.EmptySets() and a DatSet of zero-filled vanilla files)
[Fact] public void EditIsOneStepAndDirtiesTheFile()   // EditDat(Units,0,"armor",3): Dat(Units).Get(0,"armor")==3, DatDirty(Units), IsDirty, CanUndo
[Fact] public void UnchangedEditRecordsNothing()      // EditDat(Units,0,"armor",0) false, !CanUndo
[Fact] public void UndoReturnsTheDatPlace()           // edit Units 5 armor, then Weapons 2 damage_amount; Undo -> LastDatPlace (Weapons,2,"damage_amount"); Undo -> (Units,5,"armor")
[Fact] public void UndoneByHandIsClean()              // 0->3->0 by two edits: !DatDirty(Units)
[Fact] public void RevertEntryToOpenedAndVanilla()    // vanilla armor 0, opened armor 1 (a DatSet with differing files): edit to 5; RevertEntry(false) -> 1; RevertEntry(true) -> 0; each one undo step
[Fact] public void RevertToVanillaRefusedPastVanillaCount()   // expanded images 1001 entries, vanilla 999: RevertEntry(Images, 1000, true) false
[Fact] public void UseOpenedDatCountsAsUnsaved()      // UseOpenedDat(Units, copy) -> DatDirty(Units); MarkSaved -> clean
[Fact] public void EntryDiffersFromVanilla()          // after an edit of entry 3 only entry 3 differs
[Fact] public void UnitsViewReadsTheEditedFile()      // UnitsDat.Of(doc.Dat(Units)!).RaceOf(0) changes from None to Zerg after EditDat(Units,0,"staredit_group_flags",1)
```

- [ ] **Step 2: Run them to see them fail** — build errors.
- [ ] **Step 3: Implement.** `Put` swaps `Dat.File` to `Before`/`After`. The existing positional `Change(...)` and `Place(...)` calls stay valid (new parameters are last, optional).
- [ ] **Step 4: Run all Core tests** — all pass, the existing ones unchanged.
- [ ] **Step 5: Commit** — `"Manifold Editor: the .dat files in the document, one history, revert entry"`

### Task 5: Opening, saving and the to-repack copies

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Model/DatSources.cs` (`DatSources`, `DatMirror`)
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorResources.cs` (the TBLs, iscript.bin; `Units` removed)
- Modify: `tools/ManifoldEditor/Manifold.Core/Model/EditorSession.cs` (`Open` takes the `DatSet`; `FilesToSave` adds the dirty files; `Save` reports them)
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatSaveTests.cs`

**Interfaces:**
- Consumes: `ResourceResolver.Find(path) → (byte[] Data, string Source)?`, `StringMirror.Write`, `EditorSettings.RepackFolderFor`.
- Produces:
  - `static class DatSources { DatSet Load(ResourceResolver exeFirst, ResourceResolver vanillaOnly, List<string> sources, List<string> notes); }` — per file: `exeFirst.Find`, parsed; on `InvalidDataException`, the vanilla-only copy with the note "arr\images.dat: the exe's copy was refused (reason); vanilla's is shown"; parsed fine → source line "arr\units.dat: {Source}" and, when expanded and `!PluginExtends`, the note "arr\units.dat is extended (250 entries): the plugin refuses it and the game will show its invalid-file warning"; found nowhere → the note "arr\units.dat: not found; its tab is empty". `Vanilla` from `vanillaOnly` alone.
  - `EditorResources` gains `StringTable? ImagesTbl, SfxdataTbl, PortdataTbl, MapdataTbl, UnitNames` and `IReadOnlyList<int>? IscriptIds`, each loaded through `Get` with its note ("arr\images.tbl: not found; image names show numbers", `rez\unitnames.tbl` silent when missing, as PyMS treats it).
  - `EditorSession.Open(..., DatSet? dats = null)` passes it to the document. `EditorSession.Save(IWritableArchive, ButtonSetDocument, out byte[]? stringsWritten, out IReadOnlyList<(DatId File, byte[] Bytes)> datsWritten)`; the existing overloads keep their signatures. `FilesToSave` appends `(ArchivePath, Save())` for each `DatDirty` file. `static IReadOnlyList<DatId> RefusedExtended(ButtonSetDocument)` = dirty files that are expanded and `!PluginExtends`.
  - `static class DatMirror { string PathIn(string repackFolder, DatId id); void Write(string repackFolder, DatId id, byte[] bytes); IReadOnlyList<MirrorDiff> Differing(ButtonSetDocument doc, string repackFolder, string exePath, List<string> notes); }` with `sealed record MirrorDiff(DatId File, string Path, bool Newer, DatFile Copy)` — `Write` uses `StringMirror.Write` (which creates the folder); `Differing` lists copies that exist, parse, and differ from `doc.OpenedDat`; `Newer` = written after the exe; an unreadable copy is the note "to-repack\arr\units.dat: refused (reason)".

- [ ] **Step 1: Write the failing tests** (a fake `IArchive` per existing tests' pattern, temp folders)

```csharp
[Fact] public void LoadPrefersTheExeAndNamesTheSource()
[Fact] public void RefusedExeFileOpensVanillaAndStaysClean()   // exe units.dat of 100 bytes: Opened[Units] is vanilla's, the note text exact, a Save writes no arr\units.dat
[Fact] public void ExtendedRefusedTableIsNoted()                // exe units.dat expanded (252*entry size): the extended note text exact; RefusedExtended after an edit == [Units]
[Fact] public void SaveWritesOnlyDirtyFiles()                   // edit weapons only: the written files are buttonsets.bin and arr\weapons.dat; datsWritten has Weapons; after it !DatDirty
[Fact] public void MirrorWriteCreatesArrFolder()                // DatMirror.Write(temp, Weapons, bytes) creates temp\arr\weapons.dat
[Fact] public void DifferingListsChangedCopies()                // same copy: none; changed copy: one, Newer true when its time is after the exe's
[Fact] public void SaveAsCarriesDirtyDatFiles()                 // EditorSession.SaveAs with the existing tests' fake IRepackHost: the added files include arr\weapons.dat after a weapons edit
```

- [ ] **Step 2: Run them to see them fail** — build errors.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run all Core tests** — all pass.
- [ ] **Step 5: Commit** — `"Manifold Editor: .dat files opened from the exe, saved into it and to to-repack"`

### Task 6: Checks and settings

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/DatChecks.cs`
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorSettings.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatChecksTests.cs` (and add to `WindowSettingsTests.cs`)

**Interfaces:**
- Produces:
  - `sealed record DatCheck(DatId File, int? Entry, string? Property, string Text)`; `static class DatChecks { IReadOnlyList<DatCheck> Lines(ButtonSetDocument doc); }`, in file then entry order:
    - a `Target` value above the target file's `Count` (equal to it is that field's None): "units.dat 0, ground_weapon: 140 is past weapons.dat's 130 entries";
    - sprites' `selection_circle_image` with `561 + value >= images.Count`: "sprites.dat 130, selection_circle_image: names image 1000, past images.dat's 999 entries";
    - an expanded file of a table the plugin refuses: "units.dat is extended (252 entries): the plugin refuses it" (Entry null).
  - `EditorSettings`: `LastPage` also takes "data"; `string? LastDat` (a `DatId` name), `string? LastUnitsTab` (a `DatTabId` name), `Dictionary<string, int>? DatEntries` (file name → last entry), `Dictionary<string, DatNameSetting>? DatNames` with `sealed record DatNameSetting(NamesMode Mode, bool Simple)`; helpers `int EntryFor(DatId)`, `WithEntry(DatId, int)`, `DatNameSetting NamesFor(DatId)` (default Basic, not simple), `WithNames(DatId, DatNameSetting)`.

- [ ] **Step 1: Write the failing tests** — one per check text above (exact strings), a clean document gives none; settings round-trip through `Save`/`Load` with `LastPage "data"`, `LastDat "Weapons"`, an entry and a names setting; defaults when absent.
- [ ] **Step 2: Run them to see them fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run all Core tests** — all pass.
- [ ] **Step 5: Commit** — `"Manifold Editor: .dat checks and the Data page's settings"`

### Task 7: The window opens, saves and undoes the .dat files

**Files:**
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Services/Workspace.cs` (`Open`: the vanilla-only resolver, `DatSources.Load`, `DatLists`, the catalog from the document's units)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/MainWindow.cs`

**Interfaces:**
- Consumes: Tasks 4-6.
- Produces: `OpenedExe` gains `DatLists Lists`. `Workspace.LoadDatLists()` reads `Data\pydat` beside the exe. MainWindow:
  - Save writes the to-repack copies after the exe (`DatMirror.Write(settings.RepackFolderFor(exe), ...)` per `datsWritten`); a failure is one status line "to-repack\arr\units.dat not written: (reason)", the exe keeps the edit.
  - Before writing, when `EditorSession.RefusedExtended(doc)` is non-empty and not yet asked this session: a dialog "units.dat is extended: the plugin refuses an extended units.dat and the game will show its invalid-file warning." with "Save anyway" / "Cancel".
  - On opening, `OfferRepackStrings` becomes `OfferRepackCopies`: the strings case as today plus `DatMirror.Differing`; one dialog listing each file and which side was changed more recently ("The to-repack copy" / "The exe"), choices "The exe's" / "to-repack's"; to-repack's applies `UseOpenedStrings` and `UseOpenedDat` for every listed file.
  - The catalog is rebuilt (`SetCatalog.Build(state.UnitName, UnitsDat.Of(doc.Dat(Units)))`) when the units file reference changes, beside the existing strings check at the `catalogStrings` site.
  - Undo/Redo: when `LastDatPlace` is set, show the Data page, its file tab, `DatTabs.TabFor(file, property, current)` and the entry (via the page's `Show(DatPlace)`, Task 8).
- [ ] **Step 1: Make the changes** (the page bar's third item is added in Task 8; here `Show(DatPlace)` is called on the page field Task 8 creates — do Tasks 7 and 8 before the compile check).
- [ ] **Step 2: Commit after Task 8's compile check.**

### Task 8: The Data page shell and the field helpers

**Files:**
- Create: `tools/ManifoldEditor/ManifoldEditor.App/Dat/DataPage.cs`
- Create: `tools/ManifoldEditor/ManifoldEditor.App/Dat/DatFields.cs`
- Create: `tools/ManifoldEditor/ManifoldEditor.App/Dat/EntryBox.cs`
- Create: `tools/ManifoldEditor/ManifoldEditor.App/Dat/IDatTab.cs`
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/MainWindow.cs` (the page bar's "Data" item, `LastPage "data"`, Ctrl+F / Ctrl+G when the page shows)

**Interfaces:**
- Produces:
  - `interface IDatTab { DatTabId Id; UIElement Root; void Show(int entry); }` — `Show` loads every field from the current file.
  - `sealed class DatContext` (passed to every tab): `Func<OpenedExe?> Opened`, `DatFile? File(DatId)`, `IReadOnlyList<string> Names(DatId)` (from `DatNames.Build` with the file's settings, cached per file until it or the strings change), `DatLists Lists`, `bool Edit(DatId file, int entry, string property, ulong value)` (calls `EditorState.EditDat`, then the window's refresh), `string? Hint(string key)`.
  - `DatFields` (each returns its element and registers a loader with the tab): `NumberField(tab, file, property, label, hintKey, width)` — a TextBox committing on LostFocus/Enter through `DatValues.ParseNumber`, red border and tooltip "0-255" (the type's range) when refused, disabled and empty when `!Covers`; `ReferenceField(tab, file, property, label, hintKey, Func<IReadOnlyList<string>> names, string? noneName)` — an `EntryBox` plus an id box, an id past the list shown "#300 (no name)"; `ChoiceField(... list name ...)` — a ComboBox of a PyMS list plus an id box; `FlagsField(tab, file, property, (string Label, ulong Mask)[] bits, columns)`; `CheckField(tab, file, property, label, hintKey)` (a 0/1 byte); `HitPointsField`, `SupplyField(label)`, `PartField(property, index, label)` for size/position/extents; `DerivedText(Func<int, string>)` for the seconds and pixels displays (formats "= 15 secs." and PyDAT's for speed and halt distance).
  - `EntryBox` — `FunctionBox`'s pattern over a name list: `Picked(int)`, `Show(IReadOnlyList<string> names, int id)`; filters with `EntrySearch.Filter`.
  - `DataPage(Func<OpenedExe?> opened, Action changed)`: `Root`, `Refresh()`, `Show(DatPlace place)`, `FocusFind()`, `FocusJump()`. Left: a virtualised ListView of `" {id padded} {name}"` (id padded with spaces to `min(4, digits of Count-1)`, • after an entry that `EntryDiffersFromVanilla`), PyDAT's Find (TextBox + Next; Enter finds the next match after the selection, wrapping, ignoring case), ID Jump (`DatValues.ParseId`; Enter or Go), Names (ComboBox Basic / TBL Based / Combined) and Simple TBL Names (enabled when `DatNames.HasSimple`), both saved per file. The list's context menu: Revert entry (Ctrl+Shift+R), Revert entry to vanilla (disabled past vanilla's count). Right: a TabView-free `SelectorBar` of the 11 file names (● when `DatDirty`), the Units sub-tab bar, the tab's `Root` in a ScrollViewer, and the check lines (`DatChecks.Lines`) as HyperlinkButtons that `Show` their entry. A file missing everywhere shows "arr\units.dat was not found." in place of its tab. The last file, units sub-tab and per-file entry are saved in the settings.
- [ ] **Step 1: Write the shell, the helpers and `EntryBox`; one placeholder `IDatTab` per `DatTabId` showing `DatTabs.Of(id).Name`** so the page compiles and runs before the tabs exist.
- [ ] **Step 2: Compile check** — `appcheck.sh`: no "error CS".
- [ ] **Step 3: Commit Tasks 7 and 8** — `"Manifold Editor: the Data page, with the .dat files opened, saved and undone"`

### Task 9: The Units sub-tabs

**Files:**
- Create: `tools/ManifoldEditor/ManifoldEditor.App/Dat/Tabs/UnitsBasicTab.cs`, `UnitsAdvancedTab.cs`, `UnitsSoundsTab.cs`, `UnitsGraphicsTab.cs`, `UnitsStarEditTab.cs`, `UnitsAiActionsTab.cs`
- Modify: `DataPage.cs` (the placeholders replaced)

Each tab ports its PyDAT file (`BasicUnitsTab.py`, `AdvancedUnitsTab.py`, `SoundsUnitsTab.py`, `GraphicsUnitsTab.py`, `StarEditUnitsTab.py`, `AIActionsUnitsTab.py`): the same groups (Basic: Vital Statistics, Build Cost, Weapons, Supply, Space, Score, Other), labels, field order, the hint keys its `tip(...)` calls name, and the kind of each field (number, reference, choice from the named PyMS list, flags with PyDAT's labels and bit masks, check, compound, derived). Properties are named through `DatTabs.Property(Id, ...)`. Left out, per spec: Jump buttons, "Used by", previews. Flag bit labels come from the tab files (special ability flags in Advanced, StarEdit group and availability flags in StarEdit, the Zerg/Terran/Protoss bits of the group flags on Basic, AI internal flags on AI Actions).

- [ ] **Step 1: Port the six tabs.**
- [ ] **Step 2: Compile check** — no "error CS".
- [ ] **Step 3: Commit** — `"Manifold Editor: the Data page's Units sub-tabs"`. At this point the user can test the Units tab on Windows if they want to, before the rest.

### Task 10: Weapons, Flingy, Sprites, Images

**Files:** Create `Dat/Tabs/WeaponsTab.cs`, `FlingyTab.cs`, `SpritesTab.cs`, `ImagesTab.cs`; modify `DataPage.cs`.

Same rule as Task 9, from `WeaponsTab.py` (cooldown seconds, target flags), `FlingyTab.py` (top speed and halt distance in pixels), `SpritesTab.py` (health bar and selection circle disabled under 130 in a vanilla file; selection circle image shown as images 561 on), `ImagesTab.py` (the images.tbl dropdowns with "None" at 0, the iscript id dropdown from `IscriptIds.Names`).

- [ ] **Step 1: Port the four tabs.** - [ ] **Step 2: Compile check.** - [ ] **Step 3: Commit** — `"Manifold Editor: the Weapons, Flingy, Sprites and Images tabs"`

### Task 11: Upgrades, Techdata, Sound Effects, Portraits, Campaign, Orders

**Files:** Create `Dat/Tabs/UpgradesTab.cs`, `TechdataTab.cs`, `SoundsTab.cs`, `PortraitsTab.cs`, `CampaignTab.cs`, `OrdersTab.cs`; modify `DataPage.cs` (no placeholder left).

From `UpgradesTab.py`, `TechnologyTab.py` (research time in seconds), `SoundsTab.py` (sound flags), `PortraitsTab.py` (idle and talking), `MapsTab.py`, `OrdersTab.py` (its booleans, the highlight icon from `Icons.txt`).

- [ ] **Step 1: Port the six tabs.** - [ ] **Step 2: Compile check.** - [ ] **Step 3: Commit** — `"Manifold Editor: the remaining Data tabs"`

### Task 12: Docs

**Files:** `tools/ManifoldEditor/README.md` (a "The Data page" section: what it edits, names modes, Save and the to-repack copies, `MANIFOLD_ARR_DIR`); `CLAUDE.md` (the Manifold Editor bullet: part 4, the .dat files); `docs/resolution.md` §6 (the editor's next parts: part 4 built, parts 2-5 of the .dat port listed; CRLF kept with `io.open(..., newline='')`); the spec's Status line ("Built 2026-10-24 ...; not yet run on Windows").

- [ ] **Step 1: Write the docs.** - [ ] **Step 2: Run all Core tests and the compile check.** - [ ] **Step 3: Commit** — `"Docs: the editor's Data page (.dat files, part 4)"`, then push the branch.

Then a final review of the whole branch by a fresh reviewer, the fixes, and the user's Windows test (spec §10).
