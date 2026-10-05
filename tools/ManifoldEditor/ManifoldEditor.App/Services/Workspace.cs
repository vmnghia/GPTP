using System.ComponentModel;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;
using Manifold.Storm;
using Microsoft.Win32;

namespace ManifoldEditor.App.Services;

/// <summary>An exe opened in the editor: its archive, its sets, and the resources shown with them.</summary>
public sealed record OpenedExe(StormArchive Archive, EditorState State, EditorResources Resources,
    SetCatalog Catalog, IReadOnlyList<string> Status);

/// <summary>Everything the window opens with (spec §5, plan Task 9 step 1).</summary>
public static class Workspace
{
    const string DefaultStarCraftDir = @"D:\Games\Starcraft 1.16.1";
    static readonly string[] VanillaMpqs = { "patch_rt.mpq", "BrooDat.mpq", "StarDat.mpq" };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ManifoldEditor", "settings.json");

    public static EditorSettings LoadSettings() => EditorSettings.Load(SettingsPath);

    /// <summary>The settings' folder, else the registry's InstallPath, else the default.</summary>
    public static string StarCraftDir(EditorSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.StarCraftDir)) return settings.StarCraftDir;
        try
        {
            // [VERIFY] the key on the user's machine. A 32-bit view, as the game is 32-bit.
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = root.OpenSubKey(@"SOFTWARE\Blizzard Entertainment\Starcraft");
            if (key?.GetValue("InstallPath") is string path && Directory.Exists(path)) return path;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return DefaultStarCraftDir;
    }

    static FunctionTable LoadTable(string name) =>
        FunctionTable.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", name)));

    public static IReadOnlyList<string> IconNames()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "Icons.txt");
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(l => l.Length > 0).ToArray()
            : Array.Empty<string>();
    }

    /// <summary>
    /// Opens a mod exe. Throws (with a message for the user) when the exe has no MPQ or
    /// StarCraft.exe can't be read; nothing is loaded then.
    /// </summary>
    public static OpenedExe Open(string exePath, string starCraftDir)
    {
        var exe = new StormArchive(exePath);
        var archives = new List<IArchive> { exe };
        foreach (var mpq in VanillaMpqs)
        {
            string path = Path.Combine(starCraftDir, mpq);
            if (File.Exists(path)) archives.Add(new StormArchive(path));
        }
        var resources = EditorResources.Load(new ResourceResolver(archives));

        string starCraftExe = Path.Combine(starCraftDir, "StarCraft.exe");
        if (!File.Exists(starCraftExe))
            throw new FileNotFoundException($"StarCraft.exe was not found in {starCraftDir}. Set the StarCraft folder first.");
        var vanilla = VanillaSets.Read(new PeImage(File.ReadAllBytes(starCraftExe)));

        var conditions = LoadTable("FireGraftConFunc.txt");
        var actions = LoadTable("FireGraftActFunc.txt");
        string fgp = $"Firegraft\\{Path.GetFileNameWithoutExtension(exePath)}.fgp";
        EditorSession.Opened opened;
        try
        {
            opened = EditorSession.Open(exe, fgp, vanilla, conditions, actions);
        }
        catch (Win32Exception e)
        {
            throw new InvalidDataException($"{Path.GetFileName(exePath)} has no MPQ that StormLib can open ({e.Message}).", e);
        }

        var state = new EditorState(opened.Document, Path.GetFileName(exePath), resources.Text, conditions, actions);
        var status = opened.Status.Concat(resources.Sources).Concat(resources.Notes).ToArray();
        return new OpenedExe(exe, state, resources, SetCatalog.Build(resources.Text, resources.Units), status);
    }
}
