using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Editor;

/// <summary>One thing wrong with the .dat files; <paramref name="Entry"/> and <paramref name="Property"/> are null for a whole file.</summary>
public sealed record DatCheck(DatId File, int? Entry, string? Property, string Text);

/// <summary>
/// What the .dat files get wrong, for the Data page's list: a value that points past the
/// file it points into (the target's count and the property's <see cref="DatProperty.NoneValue"/>
/// name nothing and pass), a sprite's selection circle naming an image that does not exist, and
/// an expanded file the plugin does not read. A file that is not open is skipped, and so is
/// a reference into one that is not open.
/// </summary>
public static class DatChecks
{
    /// <summary>images.dat's first selection circle image (sprites.dat's selection_circle_image is relative to it).</summary>
    const int FirstSelectionCircleImage = 561;

    /// <summary>The checks in file order, then entry order.</summary>
    public static IReadOnlyList<DatCheck> Lines(ButtonSetDocument doc)
    {
        var lines = new List<DatCheck>();
        foreach (var format in DatFormats.All)
        {
            if (doc.Dat(format.Id) is not { } file) continue;
            string name = format.FileName;

            if (file.IsExpanded && !DatFormats.PluginExtends(format.Id))
                lines.Add(new DatCheck(format.Id, null, null,
                    $"{name} is extended ({file.Count} entries): the plugin refuses it"));

            var targets = new List<(DatProperty Property, int Count)>();
            foreach (var property in format.Properties)
                if (property.Target is { } target && doc.Dat(target) is { } targetFile)
                    targets.Add((property, targetFile.Count));
            var images = format.Id == DatId.Sprites ? doc.Dat(DatId.Images) : null;

            for (int entry = 0; entry < file.Count; entry++)
            {
                foreach (var (property, count) in targets)
                    if (file.Get(entry, property.Name) is { } value && value > (ulong)count && value != property.NoneValue)
                        lines.Add(new DatCheck(format.Id, entry, property.Name,
                            $"{name} {entry}, {property.Name}: {value} is past {DatFormats.Of(property.Target!.Value).FileName}'s {count} entries"));

                if (images is not null && file.Get(entry, "selection_circle_image") is { } circle
                    && FirstSelectionCircleImage + (long)circle >= images.Count)
                    lines.Add(new DatCheck(format.Id, entry, "selection_circle_image",
                        $"{name} {entry}, selection_circle_image: names image {FirstSelectionCircleImage + (long)circle}, past {DatFormats.Of(DatId.Images).FileName}'s {images.Count} entries"));
            }
        }
        return lines;
    }
}
