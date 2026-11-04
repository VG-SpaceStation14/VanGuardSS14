using System.Numerics;
using Robust.Shared.Localization;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// A single entry in the Lavaland POI fax note. Name is the exact grid name;
/// FaxName is the deliberately vague designation used by the survey report.
/// </summary>
public readonly record struct LavalandPoi(Vector2 Offset, LocId Name, LocId FaxName, bool InFax, bool IsTendril);
