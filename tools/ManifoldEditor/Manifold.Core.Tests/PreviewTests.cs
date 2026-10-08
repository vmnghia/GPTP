using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class PreviewTests
{
    static readonly uint[] Colours = TextColors.Default;
    static uint C(int code) => Colours[code];

    static IReadOnlyList<PreviewLine> Preview(string pytblText, byte? type = null) =>
        TooltipPreview.Build(TblText.Compile(pytblText, out _), type, Colours);

    [Fact]
    public void Colour_codes_colour_the_text_that_follows()
    {
        var line = Assert.Single(Preview("<3>M<1>ove", 0));
        Assert.Equal([new PreviewRun("M", C(3)), new PreviewRun("ove", C(2))], line.Runs);
        Assert.Equal(C(2), C(1));                                   // <1> is drawn as <2>, as in PyMS
        Assert.Equal(0xFFDCDC3Cu, C(3));
    }

    [Fact]
    public void After_grey_or_invisible_colour_codes_are_ignored()
    {
        var line = Assert.Single(Preview("a<5>grey<6>still grey"));
        Assert.Equal([new PreviewRun("a", C(2)), new PreviewRun("greystill grey", C(5))], line.Runs);
        Assert.Equal(0u, Assert.Single(Preview("<11>hidden<3>x")).Runs.Single().Argb);
    }

    [Fact]
    public void Lines_break_at_10_and_12_and_take_their_alignment()
    {
        var lines = Preview("Stim Packs:<10>   Research at Academy<12><19>centred<10><18>right<9>tab");
        Assert.Equal(["Stim Packs:", "   Research at Academy", "centred", "right    tab"],
            lines.Select(l => string.Concat(l.Runs.Select(r => r.Text))));
        Assert.Equal([LineAlign.Left, LineAlign.Left, LineAlign.Center, LineAlign.Right], lines.Select(l => l.Align));
    }

    [Fact]
    public void Text_stops_at_the_first_nul_and_a_type_adds_its_cost_line()
    {
        Assert.Equal("Terran Marine", Assert.Single(Preview("Terran Marine<0>*<0>Ground Units")).Runs.Single().Text);
        var train = Preview("Train <3>M<1>arine", 1);
        Assert.Equal(2, train.Count);
        Assert.Equal(" 200 minerals  100 gas  2 supply", string.Concat(train[1].Runs.Select(r => r.Text)));
        Assert.Equal(3, Preview("Upgrade", 2).Count);
        Assert.Single(Preview("Move", 0));
        Assert.Single(Preview("Odd type", 9));
    }

    [Fact]
    public void Tfontgam_colours_are_pixel_1_of_each_ramp()
    {
        // A 64x3 tfontgam whose pixel (x, y) is palette index x + 64 * y, palette index i = grey i.
        var pixels = Enumerable.Range(0, 64 * 3).Select(i => (byte)i).ToArray();
        var pcx = new Pcx(PcxFile(64, 3, pixels, i => (byte)i));
        Assert.Equal(64, pcx.Width);
        Assert.Equal(pixels, pcx.Pixels);
        var colours = TextColors.FromTfontgam(pcx);
        Assert.Equal(0xFF090909u, colours[3]);                       // row 0, column 1: x = 9
        Assert.Equal(0xFF414141u, colours[8]);                       // row 1, column 0: 1 + 64
        Assert.Equal(colours[2], colours[1]);
        Assert.Equal(0u, colours[10]);                               // not a colour
        Assert.Equal(0u, colours[11]);                               // invisible
        Assert.Throws<InvalidDataException>(() => new Pcx(new byte[1000]));
    }

    /// <summary>A PCX file: header, RLE rows (runs of 2 for every pair of equal pixels), palette.</summary>
    static byte[] PcxFile(int width, int height, byte[] pixels, Func<int, byte> grey)
    {
        var bytes = new List<byte>(new byte[128]);
        bytes[0] = 0x0A; bytes[1] = 5; bytes[2] = 1; bytes[3] = 8;
        bytes[8] = (byte)(width - 1); bytes[10] = (byte)(height - 1);
        bytes[65] = 1; bytes[66] = (byte)width;
        foreach (byte p in pixels)
        {
            if (p >= 0xC0) bytes.Add(0xC1);                         // a run of 1 for values that look like counts
            bytes.Add(p);
        }
        bytes.Add(0x0C);
        for (int i = 0; i < 256; i++) bytes.AddRange([grey(i), grey(i), grey(i)]);
        return bytes.ToArray();
    }
}
