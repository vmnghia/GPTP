using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;
using Manifold.Storm;
using Microsoft.Win32;

namespace ManifoldEditor.App.Services;

/// <summary>
/// An exe opened in the editor: its archive, its sets, and the resources shown with them.
/// The plugin's status line is kept apart, as the markers' recheck replaces it (QoL spec §7).
/// </summary>
/// <param name="Lists">PyDAT's name lists and field hints (Data\pydat), for the Data page.</param>
public sealed record OpenedExe(string ExePath, StormArchive Archive, EditorState State, EditorResources Resources,
    SetCatalog Catalog, IReadOnlyList<string> Status, string PluginLine, DatLists Lists)
{
    /// <summary>The status bar's lines: the plugin first, then what opening found.</summary>
    public IReadOnlyList<string> StatusLines => [PluginLine, .. Status];
}

/// <summary>An exception while opening, with the step it happened in.</summary>
public sealed class OpenStepException(string step, Exception inner)
    : Exception($"{step}: {inner.Message}", inner)
{
    public string Step { get; } = step;
}

/// <summary>Everything the window opens with (spec §5, plan Task 9 step 1).</summary>
public static class Workspace
{
    /// <summary>Runs one step of opening; a failure names the step.</summary>
    static T Step<T>(string step, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (Exception e) when (e is not OpenStepException)
        {
            throw new OpenStepException(step, e);
        }
    }

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

    /// <summary>PyDAT's lists and hints, from Data\pydat beside the editor; what is missing there is empty.</summary>
    public static DatLists LoadDatLists() => DatLists.Load(Path.Combine(AppContext.BaseDirectory, "Data", "pydat"));

    /// <summary>The set list's units: the document's units.dat (as edited), or none (the list is not grouped).</summary>
    public static SetCatalog Catalog(EditorState state) =>
        SetCatalog.Build(state.UnitName, state.Document.Dat(DatId.Units) is { } units ? UnitsDat.Of(units) : null);

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
    /// <param name="pluginPath">The plugin Repack would pack, compared with the exe's in the status.</param>
    public static OpenedExe Open(string exePath, string starCraftDir, string pluginPath)
    {
        var exe = new StormArchive(exePath);
        var vanillaArchives = new List<IArchive>();
        foreach (var mpq in VanillaMpqs)
        {
            string path = Path.Combine(starCraftDir, mpq);
            if (File.Exists(path)) vanillaArchives.Add(new StormArchive(path));
        }
        var archives = new List<IArchive> { exe };
        archives.AddRange(vanillaArchives);
        string palettePath = Path.Combine(AppContext.BaseDirectory, "Data", "Icons.pal");
        uint[]? iconPalette = File.Exists(palettePath) ? RawPalette.Read(File.ReadAllBytes(palettePath)) : null;
        var resources = Step("Reading stat_txt.tbl, cmdicons.grp, the .tbl files and iscript.bin",
            () => EditorResources.Load(new ResourceResolver(archives), iconPalette));
        // Each .dat file from the exe first; vanilla's copies from the MPQs alone (Revert to vanilla, the • marks).
        var datSources = new List<string>();
        var datNotes = new List<string>();
        var dats = Step("Reading the .dat files (arr\\*.dat)",
            () => DatSources.Load(new ResourceResolver(archives), new ResourceResolver(vanillaArchives), datSources, datNotes));
        var lists = Step("Reading Data\\pydat", LoadDatLists);

        string starCraftExe = Path.Combine(starCraftDir, "StarCraft.exe");
        if (!File.Exists(starCraftExe))
            throw new FileNotFoundException($"StarCraft.exe was not found in {starCraftDir}. Set the StarCraft folder first.");
        var vanilla = Step($"Reading the vanilla button sets from {starCraftExe}",
            () => VanillaSets.Read(new PeImage(File.ReadAllBytes(starCraftExe))));

        var conditions = Step("Reading Data\\FireGraftConFunc.txt", () => LoadTable("FireGraftConFunc.txt"));
        var actions = Step("Reading Data\\FireGraftActFunc.txt", () => LoadTable("FireGraftActFunc.txt"));
        string fgp = Step("Looking for the FireGraft project (Firegraft\\*.fgp)",
            () => EditorSession.FindFgp(exe, Path.GetFileName(exePath)));
        var opened = Step($"Reading the button sets in {Path.GetFileName(exePath)} ({ButtonSetFile.ArchivePath} or {fgp})",
            () => EditorSession.Open(exe, fgp, vanilla, conditions, actions, resources.Strings?.Table, dats: dats));

        var state = new EditorState(opened.Document, Path.GetFileName(exePath), conditions, actions);
        string plugin;
        try { plugin = Repack.PluginStatus(File.ReadAllBytes(exePath), exePath, pluginPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { plugin = $"Plugin: {e.Message}"; }
        var status = opened.Status.Concat(resources.Sources).Concat(datSources).Concat(resources.Notes).Concat(datNotes).ToArray();
        return new OpenedExe(exePath, exe, state, resources, Catalog(state), status, plugin, lists);
    }
}
