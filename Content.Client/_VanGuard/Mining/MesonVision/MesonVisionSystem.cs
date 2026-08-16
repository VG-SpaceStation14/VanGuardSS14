using Content.Shared._VanGuard.Mining.MesonVision;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._VanGuard.Mining.MesonVision;

/// <summary>
/// Client-side meson vision: enables or disables the vision overlay for the local player.
/// </summary>
public sealed partial class MesonVisionSystem : SharedMesonVisionSystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MesonVisionComponent, LocalPlayerAttachedEvent>(OnVisionAttached);
        SubscribeLocalEvent<MesonVisionComponent, LocalPlayerDetachedEvent>(OnVisionDetached);
    }

    private void OnVisionAttached(Entity<MesonVisionComponent> ent, ref LocalPlayerAttachedEvent args)
        => MesonVisionChanged(ent);

    private void OnVisionDetached(Entity<MesonVisionComponent> ent, ref LocalPlayerDetachedEvent args)
        => Off();

    protected override void MesonVisionChanged(Entity<MesonVisionComponent> ent)
    {
        if (ent != _player.LocalEntity)
            return;

        switch (ent.Comp.State)
        {
            case MesonVisionState.Off:
                Off();
                break;
            case MesonVisionState.Full:
                Full();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    protected override void MesonVisionRemoved(Entity<MesonVisionComponent> ent)
    {
        if (ent != _player.LocalEntity)
            return;

        Off();
    }

    private void Off() => _overlay.RemoveOverlay(new MesonVisionOverlay());

    private void Full() => _overlay.AddOverlay(new MesonVisionOverlay());
}

