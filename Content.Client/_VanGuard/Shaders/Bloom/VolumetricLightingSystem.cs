using Content.Shared._VanGuard.CCVars;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._VanGuard.Shaders.Bloom;

public sealed partial class VolumetricLightSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ISharedPlayerManager _playerMan = default!;

    private VolumetricLightOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        Subs.CVar(_cfg, VGCCVars.BloomEnabled, OnBloomEnabledChanged);

        _overlay = new();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        if (_cfg.GetCVar(VGCCVars.BloomEnabled))
            _overlayMan.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnBloomEnabledChanged(bool enabled)
    {
        if (enabled && _playerMan.LocalEntity != null)
            _overlayMan.AddOverlay(_overlay);
        else
            _overlayMan.RemoveOverlay(_overlay);
    }
}
