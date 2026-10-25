using Manifold.Core.Data;

namespace Manifold.Core.Tests;

/// <summary>DatText.Parse and Apply: PyMS's parse_text and import_data, and the round trip through Export.</summary>
public class DatTextImportTests
{
    static string? ArrDir => Environment.GetEnvironmentVariable("MANIFOLD_ARR_DIR");

    public static IEnumerable<object[]> Goldens() => DatTextExportTests.Goldens();

    static DatFormat FormatOf(string name) =>
        DatFormats.All.Single(f => f.FileName == name.Replace("-expanded", "") + ".dat");

    static DatFile Zero(DatFormat format, int size) => DatFile.Parse(format, new byte[size]);

    static DatFile VanillaUnits()
    {
        var format = DatFormats.Of(DatId.Units);
        return Zero(format, format.VanillaSize);
    }

    static DatTextException Refused(Action action) => Assert.Throws<DatTextException>(action);

    /// <summary>
    /// The goldens are random bytes, and PyMS's text lists only the bits below a flags coder's
    /// count, so the bits above are not in the text and not rebuilt: the file with them cleared.
    /// </summary>
    static DatFile WithoutUnlistedFlagBits(DatFile file)
    {
        foreach (var key in DatText.Keys(file.Format.Id).Where(k => k.Coder.Kind == DatCoderKind.Flags))
            for (int e = 0; e < file.Count; e++)
                if (file.Get(e, key.Property) is { } v)
                    file = file.With(e, key.Property, v & ((1UL << key.Coder.Bits) - 1));
        return file;
    }

    [Theory, MemberData(nameof(Goldens))]
    public void ApplyingTheGoldenRebuildsTheFile(string name)
    {
        var format = FormatOf(name);
        byte[] bytes = Fixtures.Bytes($"pydat-txt/{name}.dat");
        string golden = Fixtures.Text($"pydat-txt/{name}.txt");
        var (file, changed) = DatText.Apply(Zero(format, bytes.Length), DatText.Parse(golden));
        var expected = WithoutUnlistedFlagBits(DatFile.Parse(format, bytes));
        Assert.True(expected.SameAs(file), name);
        Assert.Equal(expected.Save(), file.Save());
        Assert.Equal(golden, DatText.Export(file, Enumerable.Range(0, file.Count)));
        Assert.NotEmpty(changed);

        // onto the fixture's own file nothing changes, high flag bits included: byte for byte
        var (same, none) = DatText.Apply(DatFile.Parse(format, bytes), DatText.Parse(golden));
        Assert.Equal(bytes, same.Save());
        Assert.Empty(none);
    }

    [Fact]
    public void FlagsKeepTheEntrysBitsAboveTheCoder()
    {
        var format = DatFormats.Of(DatId.Sfxdata);
        var target = Zero(format, format.VanillaSize).With(2, "flags", 0xC5);   // bits 6-7 are above the 6-bit coder
        var (file, changed) = DatText.Apply(target, DatText.Parse(
            "Sound(2):\n\tflags.preload False\n\tflags.unit_speech True\n\tflags.flag_0x04 True\n\tflags.never_preempt True\n"));
        Assert.Equal(0xC0UL | 0x02 | 0x04 | 0x20, file.Get(2, "flags"));
        Assert.Equal([2], changed);
    }

    [SkippableFact]
    public void VanillaFilesSurviveExportThenApply()
    {
        Skip.If(ArrDir is null, "MANIFOLD_ARR_DIR is not set");
        foreach (var format in DatFormats.All)
        {
            var bytes = File.ReadAllBytes(Path.Combine(ArrDir!, format.FileName));
            var file = DatFile.Parse(format, bytes);
            string text = DatText.Export(file, Enumerable.Range(0, file.Count));
            var (applied, _) = DatText.Apply(Zero(format, bytes.Length), DatText.Parse(text));
            Assert.True(bytes.SequenceEqual(applied.Save()), format.FileName);
            Assert.Equal(text, DatText.Export(applied, Enumerable.Range(0, applied.Count)));
        }
    }

    [Fact]
    public void ParsesCrlfBomAndTrailingSpace()
    {
        var entries = DatText.Parse("﻿Unit(0):  \r\n\tarmor 3 \r\n");
        var entry = Assert.Single(entries);
        Assert.Equal("Unit", entry.Type);
        Assert.Equal(0, entry.Id);
        Assert.Equal(("armor", "3", 2), Assert.Single(entry.Values));
        var (file, changed) = DatText.Apply(VanillaUnits(), entries);
        Assert.Equal(3UL, file.Get(0, "armor"));
        Assert.Equal([0], changed);
    }

    [Fact]
    public void ParseDropsCommentsAndBlankLinesAndKeepsLineNumbers()
    {
        var entries = DatText.Parse("# top\n\nUnit(1): # why\n  \n\thit_points.whole 40 # forty\n\tflags.True True\n");
        var entry = Assert.Single(entries);
        Assert.Equal(3, entry.Line);
        Assert.Equal([("hit_points.whole", "40", 5), ("flags.True", "True", 6)], entry.Values.ToArray());
    }

