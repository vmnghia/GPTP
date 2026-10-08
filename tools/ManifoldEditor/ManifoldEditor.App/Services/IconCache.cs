using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ManifoldEditor.App.Services;

/// <summary>cmdicons.grp frames as bitmaps, made once each.</summary>
public sealed class IconCache(EditorResources resources)
{
    readonly Dictionary<int, WriteableBitmap?> bitmaps = new();

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
}
