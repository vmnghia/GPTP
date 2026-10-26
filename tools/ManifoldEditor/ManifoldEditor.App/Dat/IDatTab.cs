using ManifoldEditor.App.Services;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManifoldEditor.App.Dat;

/// <summary>
/// One tab of the Data page (a PyDAT tab, or a units sub-tab). The page creates it once,
/// shows its <see cref="Root"/> in a ScrollViewer, and calls <see cref="Show"/> whenever
/// the entry or the file changes (after every edit, undo and redo too).
/// </summary>
interface IDatTab
{
    DatTabId Id { get; }
    UIElement Root { get; }

    /// <summary>Loads every field from the current file's <paramref name="entry"/>.</summary>
    void Show(int entry);

    /// <summary>
    /// Commits what a field holds but has not committed yet (a box still focused), before
    /// the page shows another entry, file or tab, and before Save, Undo and Redo.
    /// </summary>
    void CommitPending() { }
}

/// <summary>
/// The base of a hand-built tab. The <see cref="DatFields"/> helpers register a loader
/// (and, for the boxes, a commit) with it as they build each field, so <see cref="Show"/>
/// loads them all and <see cref="CommitPending"/> commits them all. A tab builds its layout
/// in its constructor into <see cref="Panel"/> (or overrides <see cref="Root"/>).
/// </summary>
abstract class DatTabBase : IDatTab
{
    readonly List<Action<int>> loaders = new();
    readonly List<Action> commits = new();

    protected DatTabBase(DatTabId id, DatContext context)
    {
        Id = id;
        Context = context;
    }

    public DatTabId Id { get; }
    public DatContext Context { get; }
    /// <summary>The file the tab edits.</summary>
    public DatId File => DatTabs.Of(Id).File;
    /// <summary>The entry shown; -1 before the first <see cref="Show"/>.</summary>
    public int Entry { get; private set; } = -1;

    /// <summary>The tab's content, stacked top to bottom (PyDAT's groups).</summary>
    protected StackPanel Panel { get; } = new() { Spacing = 10, Padding = new Thickness(0, 4, 12, 12) };
    public virtual UIElement Root => Panel;

    /// <summary>A property of this tab, checked against <see cref="DatTabs"/> (throws for one the tab doesn't show).</summary>
    protected string P(string name) => DatTabs.Property(Id, name);

    /// <summary>Registers what a field does on <see cref="Show"/>: load its value from the entry.</summary>
    public void AddLoader(Action<int> loader) => loaders.Add(loader);

    /// <summary>Registers what a field does on <see cref="CommitPending"/>.</summary>
    public void AddCommit(Action commit) => commits.Add(commit);

    public virtual void Show(int entry)
    {
        Entry = entry;
        foreach (var load in loaders) load(entry);
    }

    public void CommitPending()
    {
        foreach (var commit in commits) commit();
    }
}

/// <summary>
/// What every tab reads and edits through: the open exe's .dat files, their entry names,
/// PyDAT's lists and hints, and the one way to edit (a history step, then the window redraws).
/// </summary>
sealed class DatContext
{
    static readonly DatLists NoLists = DatLists.From(new Dictionary<string, string[]>(), new Dictionary<string, string>());

    readonly Func<DatId, DatNameSetting> nameSetting;
    readonly Action changed;
    readonly Action<DatId, int> jump;
    readonly Action<IReadOnlyList<DatRefMatch>> showUsedBy;
    readonly Func<EditorSettings> settings;
    readonly Action<EditorSettings> setSettings;
    OpenedExe? previewFor;
    DatPreview? preview;
    OpenedExe? iconsFor;
    IconCache? icons;
    SoundPlayer? sounds;
    readonly Dictionary<DatId, (NamesKey Key, IReadOnlyList<string> Names)> names = new();

    /// <summary>
    /// What a file's names are built from: when one of these changes, they are built again.
    /// Compared by reference (an edit makes a new file, table or overrides). Only the file's
    /// own overrides: another file's never name its entries.
    /// </summary>
    readonly record struct NamesKey(EditorState State, DatFile? File, DatFile? Sprites, DatFile? Images,
        StringTable? Strings, DatNameSetting Setting, NameOverrides Overrides);

    /// <param name="opened">The open exe, or null.</param>
    /// <param name="nameSetting">How a file's names are shown (the page's Names and Simple TBL Names, per file).</param>
    /// <param name="changed">Called after an edit: the window redraws.</param>
    /// <param name="jump">Shows a file's tab at an entry (the Jump buttons): no edit.</param>
    /// <param name="showUsedBy">Fills the "Used by" list with these lines (the Check buttons) and opens it.</param>
    /// <param name="settings">The window's settings now (the preview checkboxes).</param>
    /// <param name="setSettings">Replaces the window's settings.</param>
    public DatContext(Func<OpenedExe?> opened, Func<DatId, DatNameSetting> nameSetting, Action changed,
        Action<DatId, int> jump, Action<IReadOnlyList<DatRefMatch>> showUsedBy,
        Func<EditorSettings> settings, Action<EditorSettings> setSettings)
    {
        this.settings = settings;
        this.setSettings = setSettings;
        Opened = opened;
        this.nameSetting = nameSetting;
        this.changed = changed;
        this.jump = jump;
        this.showUsedBy = showUsedBy;
    }