    [Fact]
    public void ApplyChangesOnlyListedProperties()
    {
        var before = VanillaUnits().With(0, "hit_points", 999).With(0, "armor", 9);
        var (file, _) = DatText.Apply(before, DatText.Parse("Unit(0):\n\tarmor 3\n"));
        Assert.Equal(3UL, file.Get(0, "armor"));
        Assert.Equal(999UL, file.Get(0, "hit_points"));
    }

    [Fact]
    public void ApplyRefusesTheWrongType()
    {
        var e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Weapon(0):\n\tlabel 1\n")));
        Assert.Equal("Invalid type (expected 'Unit', got 'Weapon')", e.Message);
        Assert.Equal(1, e.Line);
    }

    [Fact]
    public void ApplyRefusesAHalfCompound()
    {
        var e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\thit_points.whole 5\n")));
        Assert.Equal("Hit Points missing `fraction` value", e.Message);
        e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\thit_points.fraction 5\n")));
        Assert.Equal("Hit Points missing `whole` value", e.Message);
        e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tsupply_required.whole 5\n")));
        Assert.Equal("Supply missing `half` value", e.Message);
        e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tunit_extents.left 1\n\tunit_extents.up 1\n\tunit_extents.right 1\n")));
        Assert.Equal("Extents missing `down` value", e.Message);
        e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(106):\n\taddon_position.x 1\n")));
        Assert.Equal("Position missing `y` value", e.Message);
        e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tstaredit_placement_size.height 1\n")));
        Assert.Equal("Size missing `width` value", e.Message);
    }

    [Fact]
    public void ApplyRefusesAnUnknownProperty()
    {
        var e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tfoo 1\n")));
        Assert.Equal("Unrecognized property 'foo'", e.Message);
        Assert.Equal(2, e.Line);
    }

    [Fact]
    public void ApplyRefusesAPropertyTheEntryDoesNotCover()
    {
        var format = DatFormats.Of(DatId.Sprites);
        var sprites = Zero(format, format.VanillaSize);
        Assert.False(sprites.Covers(5, "health_bar"));
        var e = Refused(() => DatText.Apply(sprites, DatText.Parse("Sprite(5):\n\thealth_bar 1\n")));
        Assert.Equal("Unrecognized property 'health_bar'", e.Message);
        Assert.Equal(2, e.Line);
        var (file, _) = DatText.Apply(sprites, DatText.Parse("Sprite(200):\n\thealth_bar 1\n"));
        Assert.Equal(1UL, file.Get(200, "health_bar"));
    }

    [Fact]
    public void ApplyRefusesABadFlagName()
    {
        var e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tspecial_ability_flags.nope True\n")));
        Assert.Equal("Invalid flag name nope", e.Message);
        Assert.Equal(2, e.Line);
    }

    [Fact]
    public void FlagsTakeNamesAndHexOfAnyWidth()
    {
        var (file, _) = DatText.Apply(VanillaUnits(), DatText.Parse(
            "Unit(0):\n\tspecial_ability_flags.building True\n\tspecial_ability_flags.flag_0x100 True\n\tspecial_ability_flags.flyer False\n"));
        Assert.Equal(0x101UL, file.Get(0, "special_ability_flags"));
    }

    [Fact]
    public void FlagsListedReplaceTheWholeValue()
    {
        var before = VanillaUnits().With(0, "staredit_group_flags", 0xFF);
        var (file, _) = DatText.Apply(before, DatText.Parse("Unit(0):\n\tstaredit_group_flags.zerg True\n"));
        Assert.Equal(1UL, file.Get(0, "staredit_group_flags"));
    }

    [Fact]
    public void ApplyRefusesAnIdPastTheEnd()
    {
        var e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(228):\n\tarmor 1\n")));
        Assert.Equal("Invalid entry id (must be between 0 and 228, got 228)", e.Message);
        Assert.Equal(1, e.Line);
    }

