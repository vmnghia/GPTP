using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Model;

/// <summary>
/// The .dat files a document starts with: those in the exe being edited (or the copy the
/// user chose) and StarCraft's own. A file missing everywhere is absent from both.
/// <paramref name="Overrides"/> are the exe's name overrides (see <see cref="DatSources.LoadOverrides"/>); a file with none is absent.
/// </summary>
public sealed record DatSet(IReadOnlyDictionary<DatId, DatFile> Opened, IReadOnlyDictionary<DatId, DatFile> Vanilla,
    IReadOnlyDictionary<DatId, NameOverrides>? Overrides = null);
