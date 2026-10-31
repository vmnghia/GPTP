using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// <c>Manifold\requirements.bin</c> (requirements spec §4): a FireGraft project's layout
/// ("FgPa", u32 version 7, "1.16.1", then sections) holding only the five requirement
/// sections, so the plugin's parser reads it as it reads the .fgp.
/// </summary>
public static class RequirementsFile
{
    public const string ArchivePath = "Manifold\\requirements.bin";
    public const uint Version = 7;
    public const string GameVersion = "1.16.1";

    public static byte[] Write(Requirements requirements)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("FgPa"));
        bytes.AddRange(BitConverter.GetBytes(Version));
        bytes.AddRange(Encoding.ASCII.GetBytes(GameVersion + "\0"));
        foreach (var table in ReqTables.All)
        {
            var section = ReqSections.Write(requirements[table]);
            bytes.AddRange(Encoding.ASCII.GetBytes(ReqTables.SectionTag(table)));
            bytes.AddRange(BitConverter.GetBytes((uint)section.Length));
            bytes.AddRange(section);
        }
        return bytes.ToArray();
    }

    /// <summary>
    /// The file's tables; a missing section is vanilla's, with a note. Throws
    /// <see cref="InvalidDataException"/> with the reason for a damaged file.
    /// </summary>
    public static Requirements Read(byte[] bytes, Requirements vanilla, List<string> notes) =>
        RequirementsImport.Tables(FgpProject.Parse(bytes), vanilla, notes, ArchivePath);
}

/// <summary>The five tables from a FireGraft project (requirements spec §4, first open).</summary>
public static class RequirementsImport
{
    /// <summary>
    /// The project's sections, a missing one vanilla's. Blank items are left out, with a line
    /// "{tag}: {n} blank item(s) left out". Throws <see cref="InvalidDataException"/> for a damaged section.
    /// </summary>
    public static Requirements FromProject(FgpProject project, Requirements vanilla, List<string> status) =>
        Tables(project, vanilla, status, null);

    internal static Requirements Tables(FgpProject project, Requirements vanilla, List<string> notes, string? file)
    {
        var tables = new List<RequirementTable>();
        foreach (var table in ReqTables.All)
        {
            string tag = ReqTables.SectionTag(table);
            if (!project.Sections.TryGetValue(tag, out var section))
            {
                notes.Add(file is null ? $"{tag}: not in the project; vanilla" : $"{file}: no {tag} section; vanilla");
                tables.Add(vanilla[table]);
                continue;
            }
            RequirementTable parsed;
            try { parsed = ReqSections.Parse(table, section, out int blanks); if (blanks > 0) notes.Add($"{tag}: {blanks} blank item(s) left out"); }
            catch (InvalidDataException e) { throw new InvalidDataException($"section {tag} {e.Message}", e); }
            tables.Add(parsed);
        }
        return new Requirements(tables);
    }
}
