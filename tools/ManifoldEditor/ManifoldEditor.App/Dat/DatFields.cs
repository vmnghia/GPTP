using System.Globalization;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ManifoldEditor.App.Dat;

/// <summary>
/// The Data page's fields (spec §6), one helper per kind. Each builds its element, checks its
/// property against the tab (<see cref="DatTabs.Property"/>: a name the tab doesn't show
/// throws, and the page shows the error in place of the tab), and registers with the tab a
/// loader that fills it from the shown entry on every <see cref="DatTabBase.Show"/>, and for
/// the boxes a commit for <see cref="DatTabBase.CommitPending"/>.
/// <para>
/// Every field: disabled and empty when its property doesn't cover the entry (vanilla
/// layout); its tooltip is PyMS's hint, "Label:\n(hint)", when <c>hintKey</c> names one in
/// Hints.txt (null: none). A box commits what was typed in it when it loses focus, on Enter
/// or on the tab's CommitPending, never per key (a box nobody typed in commits nothing); a
/// value equal to the current one records nothing; one outside the type is refused: the box
/// turns red with the range ("0-255") as its tooltip, and nothing is recorded. An edit is one
/// undo step (<see cref="DatContext.Edit"/>), after which the window redraws and the tab is
/// shown again. Labels are given without the colon; the field adds it.
/// </para>
/// </summary>
static class DatFields
{
    /// <summary>The least width of a field's label, so a group's boxes line up (PyDAT's width=9 labels).</summary>
    public const double LabelWidth = 96;

    // -------- Fields --------

    /// <summary>
    /// A number box (any type: byte, short, long), limited to the property's type in the
    /// file's layout.
    /// </summary>
    /// <param name="width">The box's width in pixels.</param>
    public static FrameworkElement NumberField(DatTabBase tab, DatId file, string property, string label, string? hintKey,
        double width = 80)
    {
        Check(tab, file, property);
        int shown = -1;
        TextBox box = null!;
        box = Box(tab, width, () =>
        {
            if (Current(tab, file, shown, property) is not { } dat) return;
            var type = TypeOf(dat, property);
            if (DatValues.ParseNumber(box.Text, type) is not { } value) { Refuse(box, Range(DatTypes.Max(type))); return; }
            Accept(box);
            if (value != dat.Get(shown, property)) tab.Context.Edit(file, shown, property, value);
        });
        tab.AddLoader(entry =>
        {
            shown = entry;
            Accept(box);
            var value = ValueOf(tab.Context, file, entry, property);
            box.IsEnabled = value is not null;
            Load(box, value?.ToString(CultureInfo.InvariantCulture) ?? "");
        });
        return Line(tab, label, hintKey, box);
    }

