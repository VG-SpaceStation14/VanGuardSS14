using Content.Shared._VanGuard.CCVars;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._VanGuard.Shaders.Bloom;

/// <summary>
///     Adds/removes the <see cref="VolumetricLightOverlay"/> whenever a local player
///     is present and the bloom CVar is enabled.
/// </summary>
public sealed partial class VolumetricLightSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    private VolumetricLightOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new VolumetricLightOverlay();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        Subs.CVar(_config, VGCCVars.BloomEnabled, OnBloomChanged);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        // Remove and release the overlay even if the local player is still attached
        // and bloom is enabled. Both calls are safe when the overlay is not registered.
        _overlays.RemoveOverlay(_overlay);
        _overlay.Dispose();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        if (_config.GetCVar(VGCCVars.BloomEnabled))
            _overlays.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _overlays.RemoveOverlay(_overlay);
    }

    private void OnBloomChanged(bool enabled)
    {
        if (enabled && _players.LocalEntity != null)
            _overlays.AddOverlay(_overlay);
        else
            _overlays.RemoveOverlay(_overlay);
    }
}
