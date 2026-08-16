using System.Linq;
using Content.Shared.Interaction.Events;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Shared._VanGuard.Mining.Shop;

/// <summary>
/// Handles the one-use mining shop delivery bag: using it in hand spills all
/// contained items onto the floor around the user, then deletes the bag.
/// </summary>
public sealed partial class SharedMiningShopDeliveryBagSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MiningShopDeliveryBagComponent, UseInHandEvent>(OnUseInHand);
    }

    private void OnUseInHand(Entity<MiningShopDeliveryBagComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        // Swallow the use everywhere so the storage UI never opens.
        args.Handled = true;

        if (!_net.IsServer)
            return;

        if (TryComp<StorageComponent>(ent, out var storage))
        {
            var coords = Transform(args.User).Coordinates;
            var items = storage.Container.ContainedEntities.ToList();

            foreach (var item in items)
            {
                _container.RemoveEntity(ent, item, destination: coords.Offset(_random.NextVector2Box(0.25f)));
            }
        }

        QueueDel(ent);
    }
}
