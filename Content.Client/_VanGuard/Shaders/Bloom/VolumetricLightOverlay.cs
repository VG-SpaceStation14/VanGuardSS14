using System.Numerics;
using Content.Shared._VanGuard.CCVars;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Configuration;

namespace Content.Client._VanGuard.Shaders.Bloom;

public sealed partial class VolumetricLightOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private static readonly ProtoId<ShaderPrototype> Shader = "Cataracts";
    private readonly ShaderInstance _cataractsShader;

    public VolumetricLightOverlay()
    {
        IoCManager.InjectDependencies(this);
        _cataractsShader = _prototypeManager.Index(Shader).Instance().Duplicate();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity is not { Valid: true } player)
            return false;

        if (!_cfg.GetCVar(VGCCVars.BloomEnabled))
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture is null)
            return;

        if (args.Viewport.LightRenderTarget?.Texture == null)
            return;

        var player = _playerManager.LocalEntity;
        if (player == null)
            return;

        float zoom = 1f;
        if (_entityManager.TryGetComponent<EyeComponent>(player, out var eyeComp))
            zoom = eyeComp.Zoom.X;

        float strength = _cfg.GetCVar(VGCCVars.VolumetricLightStrength);

        _cataractsShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _cataractsShader.SetParameter("LIGHT_TEXTURE", args.Viewport.LightRenderTarget.Texture);
        _cataractsShader.SetParameter("Zoom", zoom);
        _cataractsShader.SetParameter("DistortionScalar", 0f);
        _cataractsShader.SetParameter("CloudinessScalar", strength);

        var worldHandle = args.WorldHandle;
        var viewport = args.WorldBounds;
        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_cataractsShader);
        worldHandle.DrawRect(viewport, Color.White);
        worldHandle.UseShader(null);
    }
}
