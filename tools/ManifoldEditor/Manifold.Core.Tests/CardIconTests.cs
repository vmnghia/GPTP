using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class CardIconTests
{
    static uint[] Frame(int w, int h, IconBox drawn)
    {
        var px = new uint[w * h];
        for (int y = drawn.Y; y < drawn.Y + drawn.Height; y++)
            for (int x = drawn.X; x < drawn.X + drawn.Width; x++) px[y * w + x] = 0xFF00FF00;
        return px;
    }

    [Fact]
    public void Bounds_of_the_drawn_pixels()
    {
        Assert.Equal(new IconBox(3, 2, 20, 18), CardLayout.IconBounds(Frame(36, 34, new(3, 2, 20, 18)), 36, 34));
        Assert.Null(CardLayout.IconBounds(new uint[36 * 34], 36, 34));
    }

    [Fact]
    public void A_full_frame_sits_where_uniform_scaling_put_it()
    {
        var (left, top, scale) = CardLayout.IconOffset(new IconBox(0, 0, 36, 34), 36, 34, 52);
        Assert.Equal(52.0 / 36, scale, 6);
        Assert.Equal(0, left, 6);
        Assert.Equal((52 - 34 * scale) / 2, top, 6);
    }

    [Fact]
    public void An_off_centre_icon_moves_to_the_middle()
    {
        var (left, top, scale) = CardLayout.IconOffset(new IconBox(0, 0, 20, 20), 36, 34, 52);
        Assert.Equal(26 - 10 * scale, left, 6);
        Assert.Equal(26 - 10 * scale, top, 6);
    }

    [Fact]
    public void An_empty_frame_is_centred_as_a_whole()
    {
        var (left, top, scale) = CardLayout.IconOffset(null, 36, 34, 52);
        Assert.Equal(0, left, 6);
        Assert.Equal((52 - 34 * scale) / 2, top, 6);
    }
}
