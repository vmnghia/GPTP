using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public sealed class WindowSettingsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("window-settings-").FullName;
    string SettingsFile => Path.Combine(root, "settings.json");

    public void Dispose() => Directory.Delete(root, recursive: true);

    static readonly WindowRect Screen = new(0, 0, 1920, 1040);

    [Fact]
    public void Window_page_and_set_round_trip()
    {
        new EditorSettings { Window = new WindowRect(10, 20, 1200, 800), WindowMaximized = true, LastPage = "strings", LastSetId = 131 }
            .Save(SettingsFile);
        var loaded = EditorSettings.Load(SettingsFile);
        Assert.Equal(new WindowRect(10, 20, 1200, 800), loaded.Window);
        Assert.True(loaded.WindowMaximized);
        Assert.Equal("strings", loaded.LastPage);
        Assert.Equal(131, loaded.LastSetId);

        File.WriteAllText(SettingsFile, """{ "LastExe": "C:\\x.exe" }""");
        var old = EditorSettings.Load(SettingsFile);
        Assert.Null(old.Window);
        Assert.False(old.WindowMaximized);
        Assert.Null(old.LastPage);
        Assert.Null(old.LastSetId);
    }

    [Fact]
    public void A_window_on_a_display_restores() =>
        Assert.Equal(new WindowRect(100, 100, 800, 600), WindowPlacement.Restore(new(100, 100, 800, 600), [Screen]));

    [Fact]
    public void A_window_mostly_off_but_grabbable_restores() =>
        Assert.NotNull(WindowPlacement.Restore(new(1800, 900, 800, 600), [Screen]));   // 120 x 140 on screen

    [Fact]
    public void A_window_on_a_second_display_restores() =>
        Assert.NotNull(WindowPlacement.Restore(new(2000, 100, 800, 600), [Screen, new(1920, 0, 1920, 1040)]));

    [Fact]
    public void A_window_on_a_display_that_is_gone_does_not()
    {
        Assert.Null(WindowPlacement.Restore(new(2000, 100, 800, 600), [Screen]));
        Assert.Null(WindowPlacement.Restore(new(1850, 100, 800, 600), [Screen]));   // 70 wide on screen
        Assert.Null(WindowPlacement.Restore(new(100, 1000, 800, 600), [Screen]));   // 40 high on screen
        Assert.Null(WindowPlacement.Restore(null, [Screen]));
    }
}
