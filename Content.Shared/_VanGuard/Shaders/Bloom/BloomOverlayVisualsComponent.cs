using System.Numerics;
using Robust.Shared.Utility;
using static Robust.Shared.Utility.SpriteSpecifier;

namespace Content.Shared._VanGuard.Shaders.Bloom;

/// <summary>
///     Marks a light source so the client draws a soft glow around it.
///     The mask sprites and their shader tuning live here so the rendering
///     side can look them all up from a single place.
/// </summary>
[RegisterComponent]
public sealed partial class BloomOverlayVisualsComponent : Component
{
    /// <summary>
    ///     Wide, cone-shaped glow mask used for lamps and flashlights.
    /// </summary>
    public static readonly SpriteSpecifier ConeMask =
        new Rsi(new ResPath("_VanGuard/Effects/LightMasks/128.rsi"), "light_cone");

    /// <summary>
    ///     Where the cone mask is anchored relative to the light source.
    /// </summary>
    public static readonly Vector2 ConeAnchor = new(0f, -0.2f);

    /// <summary>
    ///     How strongly the cone glow is tinted toward a warm haze.
    /// </summary>
    public const float ConeHaze = 0.4f;

    /// <summary>
    ///     Brightness falloff divisor for the cone glow.
    /// </summary>
    public const float ConeFalloff = 0.225f;

    /// <summary>
    ///     Circular glow mask used for point lights.
    /// </summary>
    public static readonly SpriteSpecifier PointMask =
        new Rsi(new ResPath("_VanGuard/Effects/LightMasks/64.rsi"), "light_point");

    /// <summary>
    ///     Where the point mask is anchored relative to the light source.
    /// </summary>
    public static readonly Vector2 PointAnchor = new(0f, 0.45f);

    /// <summary>
    ///     How strongly the point glow is tinted toward a warm haze.
    /// </summary>
    public const float PointHaze = 0.8f;

    /// <summary>
    ///     Brightness falloff divisor for the point glow.
    /// </summary>
    public const float PointFalloff = 0.05f;
}