    [Fact]
    public void ApplyRefusesAValueTooBig()
    {
        var e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tarmor 256\n")));
        Assert.Contains("armor", e.Message);
        Assert.Contains("255", e.Message);
        Assert.Equal(2, e.Line);
        e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tarmor 99999999999999999999999\n")));
        Assert.Equal(2, e.Line);
        e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\thit_points.whole 40\n\thit_points.fraction 256\n")));
        Assert.Contains("hit_points.fraction", e.Message);
        Assert.Equal(3, e.Line);
        e = Refused(() => DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tsupply_required.whole 128\n\tsupply_required.half False\n")));
        Assert.Contains("supply_required.whole", e.Message);
    }

    [Fact]
    public void ApplyEncodesTheCompounds()
    {
        var (file, _) = DatText.Apply(VanillaUnits(), DatText.Parse(
            "Unit(0):\n\thit_points.whole 40\n\thit_points.fraction 128\n\tsupply_required.whole 2\n\tsupply_required.half True\n" +
            "\tstaredit_placement_size.width 17\n\tstaredit_placement_size.height 20\n"));
        Assert.Equal((40UL << 8) | 128, file.Get(0, "hit_points"));
        Assert.Equal(5UL, file.Get(0, "supply_required"));
        Assert.Equal(17UL | (20UL << 16), file.Get(0, "staredit_placement_size"));
    }

    [Fact]
    public void BoolsTakeWordsAndNumbers()
    {
        var format = DatFormats.Of(DatId.Orders);
        var orders = Zero(format, format.VanillaSize);
        var (file, _) = DatText.Apply(orders, DatText.Parse("Order(0):\n\tuse_weapon_targeting True\n\tqueueable 7\n\tweapon_targeting 3\n"));
        Assert.Equal(1UL, file.Get(0, "use_weapon_targeting"));
        Assert.Equal(7UL, file.Get(0, "queueable"));
        Assert.Equal(3UL, file.Get(0, "targeting"));
    }

    [Fact]
    public void PortraitsTakeTheirDottedKeys()
    {
        var format = DatFormats.Of(DatId.Portdata);
        var (file, _) = DatText.Apply(Zero(format, format.VanillaSize), DatText.Parse("Portraits(0):\n\tidle.portrait_file 12\n\ttalking.smk_change 1\n"));
        Assert.Equal(12UL, file.Get(0, "idle_portrait_file"));
        Assert.Equal(1UL, file.Get(0, "talking_smk_change"));
    }

    [Fact]
    public void PasteIgnoresTheHeaderId()
    {
        var (file, changed) = DatText.Apply(VanillaUnits(), DatText.Parse("Unit(0):\n\tarmor 3\n"), onto: 5);
        Assert.Equal(3UL, file.Get(5, "armor"));
        Assert.Equal(0UL, file.Get(0, "armor"));
        Assert.Equal([5], changed);
        // a header with no id pastes too
        (file, _) = DatText.Apply(VanillaUnits(), DatText.Parse("Unit():\n\tarmor 4\n"), onto: 6);
        Assert.Equal(4UL, file.Get(6, "armor"));
    }

    [Fact]
    public void AMissingIdIsRefusedUnlessPasting()
    {
        var entries = DatText.Parse("Unit():\n\tarmor 4\n");
        var e = Refused(() => DatText.Apply(VanillaUnits(), entries));
        Assert.Equal("Entry missing id", e.Message);
        Assert.Equal(1, e.Line);
    }

    [Fact]
    public void PasteNeedsExactlyOneEntry()
    {
        var two = DatText.Parse("Unit(0):\n\tarmor 3\nUnit(1):\n\tarmor 4\n");
        Refused(() => DatText.Apply(VanillaUnits(), two, onto: 5));
    }

    [Fact]
    public void PasteRefusesAnEntryPastTheEnd()
    {
        var one = DatText.Parse("Unit(0):\n\tarmor 3\n");
        var e = Refused(() => DatText.Apply(VanillaUnits(), one, onto: 228));
        Assert.Equal("Invalid entry id (must be between 0 and 228, got 228)", e.Message);
    }

    [Fact]
    public void NothingIsHalfApplied()
    {
        var before = VanillaUnits();
        var entries = DatText.Parse("Unit(0):\n\tarmor 3\nUnit(1):\n\tarmor 999\n");
        Refused(() => DatText.Apply(before, entries));
        Assert.Equal(0UL, before.Get(0, "armor"));
        Assert.True(before.SameAs(VanillaUnits()));
    }

    [Fact]
    public void ApplyReportsTheChangedEntries()
    {
        var before = VanillaUnits().With(3, "armor", 2);
        var (_, changed) = DatText.Apply(before, DatText.Parse(
            "Unit(7):\n\tarmor 1\nUnit(3):\n\tarmor 2\nUnit(1):\n\tarmor 5\n\tsight_range 0\n"));
        Assert.Equal([1, 7], changed);   // 3 and the sight range changed nothing
    }

    [Fact]
    public void ParseRefusesAsPyMSDoes()
    {
        var e = Refused(() => DatText.Parse("\tarmor 3\n"));
        Assert.Equal("Missing header before defining properties", e.Message);
        Assert.Equal(1, e.Line);
        e = Refused(() => DatText.Parse("Unit(0):\n\tarmor three\n"));
        Assert.Equal("Unexpected line, expected a header or a property definition", e.Message);
        Assert.Equal(2, e.Line);
        e = Refused(() => DatText.Parse("Unit(0):\n\tarmor 3\nUnit(0):\n\tarmor 4\n"));
        Assert.Equal("Entry 0 already exists", e.Message);
        Assert.Equal(3, e.Line);
        e = Refused(() => DatText.Parse("Unit(0):\nUnit(1):\n\tarmor 4\n"));
        Assert.Equal("Entry 0 is empty", e.Message);
        Assert.Equal(1, e.Line);
        e = Refused(() => DatText.Parse("Unit(0):\n"));
        Assert.Equal("Entry 0 is empty", e.Message);
        e = Refused(() => DatText.Parse("# nothing\n\n"));
        Assert.Equal("No entries found", e.Message);
        Assert.Null(e.Line);
    }
}
