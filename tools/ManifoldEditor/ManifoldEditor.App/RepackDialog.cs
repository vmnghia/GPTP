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

/// <summary>Which of the two Repack commands (QoL spec §6).</summary>
enum RepackKind { Plugin, Files }

/// <summary>
/// The Repack dialogs (Repack spec §6, QoL spec §6). Repack plugin: the plugin against the
/// exe's, its path with Change... and Default. Repack files: the to-repack folder (Change...,
/// Default), a filter, Select all, the count, and the files with their state. It decides
/// nothing: Manifold.Core's Repack and RepackSelection do, and the caller runs the repack.
/// </summary>
sealed class RepackDialog(XamlRoot root, nint windowHandle, string exePath, IArchive exe, RepackKind kind,
    Func<EditorSettings> settings, Action<EditorSettings> saveSettings)
{
    readonly StackPanel body = new() { Spacing = 8, MinWidth = 560 };
    readonly TextBox filter = new() { PlaceholderText = "Filter by path" };
    readonly CheckBox selectAll = new() { Content = "Select all" };
    readonly TextBlock count = new() { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>Select all and the count: one panel for the dialog's life, as Fill runs again on a folder change.</summary>
    readonly StackPanel selectRow = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    readonly List<CheckBox> boxes = new();
    RepackSelection selection = new([]);
    ContentDialog? dialog;
    byte[] exeBytes = [];
    byte[]? plugin;

    /// <summary>Null when cancelled. Throws when the exe can't be read.</summary>
    public async Task<RepackChoice?> ShowAsync()
    {
        exeBytes = File.ReadAllBytes(exePath);
        selectRow.Children.Add(selectAll);
        selectRow.Children.Add(count);
        filter.TextChanged += (_, _) => ApplyFilter();
        selectAll.Click += (_, _) =>
        {
            selection.ClickSelectAll();
            for (int i = 0; i < boxes.Count; i++) boxes[i].IsChecked = selection.IsTicked(i);
            UpdateButton();
        };
        string name = Path.GetFileName(exePath);
        dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = kind == RepackKind.Plugin ? $"Repack the plugin into {name}" : $"Repack files into {name}",
            Content = new ScrollViewer { Content = body, MaxHeight = 560 },
            PrimaryButtonText = "Repack",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        Fill();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return kind == RepackKind.Plugin
            ? new RepackChoice([], plugin)
            : new RepackChoice(selection.Ticked.Select(f => (f.MpqPath, f.Data)).ToList(), null);
    }

    static TextBlock Text(string text, double size = 13, double opacity = 1) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, Opacity = opacity, IsTextSelectionEnabled = true };

    void Fill()
    {
        body.Children.Clear();
        if (kind == RepackKind.Plugin) FillPlugin();
        else FillFiles();
        UpdateButton();
    }

    void FillPlugin()
    {
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
    }

    void FillFiles()
    {
        var s = settings();
        string folder = s.RepackFolderFor(exePath);
        body.Children.Add(new TextBlock { Text = "To-repack folder", FontWeight = FontWeights.SemiBold });
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
        selection = new RepackSelection(files);
        boxes.Clear();
        if (files.Count > 0)
        {
            body.Children.Add(filter);
            body.Children.Add(selectRow);
        }
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            int index = i;
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
            var box = new CheckBox { Content = label, IsChecked = selection.IsTicked(index) };
            box.Click += (_, _) =>
            {
                selection.SetTicked(index, box.IsChecked == true);
                UpdateButton();
            };
            boxes.Add(box);
            body.Children.Add(box);
        }
        ApplyFilter();
    }

    /// <summary>Rows whose path holds the filter show; the others are collapsed and keep their ticks.</summary>
    void ApplyFilter()
    {
        selection.Filter = filter.Text;
        var visible = selection.Visible.ToHashSet();
        for (int i = 0; i < boxes.Count; i++) boxes[i].Visibility = visible.Contains(i) ? Visibility.Visible : Visibility.Collapsed;
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
        selectAll.IsChecked = selection.SelectAllState;
        selectAll.IsEnabled = selection.CanSelectAll;
        count.Text = selection.CountText;
        if (dialog is not null)
            dialog.IsPrimaryButtonEnabled = kind == RepackKind.Plugin ? plugin is not null : selection.Ticked.Count > 0;
    }
}
