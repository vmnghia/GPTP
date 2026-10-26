using Manifold.Core.Data;
using Manifold.Core.Model;
using Manifold.Storm;

namespace ManifoldEditor.App.Services;

/// <summary>Repack's StormLib part: adding files to the work copy's MPQ, and reading an MPQ back.</summary>
public sealed class StormRepackHost : IRepackHost
{
    public void AddFiles(string file, IReadOnlyList<(string Path, byte[] Data)> files) => StormArchive.AddFiles(file, files);

    public IArchive Open(string file) => new StormArchive(file);
}
