using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ManifoldEditor.App.Services;

/// <summary>cmdicons.grp frames as bitmaps, made once each.</summary>
public sealed class IconCache(EditorResources resources)
{
    readonly Dictionary<int, WriteableBitmap?> bitmaps = new();
    readonly Dictionary<int, IconBox?> bounds = new();

    public int Count => resources.Icons?.FrameCount ?? 0;

    /// <summary>The icon, or null when the icons or the palette are missing.</summary>
    public WriteableBitmap? Get(int frame)
    {
        if (bitmaps.TryGetValue(frame, out var cached)) return cached;
        WriteableBitmap? bitmap = null;
        if (resources.IconPixels(frame) is { } argb && resources.Icons is { } grp)
        {
            bitmap = new WriteableBitmap(grp.Width, grp.Height);
            // Little-endian ARGB words are B, G, R, A bytes: the bitmap's BGRA. Alpha is 0 or
            // 255, so premultiplying changes nothing.
            byte[] bgra = MemoryMarshal.AsBytes(argb.AsSpan()).ToArray();
            using (var stream = bitmap.PixelBuffer.AsStream())
                stream.Write(bgra, 0, bgra.Length);
            bitmap.Invalidate();
        }
        bitmaps[frame] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// The icon in a square, its drawn pixels in the middle (QoL spec §2), or null when it
    /// can't be drawn.
    /// </summary>
    public FrameworkElement? View(int frame, double square)
    {
        if (Get(frame) is not { } bitmap || resources.Icons is not { } grp) return null;
        if (!bounds.TryGetValue(frame, out var box))
            bounds[frame] = box = resources.IconPixels(frame) is { } argb ? CardLayout.IconBounds(argb, grp.Width, grp.Height) : null;
        var (left, top, scale) = CardLayout.IconOffset(box, grp.Width, grp.Height, square);
        var image = new Image { Source = bitmap, Stretch = Stretch.Fill, Width = grp.Width * scale, Height = grp.Height * scale };
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        return new Canvas { Width = square, Height = square, Children = { image } };
    }
}