    public Func<OpenedExe?> Opened { get; }

    /// <summary>The window's settings now.</summary>
    public EditorSettings Settings => settings();

    /// <summary>Replaces the window's settings (kept in memory; the window writes them to disk).</summary>
    public void SetSettings(EditorSettings changed) => setSettings(changed);

    /// <summary>
    /// The open exe's cmdicons.grp frames as bitmaps (the button page's cache, a fresh one per open
    /// exe); null when nothing is open.
    /// </summary>
    public IconCache? Icons
    {
        get
        {
            if (Opened() is not { } exe) return null;
            if (!ReferenceEquals(exe, iconsFor))
            {
                icons = new IconCache(exe.Resources);
                iconsFor = exe;
            }
            return icons;
        }
    }

    /// <summary>The Sound Effects tab's player, made on first use (one for the page).</summary>
    public SoundPlayer Sounds => sounds ??= new SoundPlayer();

    /// <summary>images.tbl, which the previews find GRPs through; null when nothing is open or it wasn't found.</summary>
    public StringTable? ImagesTbl => Opened()?.Resources.ImagesTbl;

    /// <summary>
    /// The previews' drawing (previews spec §2), reading GRPs from the open exe, then vanilla. One
    /// per open exe, so its frames are cached until another exe is opened; null when nothing is open.
    /// </summary>
    public DatPreview? Preview
    {
        get
        {
            if (Opened() is not { } exe) return null;
            if (!ReferenceEquals(exe, previewFor))
            {
                var resources = exe.Resources;
                preview = new DatPreview(path => resources.Resolver?.Find(path)?.Data, resources.Palettes);
                previewFor = exe;
            }
            return preview;
        }
    }

    /// <summary>
    /// Shows <paramref name="file"/>'s tab and selects its <paramref name="entry"/> (PyDAT's Jump -&gt; button).
    /// Not an edit: nothing is recorded; a box still being typed in commits first.
    /// </summary>
    public void Jump(DatId file, int entry) => jump(file, entry);

    /// <summary>Fills the page's "Used by" list with <paramref name="matches"/> and opens it (the Images tab's Check buttons).</summary>
    public void ShowUsedBy(IReadOnlyList<DatRefMatch> matches) => showUsedBy(matches);

    /// <summary>The file as edited now; null when nothing is open or the file was found nowhere.</summary>
    public DatFile? File(DatId id) => Opened()?.State.Document.Dat(id);

    /// <summary>PyDAT's lists (Units.txt, DamTypes.txt...) and hints; empty when nothing is open.</summary>
    public DatLists Lists => Opened()?.Lists ?? NoLists;

    /// <summary>The PyMS hint (Hints.txt) for a field key, or null.</summary>
    public string? Hint(string key) => Lists.Hint(key);

    /// <summary>
    /// The names of the file's entries (<see cref="DatNames.Build"/>, with the file's Names
    /// setting), one per entry of the file as edited now. Built again only when the file,
    /// the files its names come from, the strings, the setting or the file's name overrides change. Empty when nothing is open.
    /// </summary>
    public IReadOnlyList<string> Names(DatId id)
    {
        if (Opened() is not { } exe) return [];
        var doc = exe.State.Document;
        var setting = nameSetting(id);
        // A flingy is named by its sprite, a sprite by its image, an image by its grp.
        var key = new NamesKey(exe.State, doc.Dat(id),
            id is DatId.Flingy ? doc.Dat(DatId.Sprites) : null,
            id is DatId.Flingy or DatId.Sprites ? doc.Dat(DatId.Images) : null,
            exe.State.Strings, setting, doc.Overrides(id));
        if (names.TryGetValue(id, out var cached) && cached.Key == key) return cached.Names;
        var r = exe.Resources;
        var sources = new DatNameSources(doc.Dat, exe.State.Strings, r.UnitNames, r.ImagesTbl, r.SfxdataTbl,
            r.PortdataTbl, r.MapdataTbl, exe.Lists, doc.Overrides);
        var built = DatNames.Build(id, sources, setting.Mode, setting.Simple);
        names[id] = (key, built);
        return built;
    }

    /// <summary>
    /// One property of one entry set, as one undo step (<see cref="EditorState.EditDat"/>),
    /// then the window redraws (which shows the tab again). False, and nothing redrawn, when
    /// it changes nothing or is refused.
    /// </summary>
    public bool Edit(DatId file, int entry, string property, ulong value)
    {
        if (Opened() is not { } exe || !exe.State.EditDat(file, entry, property, value)) return false;
        changed();
        return true;
    }
}
