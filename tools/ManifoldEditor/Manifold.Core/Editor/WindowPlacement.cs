namespace Manifold.Core.Editor;

/// <summary>A window's or a display's rectangle, in screen pixels.</summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height);

/// <summary>The window as it was left (QoL spec §10).</summary>
public static class WindowPlacement
{
    /// <summary>How much of the window must lie on one display to be grabbed and moved.</summary>
    const int MinVisibleWidth = 100, MinVisibleHeight = 50;

    /// <summary>The saved rectangle when enough of it lies on some display; else null (the default placement).</summary>
    public static WindowRect? Restore(WindowRect? saved, IReadOnlyList<WindowRect> workAreas)
    {
        if (saved is not { } w) return null;
        foreach (var area in workAreas)
        {
            int width = Math.Min(w.X + w.Width, area.X + area.Width) - Math.Max(w.X, area.X);
            int height = Math.Min(w.Y + w.Height, area.Y + area.Height) - Math.Max(w.Y, area.Y);
            if (width >= MinVisibleWidth && height >= MinVisibleHeight) return w;
        }
        return null;
    }
}
