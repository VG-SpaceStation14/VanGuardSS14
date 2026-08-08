using System.Numerics;
using Content.Shared._VanGuard.CCVars;
using Content.Shared._VanGuard.Shaders.Bloom;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._VanGuard.Shaders.Bloom;

/// <summary>
///     Collects every lit entity marked with <see cref="BloomOverlayVisualsComponent"/>
///     and feeds it to <see cref="LightGlowOverlay"/>, which renders the glow on top
///     of the world.
/// </summary>
public sealed partial class LightGlowSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprites = default!;

    private LightGlowOverlay _glow = default!;
    private EntityQuery<TransformComponent> _xformQuery = default!;
    private ConfigurationMultiSubscriptionBuilder _subscriptions = default!;

    private static readonly ProtoId<ShaderPrototype> BlurShader = "LightingOverlay";

    private bool _bloomOn;
    private bool _coneOn;

    public override void Initialize()
    {
        base.Initialize();

        _xformQuery = GetEntityQuery<TransformComponent>();
        _glow = new LightGlowOverlay(_prototypes, _sprites, BlurShader);

        _subscriptions = _config.SubscribeMultiple()
            .OnValueChanged(VGCCVars.BloomEnabled, SetBloomEnabled, true)
            .OnValueChanged(VGCCVars.LightBloomConeEnable, SetConeEnabled, true)
            .OnValueChanged(VGCCVars.LightBloomStrength, SetStrength, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_bloomOn || _players.LocalEntity == null)
            return;

        var sources = new List<GlowSource>();

        var query = EntityQueryEnumerator<BloomOverlayVisualsComponent, PointLightComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var light, out var xform))
        {
            if (!light.Enabled)
                continue;

            var (pos, _, matrix) = _transform.GetWorldPositionRotationMatrix(xform, _xformQuery);
            sources.Add(new GlowSource(xform, matrix, pos, light.Color));
        }

        _glow.Sources = sources;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay(_glow);
        _glow.Dispose();
        _subscriptions.Dispose();
    }

    private void SetBloomEnabled(bool enabled)
    {
        _bloomOn = enabled;
        RefreshOverlay();
    }

    private void SetConeEnabled(bool enabled)
    {
        _coneOn = enabled;
        RefreshOverlay();
    }

    private void SetStrength(float strength)
    {
        _glow.Strength = Math.Clamp(strength, 0.1f, 1f);
    }

    private void RefreshOverlay()
    {
        var active = _bloomOn;
        _glow.Enabled = active;
        _glow.ConeEnabled = active && _coneOn;

        if (active && !_overlays.HasOverlay<LightGlowOverlay>())
            _overlays.AddOverlay(_glow);
        else if (!active)
            _overlays.RemoveOverlay(_glow);
    }
}

/// <summary>
///     Draws the glow masks for every light source gathered by <see cref="LightGlowSystem"/>.
///     Both the cone and the point mask are rendered by this single overlay, with the
///     cone layer drawn first so the circular layer blends over it.
/// </summary>
public sealed partial class LightGlowOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _blur;
    private readonly Texture _coneMask;
    private readonly Texture _pointMask;
    private readonly Vector2 _coneOffset;
    private readonly Vector2 _pointOffset;

    public List<GlowSource> Sources = [];
    public bool Enabled;
    public bool ConeEnabled;
    public float Strength = 0.5f;

    public LightGlowOverlay(IPrototypeManager prototypes, SpriteSystem sprites, ProtoId<ShaderPrototype> shader)
    {
        _blur = prototypes.Index(shader).InstanceUnique();
        ZIndex = (int)DrawDepth.Effects;

        _coneMask = sprites.Frame0(BloomOverlayVisualsComponent.ConeMask);
        _pointMask = sprites.Frame0(BloomOverlayVisualsComponent.PointMask);
        _coneOffset = MaskOffset(BloomOverlayVisualsComponent.ConeAnchor, _coneMask);
        _pointOffset = MaskOffset(BloomOverlayVisualsComponent.PointAnchor, _pointMask);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        base.BeforeDraw(in args);
        return Enabled;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || Sources.Count == 0)
            return;

        var handle = args.WorldHandle;
        var bounds = args.WorldAABB.Enlarged(5f);

        handle.UseShader(_blur);
        _blur.SetParameter("SCREEN_TEXTURE", ScreenTexture);

        if (ConeEnabled)
            DrawLayer(args, handle, bounds, _coneMask, _coneOffset,
                BloomOverlayVisualsComponent.ConeHaze, BloomOverlayVisualsComponent.ConeFalloff);

        DrawLayer(args, handle, bounds, _pointMask, _pointOffset,
            BloomOverlayVisualsComponent.PointHaze, BloomOverlayVisualsComponent.PointFalloff);

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawLayer(in OverlayDrawArgs args, DrawingHandleWorld handle, Box2 bounds,
        Texture mask, Vector2 offset, float haze, float falloff)
    {
        _blur.SetParameter("haze_amount", haze);
        _blur.SetParameter("fade_divisor", falloff / Strength);

        foreach (var source in Sources)
        {
            if (source.Xform.MapID != args.MapId || !bounds.Contains(source.Position))
                continue;

            handle.SetTransform(source.Matrix);
            handle.DrawTexture(mask, offset, source.Color);
        }
    }

    private static Vector2 MaskOffset(Vector2 anchor, Texture mask)
    {
        var x = anchor.X - (mask.Width / 2f) / EyeManager.PixelsPerMeter;
        var y = anchor.Y - (mask.Height / 2f) / EyeManager.PixelsPerMeter;
        return new Vector2(x, y);
    }
}

/// <summary>
///     A single light source to draw a glow for.
/// </summary>
public readonly record struct GlowSource(
    TransformComponent Xform,
    Matrix3x2 Matrix,
    Vector2 Position,
    Color Color);

