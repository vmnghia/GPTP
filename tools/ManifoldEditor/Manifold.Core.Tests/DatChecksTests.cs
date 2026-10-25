using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class DatChecksTests
{
    static DatFile Zero(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);

    static DatFile Expanded(DatId id, int entries) =>
        DatFile.Parse(DatFormats.Of(id), new byte[entries * DatFormats.Of(id).ExpandedEntrySize!.Value]);

    static ButtonSetDocument Document(Dictionary<DatId, DatFile> files)
    {
        var opened = DatFormats.All.ToDictionary(f => f.Id, f => Zero(f.Id));
        foreach (var (id, file) in files) opened[id] = file;
        var vanilla = DatFormats.All.ToDictionary(f => f.Id, f => Zero(f.Id));
        return new(Make.EmptySets(), Make.EmptySets(), dats: new DatSet(opened, vanilla));
    }

    static ButtonSetDocument Clean() => Document([]);

    [Fact]
    public void A_clean_document_has_no_checks() => Assert.Empty(DatChecks.Lines(Clean()));

    [Fact]
    public void A_document_with_no_dat_files_has_no_checks() =>
        Assert.Empty(DatChecks.Lines(new ButtonSetDocument(Make.EmptySets(), Make.EmptySets())));

    [Fact]
    public void A_reference_past_its_file_is_found()
    {
        var doc = Document(new() { [DatId.Units] = Zero(DatId.Units).With(0, "ground_weapon", 140) });
        var line = Assert.Single(DatChecks.Lines(doc));
        Assert.Equal(new DatCheck(DatId.Units, 0, "ground_weapon", "units.dat 0, ground_weapon: 140 is past weapons.dat's 130 entries"), line);
    }

    [Fact]
    public void A_reference_equal_to_the_count_is_none_and_fine()
    {
        var doc = Document(new() { [DatId.Units] = Zero(DatId.Units).With(0, "ground_weapon", 130) });
        Assert.Empty(DatChecks.Lines(doc));
    }

    [Fact]
    public void A_reference_holding_its_property_s_none_value_is_fine()
    {
        // units.dat's portrait names no portrait with 65535 (PyDAT's none_value), not with portdata's count.
        Assert.Equal(65535UL, DatFormats.Of(DatId.Units)["portrait"].NoneValue);
        Assert.Empty(DatChecks.Lines(Document(new() { [DatId.Units] = Zero(DatId.Units).With(91, "portrait", 65535) })));

        var past = Document(new() { [DatId.Units] = Zero(DatId.Units).With(91, "portrait", 65534) });
        Assert.Equal(new DatCheck(DatId.Units, 91, "portrait", "units.dat 91, portrait: 65534 is past portdata.dat's 110 entries"),
            Assert.Single(DatChecks.Lines(past)));
    }

    [Fact]
    public void Only_the_portrait_and_the_highlight_icon_have_a_none_value()
    {
        var withNone = DatFormats.All.SelectMany(f => f.Properties.Where(p => p.NoneValue is not null).Select(p => (f.Id, p.Name, p.NoneValue)));
        Assert.Equal([(DatId.Units, "portrait", (ulong?)65535), (DatId.Orders, "highlight_icon", (ulong?)65535)], withNone.ToArray());
    }

    [Fact]
    public void A_missing_target_file_skips_its_reference_check()
    {
        var opened = DatFormats.All.Where(f => f.Id is DatId.Units).ToDictionary(f => f.Id, f => Zero(f.Id).With(0, "ground_weapon", 140));
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), dats: new DatSet(opened, opened));
        Assert.Empty(DatChecks.Lines(doc));
    }

    [Fact]
    public void A_selection_circle_naming_a_missing_image_is_found()
    {
        // The byte holds at most 255, so only a short images.dat (an expanded one) can be overrun.
        var doc = Document(new()
        {
            [DatId.Sprites] = Zero(DatId.Sprites).With(130, "selection_circle_image", 255),
            [DatId.Images] = Expanded(DatId.Images, 700),
        });
        var line = Assert.Single(DatChecks.Lines(doc));
        Assert.Equal(new DatCheck(DatId.Sprites, 130, "selection_circle_image",
            "sprites.dat 130, selection_circle_image: names image 816, past images.dat's 700 entries"), line);

        var last = Document(new()
        {
            [DatId.Sprites] = Zero(DatId.Sprites).With(130, "selection_circle_image", 255),
            [DatId.Images] = Expanded(DatId.Images, 817),   // image 816 is its last
        });
        Assert.Empty(DatChecks.Lines(last));
        Assert.Empty(DatChecks.Lines(Document(new() { [DatId.Sprites] = Zero(DatId.Sprites).With(130, "selection_circle_image", 255) })));
    }

    [Fact]
    public void An_extended_file_the_plugin_does_not_read_is_refused()
    {
        var doc = Document(new() { [DatId.Units] = Expanded(DatId.Units, 252) });
        var line = Assert.Single(DatChecks.Lines(doc));
        Assert.Equal(new DatCheck(DatId.Units, null, null, "units.dat is extended (252 entries): the plugin refuses it"), line);
    }

    [Fact]
    public void An_extended_file_the_plugin_reads_is_fine()
    {
        var doc = Document(new() { [DatId.Images] = Expanded(DatId.Images, 1200) });
        Assert.Empty(DatChecks.Lines(doc));
    }

    [Fact]
    public void Lines_come_in_file_then_entry_order()
    {
        var doc = Document(new()
        {
            [DatId.Orders] = Zero(DatId.Orders).With(5, "targeting", 200),
            [DatId.Units] = Zero(DatId.Units).With(9, "air_weapon", 140).With(2, "ground_weapon", 140),
        });
        Assert.Equal([(DatId.Units, 2), (DatId.Units, 9), (DatId.Orders, 5)],
            DatChecks.Lines(doc).Select(l => (l.File, l.Entry!.Value)).ToArray());
    }
}
