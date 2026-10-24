using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class DatFileTests
{
    static DatFile Zeros(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);

    static DatFile Expanded(DatId id, int entries) =>
        DatFile.Parse(DatFormats.Of(id), new byte[entries * DatFormats.Of(id).ExpandedEntrySize!.Value]);

    [Fact]
    public void FormatsHaveVanillaSizes()
    {
        var sizes = new Dictionary<DatId, int>
        {
            [DatId.Units] = 19876, [DatId.Weapons] = 5460, [DatId.Flingy] = 3135,
            [DatId.Sprites] = 3229, [DatId.Images] = 37962, [DatId.Upgrades] = 1281, [DatId.Techdata] = 836,
            [DatId.Sfxdata] = 10296, [DatId.Portdata] = 1320, [DatId.Mapdata] = 260, [DatId.Orders] = 4158,
        };
        foreach (var (id, size) in sizes) Assert.Equal(size, DatFormats.Of(id).VanillaSize);
        Assert.Equal(38, DatFormats.Of(DatId.Images).ExpandedEntrySize);
        Assert.Equal(7, DatFormats.Of(DatId.Sprites).ExpandedEntrySize);
        Assert.Equal(15, DatFormats.Of(DatId.Flingy).ExpandedEntrySize);
        Assert.Null(DatFormats.Of(DatId.Mapdata).ExpandedEntrySize);
    }

    [Fact]
    public void AllIsInIdOrderAndTheArchivePathIsUnderArr()
    {
        Assert.Equal(11, DatFormats.All.Count);
        for (int i = 0; i < DatFormats.All.Count; i++) Assert.Equal((DatId)i, DatFormats.All[i].Id);
        Assert.Equal("arr\\units.dat", DatFormats.Of(DatId.Units).ArchivePath);
        Assert.True(DatFormats.PluginExtends(DatId.Images));
        Assert.True(DatFormats.PluginExtends(DatId.Sprites));
        Assert.True(DatFormats.PluginExtends(DatId.Flingy));
        Assert.False(DatFormats.PluginExtends(DatId.Units));
    }

    [Fact]
    public void TypesHaveTheirByteCounts()
    {
        Assert.Equal(new[] { 1, 2, 4, 4, 4, 8, 4, 1 }, Enum.GetValues<DatType>().Select(DatTypes.Bytes));
        Assert.Equal(255UL, DatTypes.Max(DatType.Byte));
        Assert.Equal(65535UL, DatTypes.Max(DatType.Short));
        Assert.Equal(uint.MaxValue, DatTypes.Max(DatType.Long));
        Assert.Equal(ulong.MaxValue, DatTypes.Max(DatType.Extents));
    }

    [Fact]
    public void PropertiesAreLookedUpByName()
    {
        var units = DatFormats.Of(DatId.Units);
        Assert.Equal(54, units.Properties.Count);
        Assert.Equal(0, units.IndexOf("graphics"));
        Assert.Equal(DatId.Flingy, units["graphics"].Target);
        Assert.Equal(DatType.Short, units["graphics"].TypeIn(true));
        Assert.Equal(DatType.Byte, units["graphics"].TypeIn(false));
        Assert.Throws<ArgumentException>(() => units.IndexOf("nope"));
    }

    [Fact]
    public void VanillaZerosRoundTrip()
    {
        foreach (var format in DatFormats.All)
        {
            var bytes = new byte[format.VanillaSize];
            var file = DatFile.Parse(format, bytes);
            Assert.Equal(format.Entries, file.Count);
            Assert.False(file.IsExpanded);
            Assert.Equal(bytes, file.Save());
        }
    }

    [Fact]
    public void RangedPropertyHasNoValueOutsideIt()
    {
        var sprites = Zeros(DatId.Sprites);
        Assert.Null(sprites.Get(129, "health_bar"));
        Assert.Equal(0UL, sprites.Get(130, "health_bar"));
        Assert.Equal(0UL, sprites.Get(516, "health_bar"));
        Assert.False(sprites.Covers(5, "health_bar"));
        Assert.True(sprites.Covers(130, "health_bar"));
        Assert.True(sprites.Covers(5, "image"));

        var units = Zeros(DatId.Units);
        Assert.Null(units.Get(105, "infestation"));
        Assert.NotNull(units.Get(106, "infestation"));
        Assert.Null(units.Get(202, "infestation"));
        Assert.NotNull(units.Get(105, "ready_sound"));
        Assert.Null(units.Get(106, "ready_sound"));
    }

    [Fact]
    public void ExpandedCoversEveryEntry()
    {
        var sprites = Expanded(DatId.Sprites, 600);
        Assert.Equal(600, sprites.Count);
        Assert.True(sprites.IsExpanded);
        Assert.Equal(0UL, sprites.Get(5, "health_bar"));
        Assert.True(sprites.Covers(5, "health_bar"));
        Assert.Equal(new byte[600 * 7], sprites.Save());

        var units = Expanded(DatId.Units, 252);
        Assert.Equal(252, units.Count);
        Assert.Equal(300UL, units.With(0, "graphics", 300).Get(0, "graphics"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Zeros(DatId.Units).With(0, "graphics", 300));
    }

    [Fact]
    public void ExpandedValuesRoundTripInTheirOwnLayout()
    {
        var bytes = new byte[300 * 15];
        new Random(7).NextBytes(bytes);
        var flingy = DatFile.Parse(DatFormats.Of(DatId.Flingy), bytes);
        Assert.Equal(bytes, flingy.Save());
        Assert.Equal(BitConverter.ToUInt16(bytes, 0), flingy.Get(0, "sprite"));
        Assert.Equal(BitConverter.ToUInt32(bytes, 300 * 2), flingy.Get(0, "speed"));
    }

    [Fact]
    public void RefusesOtherSizes()
    {
        var images = DatFormats.Of(DatId.Images);
        var ex = Assert.Throws<InvalidDataException>(() => DatFile.Parse(images, new byte[38001]));
        Assert.Equal("images.dat is 38001 bytes: not 37962, nor a whole number of 38-byte entries", ex.Message);

        var mapdata = Assert.Throws<InvalidDataException>(() => DatFile.Parse(DatFormats.Of(DatId.Mapdata), new byte[264]));
        Assert.Equal("mapdata.dat is 264 bytes: not 260", mapdata.Message);

        var units = DatFormats.Of(DatId.Units);
        int unit = units.ExpandedEntrySize!.Value;
        Assert.Throws<InvalidDataException>(() => DatFile.Parse(units, new byte[249 * unit]));
        Assert.Throws<InvalidDataException>(() => DatFile.Parse(units, new byte[251 * unit]));
        Assert.Throws<InvalidDataException>(() => DatFile.Parse(units, new byte[924 * unit]));
        Assert.Equal(252, DatFile.Parse(units, new byte[252 * unit]).Count);
        Assert.Equal(912, DatFile.Parse(units, new byte[912 * unit]).Count);

        var orders = DatFormats.Of(DatId.Orders);
        int order = orders.ExpandedEntrySize!.Value;
        Assert.Equal(255, DatFile.Parse(orders, new byte[255 * order]).Count);
        Assert.Throws<InvalidDataException>(() => DatFile.Parse(orders, new byte[256 * order]));
        Assert.Throws<InvalidDataException>(() => DatFile.Parse(orders, Array.Empty<byte>()));
    }

    [Fact]
    public void WithIsImmutableAndChecksRange()
    {
        var a = Zeros(DatId.Units);
        var b = a.With(0, "armor", 3);
        Assert.Equal(0UL, a.Get(0, "armor"));
        Assert.Equal(3UL, b.Get(0, "armor"));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.With(0, "armor", 256));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.With(228, "armor", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.With(5, "infestation", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Zeros(DatId.Sprites).With(5, "health_bar", 1));
        Assert.False(b.SameAs(a));
        Assert.True(a.With(0, "armor", 0).SameAs(a));
        Assert.True(b.EntrySameAs(1, a));
        Assert.False(b.EntrySameAs(0, a));
    }

    [Fact]
    public void SaveWritesAnEditedValueAtItsPlace()
    {
        var weapons = DatFormats.Of(DatId.Weapons);
        int at = 0;   // the arrays come one after another: the bytes of every property before damage_amount
        for (int i = 0; i < weapons.IndexOf("damage_amount"); i++) at += weapons.Entries * DatTypes.Bytes(weapons.Properties[i].Type);
        var saved = Zeros(DatId.Weapons).With(2, "damage_amount", 0x1234).Save();
        Assert.Equal(0x1234, BitConverter.ToUInt16(saved, at + 2 * 2));
        Assert.Equal(weapons.VanillaSize, saved.Length);
    }

    [Fact]
    public void WithEntryFromCopiesEveryCoveredProperty()
    {
        var a = Zeros(DatId.Units);
        var source = a.With(1, "armor", 3).With(1, "mineral_cost", 50).With(1, "hit_points", 10240)
            .With(106, "infestation", 130).With(2, "armor", 9);
        var copied = a.WithEntryFrom(1, source);
        Assert.True(copied.EntrySameAs(1, source));
        Assert.Equal(0UL, copied.Get(2, "armor"));
        Assert.Null(copied.Get(1, "infestation"));
        Assert.Equal(130UL, a.WithEntryFrom(106, source).Get(106, "infestation"));
        Assert.Null(a.WithEntryFrom(106, source).Get(106, "ready_sound"));
        Assert.False(copied.SameAs(source));

        var sprites = Zeros(DatId.Sprites);
        var edited = sprites.With(130, "health_bar", 19).With(5, "image", 7);
        Assert.Equal(19UL, sprites.WithEntryFrom(130, edited).Get(130, "health_bar"));
        Assert.Equal(0UL, sprites.WithEntryFrom(130, edited).Get(5, "image"));
        Assert.Equal(7UL, sprites.WithEntryFrom(5, edited).Get(5, "image"));
        Assert.Throws<ArgumentException>(() => sprites.WithEntryFrom(5, Zeros(DatId.Flingy)));
    }

    [Fact]
    public void EntrySameAsCountsAMissingValueAsDifferent()
    {
        var vanilla = Zeros(DatId.Sprites);
        var expanded = Expanded(DatId.Sprites, 600);
        Assert.False(vanilla.EntrySameAs(5, expanded));    // health_bar: no value vs 0
        Assert.True(vanilla.EntrySameAs(200, expanded));
        Assert.False(expanded.EntrySameAs(550, vanilla));  // vanilla has no entry 550
    }
}
