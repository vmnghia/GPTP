using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App;

/// <summary>What the user chose to repack: the ticked files, and the plugin (null: leave the exe's).</summary>
sealed record RepackChoice(IReadOnlyList<(string Path, byte[] Data)> Files, byte[]? Plugin);

/// <summary>
/// The Repack dialog (Repack spec §6): the plugin against the exe's, the to-repack folder's
/// files with their state, both paths with Change... and Default. It decides nothing:
/// Manifold.Core's Repack scans and compares, and the caller runs the repack.
/// </summary>
sealed class RepackDialog(XamlRoot root, nint windowHandle, string exePath, IArchive exe,
    Func<EditorSettings> settings, Action<EditorSettings> saveSettings)
{
    readonly StackPanel body = new() { Spacing = 8, MinWidth = 560 };
    readonly List<(RepackFile File, CheckBox Box)> rows = new();
    ContentDialog? dialog;
    byte[] exeBytes = [];
    byte[]? plugin;

    /// <summary>Null when cancelled. Throws when the exe can't be read.</summary>
    public async Task<RepackChoice?> ShowAsync()
    {
        exeBytes = File.ReadAllBytes(exePath);
        dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = $"Repack {Path.GetFileName(exePath)}",
            Content = new ScrollViewer { Content = body, MaxHeight = 560 },
            PrimaryButtonText = "Repack",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        Fill();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var files = rows.Where(r => r.Box.IsChecked == true).Select(r => (r.File.MpqPath, r.File.Data)).ToList();
        return new RepackChoice(files, plugin);
    }

    static TextBlock Text(string text, double size = 13, double opacity = 1) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, Opacity = opacity, IsTextSelectionEnabled = true };

    void Fill()
    {
        body.Children.Clear();
        rows.Clear();
        var s = settings();

        string pluginPath = s.PluginPathFor(exePath);
        body.Children.Add(new TextBlock { Text = "Plugin", FontWeight = FontWeights.SemiBold });
        body.Children.Add(Text(PluginLine(pluginPath)));
        body.Children.Add(PathButtons(s.IsDefaultPluginPath(exePath),
            async () =>
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".qdp");
                picker.FileTypeFilter.Add(".dll");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
                if (await picker.PickSingleFileAsync() is { } file) Change(settings().WithPluginPath(exePath, file.Path));
            },
            () => Change(settings().WithPluginPath(exePath, null))));

        string folder = s.RepackFolderFor(exePath);
        body.Children.Add(new TextBlock { Text = "To-repack folder", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        body.Children.Add(Text(folder));
        body.Children.Add(PathButtons(s.IsDefaultRepackFolder(exePath),
            async () =>
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
                if (await picker.PickSingleFolderAsync() is { } picked) Change(settings().WithRepackFolder(exePath, picked.Path));
            },
            () => Change(settings().WithRepackFolder(exePath, null))));

        IReadOnlyList<RepackFile> files;
        try
        {
            files = Repack.Scan(folder, exe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            body.Children.Add(Text($"The folder can't be read: {e.Message}"));
            files = [];
        }
        if (!Directory.Exists(folder)) body.Children.Add(Text("The folder doesn't exist: no files to add.", opacity: 0.75));
        else if (files.Count == 0) body.Children.Add(Text("No files in its subfolders (files at its top and *.bak are left out).", opacity: 0.75));
        foreach (var file in files)
        {
            string state = file.State switch
            {
                RepackState.New => "new",
                RepackState.Changed => "changed",
                _ => "same as the exe's",
            };
            var label = new StackPanel { Children = { Text($"{file.MpqPath}   {file.Data.Length:N0} bytes, {state}") } };
            if (file.Warning is not null)
                label.Children.Add(new TextBlock
                {
                    Text = $"Differs from the exe's: {file.Warning}", FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.OrangeRed),
                });
            var box = new CheckBox { Content = label, IsChecked = file.TickedByDefault };
            box.Click += (_, _) => UpdateButton();
            rows.Add((file, box));
            body.Children.Add(box);
        }
        UpdateButton();
    }

    /// <summary>The plugin against the exe's; sets <see cref="plugin"/> when it would be packed.</summary>
    string PluginLine(string pluginPath)
    {
        plugin = null;
        FireGraftExe? layout;
        try { layout = FireGraftExe.Parse(exeBytes); }
        catch (InvalidDataException e) { return $"Not packed: the exe's plugin header isn't understood ({e.Message})."; }
        if (layout is null) return "Not packed: the exe has no FireGraft plugin header, so Repack adds files only.";
        var (bytes, problem) = Repack.LoadPlugin(pluginPath);
        if (bytes is null) return $"Not packed: {problem}.";
        if (bytes.AsSpan().SequenceEqual(exeBytes.AsSpan(layout.PluginOffset, layout.PluginSize)))
            return $"{pluginPath}\nThe same as the exe's: not packed again.";
        plugin = bytes;
        return $"{pluginPath}\n{bytes.Length:N0} bytes, built {File.GetLastWriteTime(pluginPath):yyyy-MM-dd HH:mm}: replaces the exe's ({layout.PluginSize:N0} bytes).";
    }

    static StackPanel PathButtons(bool isDefault, Func<Task> change, Action reset)
    {
        var changeButton = new Button { Content = "Change..." };
        changeButton.Click += async (_, _) => await change();
        var defaultButton = new Button { Content = "Default", IsEnabled = !isDefault };
        defaultButton.Click += (_, _) => reset();
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { changeButton, defaultButton } };
    }

    void Change(EditorSettings changed)
    {
        saveSettings(changed);
        Fill();
    }

    void UpdateButton()
    {
        if (dialog is not null) dialog.IsPrimaryButtonEnabled = plugin is not null || rows.Any(r => r.Box.IsChecked == true);
    }
}
