using Content.Shared.Mining.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._VanGuard.Mining;

/// <summary>
/// Manages <see cref="InnateMiningScannerViewerComponent"/>: entities carrying it permanently
/// receive ore-detection vision via <see cref="MiningScannerViewerComponent"/>, even without a
/// handheld mineral scanner. An activated scanner, while held, takes priority over the innate
/// range; when it is unequipped or turned off, innate vision is restored.
/// </summary>
public sealed partial class SharedInnateMiningScannerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InnateMiningScannerViewerComponent, ComponentStartup>(OnStartup);
    }

    /// <summary>
    /// Tries to keep the mining viewer alive on <paramref name="uid"/> using innate vision.
    /// </summary>
    /// <returns>True if the entity has innate vision and its viewer was (re)applied.</returns>
    public bool TryRestoreInnateViewer(EntityUid uid)
    {
        if (_net.IsServer
            && TryComp<InnateMiningScannerViewerComponent>(uid, out var innate))
        {
            ApplyInnateViewer(uid, innate);
            return true;
        }

        return false;
    }

    private void OnStartup(Entity<InnateMiningScannerViewerComponent> ent, ref ComponentStartup args)
    {
        // A viewer already granted by an equipped scanner (or a stale one) takes precedence.
        if (_net.IsServer && !HasComp<MiningScannerViewerComponent>(ent))
            ApplyInnateViewer(ent, ent.Comp);
    }

    private void ApplyInnateViewer(EntityUid uid, InnateMiningScannerViewerComponent innate)
    {
        var viewer = EnsureComp<MiningScannerViewerComponent>(uid);
        viewer.ViewRange = innate.ViewRange;
        viewer.PingDelay = innate.PingDelay;
        viewer.PingSound = innate.PingSound;
        viewer.AnimationDuration = innate.AnimationDuration;
        viewer.QueueRemoval = false;
        viewer.NextPingTime = _timing.CurTime + innate.PingDelay;
        Dirty(uid, viewer);
    }
}
