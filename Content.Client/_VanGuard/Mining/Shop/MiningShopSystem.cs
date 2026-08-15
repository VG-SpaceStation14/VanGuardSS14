using Content.Shared._VanGuard.Mining.Shop;
using Content.Shared._VanGuard.Mining.Shop.Components;

namespace Content.Client._VanGuard.Mining.Shop;

/// <summary>
/// Refreshes open mining shop windows whenever the vendor state changes.
/// </summary>
public sealed partial class MiningShopSystem : SharedMiningShopSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MiningShopComponent, AfterAutoHandleStateEvent>(OnRefresh);
    }

    private void OnRefresh<T>(Entity<T> ent, ref AfterAutoHandleStateEvent args) where T : IComponent?
    {
        if (!TryComp(ent, out UserInterfaceComponent? ui))
            return;

        foreach (var bui in ui.ClientOpenInterfaces.Values)
        {
            if (bui is MiningShopBui shopUi)
                shopUi.Refresh();
        }
    }
}
