using System.Numerics;
using Robust.Shared.Localization;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// A single entry in the Lavaland POI fax note.
/// </summary>
public readonly record struct LavalandPoi(Vector2 Offset, LocId Name, bool InFax);