    /// <summary>
    /// A reference (PyDAT's id box = dropdown): the id box, then an <see cref="EntryBox"/> of
    /// <paramref name="names"/>. Picking an entry sets the value; typing an id selects the
    /// entry; an id past the list's end is kept and shown as "#300 (no name)".
    /// </summary>
    /// <param name="names">The target's names, asked on every load (<see cref="DatContext.Names"/>, an iscript or TBL list...).</param>
    /// <param name="noneName">PyDAT's extra last entry, when it has one: a units ground weapon of 130 (weapons' count) is "None".</param>
    /// <param name="boxWidth">The dropdown's width.</param>
    /// <remarks>
    /// The id the "None" entry writes and that shows as "None" (with <paramref name="noneName"/>) is the
    /// property's <see cref="DatProperty.NoneValue"/> when it has one (a units portrait's 65535, as in PyDAT;
    /// the checks read the same), the list's count otherwise, where the entry sits. With a none value the
    /// count itself shows as an id without a name.
    /// </remarks>
    public static FrameworkElement ReferenceField(DatTabBase tab, DatId file, string property, string label, string? hintKey,
        Func<IReadOnlyList<string>> names, string? noneName = null, double boxWidth = 260)
    {
        Check(tab, file, property);
        ulong? noneValue = DatFormats.Of(file)[property].NoneValue;
        int shown = -1;
        var entryBox = new EntryBox(boxWidth);
        IReadOnlyList<string>? lastNames = null, withNone = null;
        IReadOnlyList<string> List()
        {
            var list = names();
            if (noneName is null) return list;
            if (!ReferenceEquals(list, lastNames)) { lastNames = list; withNone = [.. list, noneName]; }
            return withNone!;
        }
        TextBox idBox = null!;
        // The "None" entry is the list's last; its id is noneValue when there is one.
        bool HasNoneValue() => noneName is not null && noneValue is not null;
        void ShowValue(ulong value)
        {
            var list = List();
            if (!HasNoneValue()) { entryBox.Show(list, (long)value); return; }
            int last = list.Count - 1;
            if (value == noneValue) entryBox.Show(list, last);
            else if (value == (ulong)last) entryBox.Show(list, (long)value, $"#{value} (no name)");
            else entryBox.Show(list, (long)value);
        }
        void Set(ulong value)
        {
            if (Current(tab, file, shown, property) is not { } dat) return;
            ulong max = DatTypes.Max(TypeOf(dat, property));
            if (value > max)
            {
                Refuse(idBox, Range(max));
                ShowValue(dat.Get(shown, property)!.Value);
                return;
            }
            Accept(idBox);
            if (value != dat.Get(shown, property)) tab.Context.Edit(file, shown, property, value);
        }
        idBox = Box(tab, 64, () =>
        {
            if (Current(tab, file, shown, property) is not { } dat) return;
            ulong max = DatTypes.Max(TypeOf(dat, property));
            if (DatValues.ParseNumber(idBox.Text, TypeOf(dat, property)) is { } value) Set(value);
            else Refuse(idBox, Range(max));
        });
        entryBox.Picked += index => Set(HasNoneValue() && index == List().Count - 1 ? noneValue!.Value : (ulong)index);
        tab.AddLoader(entry =>
        {
            shown = entry;
            Accept(idBox);
            var value = ValueOf(tab.Context, file, entry, property);
            idBox.IsEnabled = entryBox.Root.IsEnabled = value is not null;
            Load(idBox, value?.ToString(CultureInfo.InvariantCulture) ?? "");
            if (value is { } v) ShowValue(v);
            else entryBox.Clear();
        });
        return Line(tab, label, hintKey, idBox, EqualsSign(), entryBox.Root);
    }

    /// <summary>A fixed list (PyDAT's DropDown of a PyMS list): a ComboBox of the list named (DatLists, e.g. "DamTypes"), plus the id box.</summary>
    /// <param name="list">The list's name in <see cref="DatLists.Names"/>, without ".txt".</param>
    public static FrameworkElement ChoiceField(DatTabBase tab, DatId file, string property, string label, string? hintKey,
        string list, double width = 200) =>
        ChoiceField(tab, file, property, label, hintKey, () => tab.Context.Lists[list], width);

