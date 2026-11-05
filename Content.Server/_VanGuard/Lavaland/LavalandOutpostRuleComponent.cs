using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Lavaland;

/// <summary>
/// Sets up the Lavaland planet after <see cref="Content.Server.GameTicking.Rules.Components.LoadMapRuleComponent"/>
/// has created and initialized the map.
/// </summary>
/// <remarks>
/// This is deliberately only a sequencer around existing APIs: <c>BiomeSystem.EnsurePlanet</c>,
/// <c>MapLoaderSystem.TryLoadGrid</c> and <c>BiomeSystem.ReserveTiles</c>. It does not generate
/// terrain or introduce a second planet system.
/// </remarks>
[RegisterComponent]
public sealed partial class LavalandOutpostRuleComponent : Component
{
    /// <summary>
    /// Biome template used for the surface. Kept as a prototype id so future Lavaland
    /// variants can reuse the same rule without touching C#.
    /// </summary>
    [DataField]
    public ProtoId<BiomeTemplatePrototype> Biome = "Lava";

    /// <summary>
    /// Grid loaded into the cleared area at the centre of the map.
    /// </summary>
    [DataField]
    public ResPath GridPath = new("/Maps/_VanGuard/Lavaland/mining_outpost.yml");

    /// <summary>
    /// Extra clearance around the outpost footprint, in tiles.
    /// The outpost itself is about 48 by 28 tiles, so ten tiles gives a comfortable
    /// margin without turning the whole map into a bare platform.
    /// </summary>
    [DataField]
    public float ClearRadius = 10f;

    /// <summary>
    /// Fixed seed for reproducible tests. Null means "pick a random seed".
    /// </summary>
    [DataField]
    public int? Seed;

    /// <summary>
    /// Ambient map light. Lava's own default is too bright for the Lavaland surface.
    /// </summary>
    [DataField]
    public Color MapLight = Color.FromHex("#6e2c2c");

    /// <summary>
    /// Map atmosphere. The prototype supplies the mole counts; see the YAML comment
    /// for the ideal-gas calculation.
    /// </summary>
    [DataField]
    public GasMixture Atmosphere = new();

    /// <summary>
    /// Lavaland has no day/night cycle in this iteration. <c>EnsurePlanet</c> adds
    /// <see cref="Content.Shared.Light.Components.LightCycleComponent"/> unconditionally,
    /// so the rule disables it instead of removing it.
    /// </summary>
    [DataField]
    public bool DisableLightCycle = true;

    /// <summary>
    /// <c>EnsurePlanet</c> adds <see cref="Content.Shared.Light.Components.RoofComponent"/>.
    /// The surface is open, so the rule removes it.
    /// </summary>
    [DataField]
    public bool RemoveRoof = true;

    /// <summary>
    /// Sun shadow direction, matching the intended Lavaland light direction.
    /// </summary>
    [DataField]
    public Vector2 SunShadowDirection = new(-1f, -1f);

    /// <summary>
    /// Sun shadow alpha for the Lavaland surface.
    /// </summary>
    [DataField]
    public float SunShadowAlpha = 0.75f;

    /// <summary>
    /// Gravity flags applied after <c>EnsurePlanet</c>.
    /// </summary>
    [DataField]
    public bool GravityEnabled = true;

    [DataField]
    public bool GravityInherent = true;

    /// <summary>
    /// Prototype spawned at the outpost as the FTL target.
    /// </summary>
    [DataField]
    public EntProtoId FtlPointProto = "LavalandFTLPoint";

    /// <summary>
    /// Marker layers added to the generated biome. Density and grouping live in
    /// the marker layer prototypes so they can be tuned without code changes.
    /// </summary>
    [DataField]
    public List<string> MarkerLayers = new();

    [DataField]
    public float MapBorderRadius = 250f;

    /// <summary>
    /// When true, the automatic round-start loader skips the expensive planet
    /// bootstrap on the empty/test map. Explicit LavalandLoader rules leave this disabled.
    /// </summary>
    [DataField]
    public bool SkipOnEmptyMap;

    /// <summary>
    /// Runtime reference to the created map, used by tests and diagnostics.
    /// </summary>
    [ViewVariables]
    public EntityUid? Map;

    /// <summary>
    /// Runtime map id, used by tests and diagnostics.
    /// </summary>
    [ViewVariables]
    public MapId? MapId;

    /// <summary>
    /// Runtime reference to the loaded outpost grid.
    /// </summary>
    [ViewVariables]
    public EntityUid? OutpostGrid;
}
