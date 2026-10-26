using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

/// <summary>Entry counts: PyMS's growth rules, the undo steps, and entries changed since save (counts spec §2).</summary>
public class DatCountTests
{
    static DatFile Zeros(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);
    static DatFile Expanded(DatId id, int entries) =>
        DatFile.Parse(DatFormats.Of(id), new byte[entries * DatFormats.Of(id).ExpandedEntrySize!.Value]);

    // -------- The rules (Task 1) --------

    [Fact]
    public void ExpandedCountAsPyMs()
    {
        var units = Zeros(DatId.Units);
        Assert.Equal(252, units.ExpandedCount(229));   // the minimum, 250, rounds up to the step of 12
        Assert.Equal(252, units.ExpandedCount(251));
        Assert.Equal(912, units.ExpandedCount(912));
        Assert.Null(units.ExpandedCount(913));
        Assert.Equal(264, Expanded(DatId.Units, 252).ExpandedCount(253));
        Assert.Equal(131, Zeros(DatId.Weapons).ExpandedCount(131));
        Assert.Null(Zeros(DatId.Weapons).ExpandedCount(256));
        var images = Zeros(DatId.Images);
        Assert.Equal(1000, images.ExpandedCount(1000));
        Assert.Equal(65536, images.ExpandedCount(65536));
        Assert.Null(images.ExpandedCount(65537));
        Assert.Null(Zeros(DatId.Mapdata).ExpandedCount(2));
    }

    [Fact]
    public void ExpandedCountRefusesHugeAndNegative()
    {
        var images = Zeros(DatId.Images);
        Assert.Null(images.ExpandedCount(long.MaxValue));
        Assert.Null(images.ExpandedCount(0));
        Assert.Null(images.ExpandedCount(-5));
    }

    [Fact]
    public void CanGrowAsTheLimits()
    {
        Assert.True(Zeros(DatId.Images).CanGrow);
        Assert.True(Zeros(DatId.Units).CanGrow);
        Assert.True(Zeros(DatId.Weapons).CanGrow);
        Assert.True(Zeros(DatId.Sfxdata).CanGrow);
        Assert.False(Zeros(DatId.Mapdata).CanGrow);
        Assert.False(Expanded(DatId.Weapons, 255).CanGrow);
    }

    [Fact]
    public void FirstGrowthRewritesTheLayout()
    {
        var sprites = Zeros(DatId.Sprites).With(130, "health_bar", 7).With(5, "image", 3);
        var grown = sprites.WithCount(518);
        Assert.True(grown.IsExpanded);
        Assert.Equal(518, grown.Count);
        Assert.Equal(7UL, grown.Get(130, "health_bar"));
        Assert.Equal(3UL, grown.Get(5, "image"));
        Assert.Equal(0UL, grown.Get(5, "health_bar"));
        foreach (var p in grown.Format.Properties) Assert.Equal(0UL, grown.Get(517, p.Name));
    }

    [Fact]
    public void UnitsGraphicsKeepsItsValueAsAShort()
    {
        var grown = Zeros(DatId.Units).With(0, "graphics", 200).WithCount(229);
        Assert.Equal(252, grown.Count);
        Assert.Equal(200UL, grown.Get(0, "graphics"));
        Assert.Equal(300UL, grown.With(0, "graphics", 300).Get(0, "graphics"));
    }

    [Fact]
    public void AGrownFileSavesAndParsesBack()
    {
        foreach (var id in new[] { DatId.Images, DatId.Units, DatId.Sprites })
        {
            var grown = Zeros(id).With(1, DatFormats.Of(id).Properties[0].Name, 1).WithCount(Zeros(id).Count + 1);
            var back = DatFile.Parse(grown.Format, grown.Save());
            Assert.True(back.IsExpanded);
            Assert.Equal(grown.Count, back.Count);
            Assert.True(back.SameAs(grown));
        }
    }

    [Fact]
    public void WithCountRefuses()
    {
        var images = Zeros(DatId.Images);
        Assert.Same(images, images.WithCount(images.Count));
        var fewer = Assert.Throws<ArgumentOutOfRangeException>(() => images.WithCount(images.Count - 1));
        Assert.Contains("images.dat has 999 entries: it can't have fewer", fewer.Message);
        var many = Assert.Throws<ArgumentOutOfRangeException>(() => images.WithCount(70000));
        Assert.Contains("images.dat has at most 65536 entries", many.Message);
    }
}