    /// <summary>
    /// A fixed list from items the tab gives (when PyDAT's aren't a PyMS list file): a ComboBox
    /// plus the id box. An id past the list's end shows "#9 (no name)" with nothing selected.
    /// </summary>
    public static FrameworkElement ChoiceField(DatTabBase tab, DatId file, string property, string label, string? hintKey,
        Func<IReadOnlyList<string>> items, double width = 200)
    {
        Check(tab, file, property);
        int shown = -1;
        bool loading = false;
        IReadOnlyList<string>? filled = null;
        var combo = new ComboBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
        TextBox idBox = null!;
        void Set(ulong value)
        {
            if (Current(tab, file, shown, property) is not { } dat) return;
            if (value != dat.Get(shown, property)) tab.Context.Edit(file, shown, property, value);
        }
        idBox = Box(tab, 64, () =>
        {
            if (Current(tab, file, shown, property) is not { } dat) return;
            var type = TypeOf(dat, property);
            if (DatValues.ParseNumber(idBox.Text, type) is not { } value) { Refuse(idBox, Range(DatTypes.Max(type))); return; }
            Accept(idBox);
            Set(value);
        });
        combo.SelectionChanged += (_, _) =>
        {
            if (loading || combo.SelectedIndex < 0) return;
            Set((ulong)combo.SelectedIndex);
        };
        tab.AddLoader(entry =>
        {
            shown = entry;
            loading = true;
            try
            {
                var names = items();
                if (!ReferenceEquals(names, filled))
                {
                    filled = names;
                    combo.Items.Clear();
                    foreach (string name in names) combo.Items.Add(name);
                }
                Accept(idBox);
                var value = ValueOf(tab.Context, file, entry, property);
                idBox.IsEnabled = combo.IsEnabled = value is not null;
                Load(idBox, value?.ToString(CultureInfo.InvariantCulture) ?? "");
                bool listed = value is { } v && v < (ulong)names.Count;
                combo.SelectedIndex = listed ? (int)value!.Value : -1;
                combo.PlaceholderText = value is { } past && !listed ? $"#{past} (no name)" : "";
            }
            finally { loading = false; }
        });
        return Line(tab, label, hintKey, idBox, EqualsSign(), combo);
    }

    /// <summary>
    /// A dropdown whose entries stand for fixed values of the property rather than for their own
    /// index (PyDAT's shield size on the Images tab: None, Small, Medium, Large write 0, 133, 2, 184).
    /// No id box: the property's own field is elsewhere. A value none of them stands for selects
    /// nothing and shows "(other)". An empty <paramref name="label"/> keeps the label's room, so the
    /// dropdown lines up with the fields above it.
    /// </summary>
    /// <param name="items">The entries (cache the list: it is refilled when the reference changes).</param>
    /// <param name="values">The value each entry writes, by position.</param>
    public static FrameworkElement MappedChoiceField(DatTabBase tab, DatId file, string property, string label, string? hintKey,
        Func<IReadOnlyList<string>> items, IReadOnlyList<ulong> values, double width = 120)
    {
        Check(tab, file, property);
        int shown = -1;
        bool loading = false;
        IReadOnlyList<string>? filled = null;
        var combo = new ComboBox { Width = width, VerticalAlignment = VerticalAlignment.Center };
        combo.SelectionChanged += (_, _) =>
        {
            if (loading || combo.SelectedIndex < 0 || combo.SelectedIndex >= values.Count) return;
            if (Current(tab, file, shown, property) is not { } dat) return;
            ulong value = values[combo.SelectedIndex];
            if (value != dat.Get(shown, property)) tab.Context.Edit(file, shown, property, value);
        };
        tab.AddLoader(entry =>
        {
            shown = entry;
            loading = true;
            try
            {
                var names = items();
                if (!ReferenceEquals(names, filled))
                {
                    filled = names;
                    combo.Items.Clear();
                    foreach (string name in names) combo.Items.Add(name);
                }
                var value = ValueOf(tab.Context, file, entry, property);
                combo.IsEnabled = value is not null;
                int at = -1;
                for (int i = 0; value is { } v && i < values.Count && i < names.Count; i++)
                    if (values[i] == v) { at = i; break; }
                combo.SelectedIndex = at;
                combo.PlaceholderText = value is not null && at < 0 ? "(other)" : "";
            }
            finally { loading = false; }
        });
        if (label.Length > 0) return Line(tab, label, hintKey, combo);
        var line = Line(tab, "", hintKey, new Border { Width = LabelWidth }, combo);
        return line;
    }

    /// <summary>
    /// The strings of a .tbl as a reference list (PyDAT's <c>('None',) + strings</c>): "None" at 0, then
    /// string 1, 2... so an entry's position is its id. Built once per table, and again only when
    /// <paramref name="table"/> answers another one (the cached list the reference fields want).
    /// A string that can't be read is "String #n".
    /// </summary>
    public sealed class TblChoices(Func<StringTable?> table)
    {
        StringTable? from;
        IReadOnlyList<string>? list;

