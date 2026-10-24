using Manifold.Core.Data;

namespace Manifold.Core.Model;

/// <summary>
/// The .dat files a document starts with: those in the exe being edited (or the copy the
/// user chose) and StarCraft's own. A file missing everywhere is absent from both.
/// </summary>
public sealed record DatSet(IReadOnlyDictionary<DatId, DatFile> Opened, IReadOnlyDictionary<DatId, DatFile> Vanilla);
