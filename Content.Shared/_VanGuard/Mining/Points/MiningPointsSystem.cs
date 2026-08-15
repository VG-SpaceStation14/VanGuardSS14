using Content.Shared._VanGuard.Mining.Points.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Lathe;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._VanGuard.Mining.Points;

/// <summary>
/// Central system for the mining points economy: manages point balances on ID cards and
/// ore processors, rewards points for smelting ore, and handles claiming points to an ID card.
/// </summary>
public sealed partial class MiningPointsSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;

    private EntityQuery<MiningPointsComponent> _query = default!;

    public override void Initialize()
    {
        base.Initialize();

        _query = GetEntityQuery<MiningPointsComponent>();

        SubscribeLocalEvent<MiningPointsLatheComponent, LatheStartPrintingEvent>(OnLatheStartPrinting);
        Subs.BuiEvents<MiningPointsLatheComponent>(LatheUiKey.Key, subs =>
        {
            subs.Event<LatheClaimMiningPointsMessage>(OnClaimMiningPoints);
        });
    }

    private void OnLatheStartPrinting(Entity<MiningPointsLatheComponent> ent, ref LatheStartPrintingEvent args)
    {
        var points = args.Recipe.MiningPoints;
        if (points > 0)
            AddPoints(ent.Owner, points);
    }

    private void OnClaimMiningPoints(Entity<MiningPointsLatheComponent> ent, ref LatheClaimMiningPointsMessage args)
    {
        var user = args.Actor;
        if (TryFindIdCard(user) is { } idCard)
            TransferAll(ent.Owner, idCard);
    }

    /// <summary>
    /// Tries to find the user's ID card and returns its mining points component.
    /// </summary>
    public Entity<MiningPointsComponent?>? TryFindIdCard(EntityUid user)
    {
        if (!_idCard.TryFindIdCard(user, out var idCard))
            return null;

        if (!_query.TryComp(idCard, out var comp))
            return null;

        return (idCard, comp);
    }

    /// <summary>
    /// Removes points from a holder, returning whether it succeeded.
    /// </summary>
    public bool RemovePoints(Entity<MiningPointsComponent?> ent, uint amount)
    {
        if (!_query.Resolve(ent, ref ent.Comp) || amount > ent.Comp.Points)
            return false;

        ent.Comp.Points -= amount;
        Dirty(ent);
        return true;
    }

    /// <summary>
    /// Adds points to a holder.
    /// </summary>
    public bool AddPoints(Entity<MiningPointsComponent?> ent, uint amount)
    {
        if (!_query.Resolve(ent, ref ent.Comp))
            return false;

        ent.Comp.Points += amount;
        Dirty(ent);
        return true;
    }

    /// <summary>
    /// Transfers a number of points from one holder to another.
    /// </summary>
    public bool Transfer(Entity<MiningPointsComponent?> src, Entity<MiningPointsComponent?> dest, uint amount)
    {
        if (amount == 0)
            return true;

        if (!_query.Resolve(src, ref src.Comp) || !_query.Resolve(dest, ref dest.Comp))
            return false;

        if (!RemovePoints(src, amount))
            return false;

        AddPoints(dest, amount);
        _audio.PlayPvs(src.Comp.TransferSound, src);
        return true;
    }

    /// <summary>
    /// Transfers all points from one holder to another.
    /// </summary>
    public bool TransferAll(Entity<MiningPointsComponent?> src, Entity<MiningPointsComponent?> dest)
    {
        return _query.Resolve(src, ref src.Comp) && Transfer(src, dest, src.Comp.Points);
    }
}

