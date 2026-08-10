using System.Numerics;
using Content.Shared._VanGuard.CCVars;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._VanGuard.Shaders.Bloom;

/// <summary>
///     Full-screen volumetric glow drawn from the lighting buffer. Reuses the
///     Cataracts shader with zero distortion so the light texture softly bleeds
///     into the scene without warping it.
/// </summary>
public sealed partial class VolumetricLightOverlay : Overlay
{
    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private static readonly ProtoId<ShaderPrototype> LightShader = "Cataracts";

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IConfigurationManager _config = default!;

    private readonly ShaderInstance _shader;

    public VolumetricLightOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index(LightShader).Instance().Duplicate();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _players.LocalEntity is { Valid: true } && _config.GetCVar(VGCCVars.BloomEnabled);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is null || args.Viewport.LightRenderTarget?.Texture == null)
            return;

        if (_players.LocalEntity is not { Valid: true } player)
            return;

        var zoom = _entities.TryGetComponent<EyeComponent>(player, out var eye) ? eye.Zoom.X : 1f;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("LIGHT_TEXTURE", args.Viewport.LightRenderTarget.Texture);
        _shader.SetParameter("Zoom", zoom);
        _shader.SetParameter("DistortionScalar", 0f);
        _shader.SetParameter("CloudinessScalar", _config.GetCVar(VGCCVars.VolumetricLightStrength));

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
