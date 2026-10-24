using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class DatTabsTests
{
    [Fact]
    public void EveryPropertyShowsOnATab()
    {
        foreach (var format in DatFormats.All)
            foreach (var p in format.Properties)
                Assert.True(DatTabs.For(format.Id).Any(t => t.Properties.Contains(p.Name)),
                    $"{format.FileName} {p.Name} is on no tab");
    }

    [Fact]
    public void TabsListOnlyRealProperties()
    {
        foreach (var tab in DatTabs.All)
        {
            var format = DatFormats.Of(tab.File);
            foreach (var name in tab.Properties)
                Assert.True(format.Properties.Any(p => p.Name == name), $"{tab.Name}: {name} is not in {format.FileName}");
            Assert.Equal(tab.Properties.Count, tab.Properties.Distinct().Count());
        }
    }

    [Fact]
    public void EveryTabIsInTheTableOnce()
    {
        foreach (var id in Enum.GetValues<DatTabId>()) Assert.Equal(id, DatTabs.Of(id).Id);
        Assert.Equal(Enum.GetValues<DatTabId>().Length, DatTabs.All.Count);
        Assert.Equal(6, DatTabs.For(DatId.Units).Count);
        Assert.Equal("Sound Effects", DatTabs.Of(DatTabId.Sounds).Name);
    }

    [Fact]
    public void TabForPrefersTheCurrentTab()
    {
        Assert.Equal(DatTabId.UnitsAiActions, DatTabs.TabFor(DatId.Units, "ground_weapon", DatTabId.UnitsAiActions));
        Assert.Equal(DatTabId.UnitsBasic, DatTabs.TabFor(DatId.Units, "ground_weapon", null));
        Assert.Equal(DatTabId.UnitsAdvanced, DatTabs.TabFor(DatId.Units, "subunit1", DatTabId.UnitsBasic));
    }

    [Fact]
    public void PropertyRefusesOneTheTabDoesntShow()
    {
        Assert.Equal("armor", DatTabs.Property(DatTabId.UnitsBasic, "armor"));
        Assert.Throws<ArgumentException>(() => DatTabs.Property(DatTabId.UnitsBasic, "subunit1"));
    }
}