        public IReadOnlyList<string> WithNone()
        {
            var current = table();
            if (list is not null && ReferenceEquals(current, from)) return list;
            var built = new List<string> { "None" };
            for (int i = 1; current is not null && i <= current.Count; i++)
                built.Add(DatNames.TblString(current, (ulong)i) ?? $"String #{i}");
            from = current;
            list = built;
            return built;
        }
    }

    /// <summary>
    /// Flags: a grid of checkboxes, <paramref name="columns"/> wide, each its own bit(s) of the
    /// one property (a click is one step that changes only that bit). A box shows checked
    /// when every bit of its mask is set.
    /// </summary>
    public static FrameworkElement FlagsField(DatTabBase tab, DatId file, string property, (string Label, ulong Mask)[] bits,
        int columns) =>
        FlagsField(tab, file, property, bits.Select(b => (b.Label, b.Mask, (string?)null)).ToArray(), columns);

    /// <summary>Flags with a hint per checkbox (PyDAT tips each one: "UnitSEGroupZerg"...).</summary>
    public static FrameworkElement FlagsField(DatTabBase tab, DatId file, string property,
        (string Label, ulong Mask, string? HintKey)[] bits, int columns)
    {
        Check(tab, file, property);
        int shown = -1;
        var grid = new Grid { ColumnSpacing = 12 };
        columns = Math.Max(1, columns);
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int rows = (bits.Length + columns - 1) / columns;
        for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var boxes = new CheckBox[bits.Length];
        for (int i = 0; i < bits.Length; i++)
        {
            var (label, mask, hintKey) = bits[i];
            var box = new CheckBox { Content = label, MinWidth = 0 };
            Tip(tab, box, label, hintKey);
            // Click, not Checked: only the user's change, not the loader's.
            box.Click += (_, _) =>
            {
                if (Current(tab, file, shown, property) is not { } dat) return;
                ulong value = dat.Get(shown, property)!.Value;
                ulong changed = box.IsChecked == true ? value | mask : value & ~mask;
                if (changed > DatTypes.Max(TypeOf(dat, property))) { box.IsChecked = (value & mask) == mask; return; }
                tab.Context.Edit(file, shown, property, changed);
            };
            Grid.SetRow(box, i / columns);
            Grid.SetColumn(box, i % columns);
            grid.Children.Add(box);
            boxes[i] = box;
        }
        tab.AddLoader(entry =>
        {
            shown = entry;
            var value = ValueOf(tab.Context, file, entry, property);
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].IsEnabled = value is not null;
                boxes[i].IsChecked = value is { } v && (v & bits[i].Mask) == bits[i].Mask;
            }
        });
        return grid;
    }

    /// <summary>A checkbox for a 0/1 byte (shields enabled, BroodWar, the orders.dat booleans). Checked for any value but 0; a click writes 1 or 0.</summary>
    public static FrameworkElement CheckField(DatTabBase tab, DatId file, string property, string label, string? hintKey)
    {
        Check(tab, file, property);
        int shown = -1;
        var box = new CheckBox { Content = label, MinWidth = 0 };
        Tip(tab, box, label, hintKey);
        box.Click += (_, _) =>
        {
            if (Current(tab, file, shown, property) is null) return;
            tab.Context.Edit(file, shown, property, box.IsChecked == true ? 1UL : 0UL);
        };
        tab.AddLoader(entry =>
        {
            shown = entry;
            var value = ValueOf(tab.Context, file, entry, property);
            box.IsEnabled = value is not null;
            box.IsChecked = value is { } v && v != 0;
        });
        return box;
    }

    /// <summary>
    /// Hit points (24.8 fixed point): the whole box, then "Fraction:" and its box (0-255),
    /// committed together as one edit of the one property.
    /// </summary>
    public static FrameworkElement HitPointsField(DatTabBase tab, DatId file, string property, string label, string? hintKey,
        string? fractionHintKey = null)
    {
        Check(tab, file, property);
        int shown = -1;
        TextBox whole = null!, fraction = null!;
        void Commit()
        {
            if (Current(tab, file, shown, property) is not { } dat) return;
            ulong wholeMax = DatTypes.Max(TypeOf(dat, property)) >> 8;
            var w = DatValues.ParseNumber(whole.Text, DatType.Long) is { } pw && pw <= wholeMax ? pw : (ulong?)null;
            var f = DatValues.ParseNumber(fraction.Text, DatType.Byte);
            if (w is null) Refuse(whole, Range(wholeMax)); else Accept(whole);
            if (f is null) Refuse(fraction, Range(255)); else Accept(fraction);
            if (w is null || f is null) return;
            ulong value = DatValues.HitPoints((uint)w.Value, (byte)f.Value);
            if (value != dat.Get(shown, property)) tab.Context.Edit(file, shown, property, value);
        }
        whole = Box(tab, 100, Commit);
        fraction = Box(tab, 44, Commit);
        tab.AddLoader(entry =>
        {
            shown = entry;
            Accept(whole);
            Accept(fraction);
            var value = ValueOf(tab.Context, file, entry, property);
            whole.IsEnabled = fraction.IsEnabled = value is not null;
            var (w, f) = value is { } v ? DatValues.HitPoints(v) : (0u, (byte)0);
            Load(whole, value is null ? "" : w.ToString(CultureInfo.InvariantCulture));
            Load(fraction, value is null ? "" : f.ToString(CultureInfo.InvariantCulture));
        });
        var fractionPart = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        fractionPart.Children.Add(new TextBlock { Text = "Fraction:", VerticalAlignment = VerticalAlignment.Center });
        fractionPart.Children.Add(fraction);
        Tip(tab, fractionPart, "Hit Points Fraction", fractionHintKey);
        return Line(tab, label, hintKey, whole, fractionPart);
    }

    /// <summary>
    /// Supply (counted in halves): the whole box (0-127) and a "+0.5" checkbox, committed
    /// together as one edit of the one property.
    /// </summary>
    public static FrameworkElement SupplyField(DatTabBase tab, DatId file, string property, string label, string? hintKey)
    {
        Check(tab, file, property);
        int shown = -1;
        const ulong WholeMax = 127;   // DatValues.Supply keeps 7 bits of the whole
        TextBox whole = null!;
        var half = new CheckBox { Content = "+0.5", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        void Commit()
        {
            if (Current(tab, file, shown, property) is not { } dat) return;
            ulong current = dat.Get(shown, property)!.Value;
            if (DatValues.ParseNumber(whole.Text, DatType.Byte) is not { } w || w > WholeMax)
            {
                Refuse(whole, Range(WholeMax));
                half.IsChecked = DatValues.Supply(current).Half;
                return;
            }
            Accept(whole);
            ulong value = DatValues.Supply((byte)w, half.IsChecked == true);
            if (value != current) tab.Context.Edit(file, shown, property, value);
        }
        whole = Box(tab, 44, Commit);
        half.Click += (_, _) => Commit();
        tab.AddLoader(entry =>
        {
            shown = entry;
            Accept(whole);
            var value = ValueOf(tab.Context, file, entry, property);
            whole.IsEnabled = half.IsEnabled = value is not null;
            Load(whole, value is { } v ? DatValues.Supply(v).Whole.ToString(CultureInfo.InvariantCulture) : "");
            half.IsChecked = value is { } h && DatValues.Supply(h).Half;
        });
        return Line(tab, label, hintKey, whole, half);
    }

    /// <summary>
    /// One 16-bit part of a size, position or extents value (part 0 is the lowest: a size's
    /// width, an extents' left), 0-65535; an edit changes only that part.
    /// </summary>
    public static FrameworkElement PartField(DatTabBase tab, DatId file, string property, int index, string label, string? hintKey,
        double width = 64)
    {
        Check(tab, file, property);
        int shown = -1;
        TextBox box = null!;
        box = Box(tab, width, () =>
        {
            if (Current(tab, file, shown, property) is not { } dat) return;
            if (DatValues.ParseNumber(box.Text, DatType.Short) is not { } part) { Refuse(box, Range(ushort.MaxValue)); return; }
            Accept(box);
            ulong current = dat.Get(shown, property)!.Value;
            ulong value = DatValues.WithPart(current, index, (ushort)part);
            if (value != current) tab.Context.Edit(file, shown, property, value);
        });
        tab.AddLoader(entry =>
        {
            shown = entry;
            Accept(box);
            var value = ValueOf(tab.Context, file, entry, property);
            box.IsEnabled = value is not null;
            Load(box, value is { } v ? DatValues.Part(v, index).ToString(CultureInfo.InvariantCulture) : "");
        });
        return Line(tab, label, hintKey, box);
    }

    /// <summary>
    /// A derived display (read only): <paramref name="text"/> of the shown entry, worked out
    /// again on every load, e.g. <c>e =&gt; DatFields.Seconds(DatFields.ValueOf(tab.Context, file, e, "build_time"))</c>.
    /// </summary>
    public static TextBlock DerivedText(DatTabBase tab, Func<int, string> text)
    {
        var block = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        tab.AddLoader(entry => block.Text = text(entry));
        return block;
    }

    // -------- Derived formats (PyDAT's) --------

    /// <summary>Frames as seconds at Fastest (24 a second), as PyDAT shows a time: "= 15 secs.", "= 1.0416 secs."; "" for null.</summary>
    public static string Seconds(ulong? frames) =>
        frames is { } f ? $"= {FloatText(DatValues.Seconds(f), 4)} secs." : "";

    /// <summary>A flingy's top speed as PyDAT's FlingyTab shows it (raw × 3 / 320, three decimals): "= 6 pixels/frame" for 640; "" for null.</summary>
    public static string Speed(ulong? raw) =>
        raw is { } r ? $"= {FloatText(r * 3 / 320.0, 3)} pixels/frame" : "";

    /// <summary>A flingy's halt distance in pixels (raw / 256, four decimals): "= 14.0625 pixels" for 3600; "" for null.</summary>
    public static string HaltDistance(ulong? raw) =>
        raw is { } r ? $"= {FloatText(DatValues.Pixels(r), 4)} pixels" : "";

    /// <summary>PyMS's float_to_str: no ".0" on a whole number, the decimals cut (not rounded) after <paramref name="maxDecimals"/>.</summary>
    public static string FloatText(double value, int maxDecimals)
    {
        string s = value.ToString("R", CultureInfo.InvariantCulture);
        int dot = s.IndexOf('.');
        if (dot < 0 || s.Contains('E')) return s;
        return s.Length - dot - 1 > maxDecimals ? s[..(dot + 1 + maxDecimals)] : s;
    }

    // -------- Reading --------

    /// <summary>The property's value on the entry; null when nothing is open, the file is missing, or the property doesn't cover the entry.</summary>
    public static ulong? ValueOf(DatContext context, DatId file, int entry, string property) =>
        context.File(file) is { } dat && dat.Covers(entry, property) ? dat.Get(entry, property) : null;

    // -------- Layout --------

    /// <summary>One of PyDAT's LabelFrames: a titled, framed group of fields stacked top to bottom.</summary>
    public static Border Group(string title, params UIElement[] children)
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        foreach (var child in children) stack.Children.Add(child);
        return new Border
        {
            Child = stack, Padding = new Thickness(10), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
        };
    }

    /// <summary>Elements side by side (groups or fields on one row, as PyDAT packs them LEFT).</summary>
    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    /// <summary>
    /// A field's line: "Label:" (right-aligned, at least <see cref="LabelWidth"/>; none for an
    /// empty label), then the controls; the hint as the line's tooltip.
    /// </summary>
    public static StackPanel Line(DatTabBase tab, string label, string? hintKey, params UIElement[] controls)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (label.Length > 0)
            line.Children.Add(new TextBlock
            {
                Text = label + ":", MinWidth = LabelWidth, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            });
        foreach (var control in controls) line.Children.Add(control);
        Tip(tab, line, label, hintKey);
        return line;
    }

    /// <summary>
    /// A field with PyDAT's tooltip title where it differs from the label ("Armor Upgrade" on the
    /// field labelled "Upgrade"): sets the tooltip to "title:\n(hint)" and returns the element. Build
    /// the field with a null hint key and wrap it: <c>Titled(this, NumberField(..., "Time", null), "Build Time", "UnitTime")</c>.
    /// </summary>
    public static T Titled<T>(DatTabBase tab, T element, string title, string? hintKey) where T : FrameworkElement
    {
        Tip(tab, element, title, hintKey);
        return element;
    }

    /// <summary>PyDAT's tooltip: "Label:\n(hint)"; nothing without a hint.</summary>
    public static void Tip(DatTabBase tab, FrameworkElement element, string label, string? hintKey)
    {
        if (hintKey is not null && tab.Context.Hint(hintKey) is { } hint)
            ToolTipService.SetToolTip(element, label.Length > 0 ? $"{label}:\n{hint}" : hint);
    }

    // -------- Inside --------

    static TextBlock EqualsSign() => new() { Text = "=", VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// A number box that commits on losing focus, on Enter and on the tab's CommitPending, but
    /// only text typed since the last <see cref="Load"/> or the last commit: a box the document
    /// has moved on from (after an undo, before the tab is shown again) puts nothing back, and
    /// text that recorded nothing ("045" over 45, a refused value) does not commit again.
    /// </summary>
    static TextBox Box(DatTabBase tab, double width, Action commit)
    {
        var box = new TextBox { Width = width, MinWidth = 0, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        void CommitTyped()
        {
            if (box.Text == (box.Tag as string ?? "")) return;
            commit();
            // An edit reloaded the box (Text and Tag the canonical value); an equal or refused
            // value left the typed text: either way it is committed now, until the next typing.
            box.Tag = box.Text;
        }
        box.LostFocus += (_, _) => CommitTyped();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            CommitTyped();
        };
        tab.AddCommit(CommitTyped);
        return box;
    }

    /// <summary>A box's text from the entry; what the user types after it is what commits.</summary>
    static void Load(TextBox box, string text)
    {
        box.Tag = text;
        box.Text = text;
    }

    /// <summary>The file, when the field shows an entry its property covers; else null (nothing to commit).</summary>
    static DatFile? Current(DatTabBase tab, DatId file, int shown, string property) =>
        shown >= 0 && tab.Context.File(file) is { } dat && dat.Covers(shown, property) ? dat : null;

    static DatType TypeOf(DatFile dat, string property) => dat.Format[property].TypeIn(dat.IsExpanded);

    static string Range(ulong max) => $"0-{max.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>A refused value: the box red, the range as its tooltip.</summary>
    static void Refuse(TextBox box, string range)
    {
        box.BorderBrush = new SolidColorBrush(Colors.Red);
        ToolTipService.SetToolTip(box, range);
    }

    static void Accept(TextBox box)
    {
        box.ClearValue(Control.BorderBrushProperty);
        ToolTipService.SetToolTip(box, null);
    }

    /// <summary>The field's file is the tab's, and the tab shows the property (else it throws: a misspelt name).</summary>
    static void Check(DatTabBase tab, DatId file, string property)
    {
        if (file != tab.File)
            throw new ArgumentException($"the {DatTabs.Of(tab.Id).Name} tab edits {DatFormats.Of(tab.File).FileName}, not {DatFormats.Of(file).FileName}", nameof(file));
        DatTabs.Property(tab.Id, property);
    }
}
