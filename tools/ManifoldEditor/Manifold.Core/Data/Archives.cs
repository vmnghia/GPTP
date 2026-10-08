namespace Manifold.Core.Data;

/// <summary>An MPQ (or anything holding game files) the editor reads from.</summary>
public interface IArchive
{
    /// <summary>Shown in the status bar: where a resource came from.</summary>
    string Name { get; }
    byte[]? TryRead(string path);

    /// <summary>The files matching a mask such as "Firegraft\\*.fgp", if the archive can list them.</summary>
    IReadOnlyList<string> List(string mask) => Array.Empty<string>();
}

/// <summary>The mod exe's MPQ, which the editor also writes to.</summary>
public interface IWritableArchive : IArchive
{
    /// <summary>
    /// Writes the files in a single operation: all of them or none. Throws
    /// <see cref="ArchiveBusyException"/> when the exe is held by another program; nothing
    /// is written then.
    /// </summary>
    void Write(IReadOnlyList<(string Path, byte[] Data)> files);

    void Write(string path, byte[] data) => Write([(path, data)]);
}

public sealed class ArchiveBusyException(string message) : Exception(message);

/// <summary>Looks a file up in the archives in order: the mod exe first, then the vanilla MPQs.</summary>
public sealed class ResourceResolver(IReadOnlyList<IArchive> archives)
{
    public (byte[] Data, string Source)? Find(string path)
    {
        foreach (var archive in archives)
            if (archive.TryRead(path) is { } data)
                return (data, archive.Name);
        return null;
    }
}
