using System;
using System.Linq;
using Content.Shared._VanGuard.Delivery.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.Delivery;

/// <summary>
/// Server-side implementation of the classic falling-pod lifecycle:
/// falling capsule -> landing -> separate opened-pod/crater entity.
/// </summary>
public sealed partial class DeliveryPodSystem : EntitySystem
{
    private static readonly SoundPathSpecifier LandingSound =
        new("/Audio/_VanGuard/Effects/Droppod/droppod_landing.ogg");

    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeliveryPodComponent, ComponentInit>(OnPodInit);
        SubscribeLocalEvent<DeliveryPodComponent, MapInitEvent>(OnPodMapInit);
        SubscribeLocalEvent<DeliveryPodComponent, ComponentShutdown>(OnPodShutdown);
        SubscribeLocalEvent<DeliveryPodComponent, EntityTerminatingEvent>(OnPodTerminating);
    }

    private void OnPodInit(Entity<DeliveryPodComponent> pod, ref ComponentInit args)
    {
        pod.Comp.Contents = _container.EnsureContainer<Container>(pod.Owner, DeliveryPodComponent.ContainerId);
    }

    private void OnPodMapInit(Entity<DeliveryPodComponent> pod, ref MapInitEvent args)
    {
        if (pod.Comp.LandAt == default)
            pod.Comp.LandAt = _timing.CurTime + pod.Comp.LandingDelay;

        // Play the landing sound when the dropping entity appears.
        _audio.PlayPvs(LandingSound, pod.Owner);
    }

    private void OnPodShutdown(Entity<DeliveryPodComponent> pod, ref ComponentShutdown args)
    {
        RecoverContents(pod);
    }

    private void OnPodTerminating(Entity<DeliveryPodComponent> pod, ref EntityTerminatingEvent args)
    {
        RecoverContents(pod);
    }

    private void RecoverContents(Entity<DeliveryPodComponent> pod)
    {
        if (pod.Comp.Released || !TryGetReleaseCoordinates(pod.Owner, out var coordinates))
            return;

        // If the pod is destroyed before landing, do not delete the paid goods.
        if (!pod.Comp.Landed && pod.Comp.ContentsPrototypes.Count > 0)
        {
            foreach (var prototype in pod.Comp.ContentsPrototypes)
                Spawn(prototype, coordinates);

            pod.Comp.Released = true;
            return;
        }

        ReleaseContents(pod, coordinates);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DeliveryPodComponent>();
        while (query.MoveNext(out var uid, out var pod))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (!pod.Landed)
            {
                if (_timing.CurTime < pod.LandAt)
                    continue;

                Land((uid, pod));
                continue;
            }

            if (!pod.Released && TryGetReleaseCoordinates(uid, out var coordinates))
                ReleaseContents((uid, pod), coordinates);
        }
    }

    /// <summary>
    /// Creates a falling pod and puts all requested contents into its container.
    /// Callers can keep their order list until this returns true.
    /// </summary>
    public bool TryCreateDelivery(
        EntityCoordinates target,
        IReadOnlyList<EntProtoId> contents,
        EntProtoId podPrototype)
    {
        if (!target.IsValid(EntityManager) || !ProtoMan.HasIndex<EntityPrototype>(podPrototype))
            return false;

        foreach (var content in contents)
        {
            if (!ProtoMan.HasIndex<EntityPrototype>(content))
                return false;
        }

        EntityUid pod = default;
        try
        {
            pod = Spawn(podPrototype, target);
            if (!TryComp<DeliveryPodComponent>(pod, out var delivery))
            {
                QueueDel(pod);
                return false;
            }

            delivery.Contents = _container.EnsureContainer<Container>(pod, DeliveryPodComponent.ContainerId);
            delivery.ContentsPrototypes = contents.ToList();
            delivery.Landed = false;
            delivery.OpenedSpawned = false;
            delivery.Released = false;

            foreach (var content in contents)
            {
                var item = Spawn(content, target);
                if (_container.Insert(item, delivery.Contents))
                    continue;

                delivery.Released = true;
                QueueDel(pod);
                return false;
            }

            delivery.LandAt = _timing.CurTime + delivery.LandingDelay;
            return true;
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to create a delivery pod with contents: {exception}");
            if (pod != default && Exists(pod))
                QueueDel(pod);
            return false;
        }
    }

    private void Land(Entity<DeliveryPodComponent> pod)
    {
        if (pod.Comp.Landed)
            return;

        pod.Comp.Landed = true;
        if (!TryGetReleaseCoordinates(pod.Owner, out var coordinates))
            return;

        // Spawn the opened pod/crater as a separate visual entity.
        if (!pod.Comp.OpenedSpawned && ProtoMan.HasIndex<EntityPrototype>(pod.Comp.OpenedPrototype))
        {
            Spawn(pod.Comp.OpenedPrototype, coordinates);
            pod.Comp.OpenedSpawned = true;
        }

        ReleaseContents(pod, coordinates);

        if (pod.Comp.Released)
            QueueDel(pod);
    }

    private void ReleaseContents(Entity<DeliveryPodComponent> pod, EntityCoordinates coordinates)
    {
        if (pod.Comp.Released)
            return;

        // Force removal also works while the falling pod is terminating.
        foreach (var item in pod.Comp.Contents.ContainedEntities.ToList())
        {
            if (TerminatingOrDeleted(item))
                continue;

            if (_container.Remove(item, pod.Comp.Contents, force: true, reparent: false) &&
                !TerminatingOrDeleted(item))
            {
                _transform.SetCoordinates(item, coordinates);
            }
        }

        pod.Comp.Released = pod.Comp.Contents.ContainedEntities.Count == 0;
    }

    private bool TryGetReleaseCoordinates(EntityUid uid, out EntityCoordinates coordinates)
    {
        coordinates = Transform(uid).Coordinates;
        return coordinates.IsValid(EntityManager) && !TerminatingOrDeleted(coordinates.EntityId);
    }
}
