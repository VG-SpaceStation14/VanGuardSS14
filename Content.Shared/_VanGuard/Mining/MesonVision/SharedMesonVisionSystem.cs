using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Inventory.Events;
using Content.Shared.Rounding;
using Content.Shared.Toggleable;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Shared._VanGuard.Mining.MesonVision;

/// <summary>
/// Handles meson vision: worn items grant a toggleable vision overlay to their wearer,
/// and entities can carry the vision innately.
/// </summary>
public abstract partial class SharedMesonVisionSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MesonVisionComponent, ComponentStartup>(OnVisionStartup);
        SubscribeLocalEvent<MesonVisionComponent, MapInitEvent>(OnVisionMapInit);
        SubscribeLocalEvent<MesonVisionComponent, AfterAutoHandleStateEvent>(OnVisionAfterHandle);
        SubscribeLocalEvent<MesonVisionComponent, ComponentRemove>(OnVisionRemove);

        SubscribeLocalEvent<MesonVisionItemComponent, GetItemActionsEvent>(OnItemGetActions);
        SubscribeLocalEvent<MesonVisionItemComponent, ToggleActionEvent>(OnItemToggle);
        SubscribeLocalEvent<MesonVisionItemComponent, GotEquippedEvent>(OnItemGotEquipped);
        SubscribeLocalEvent<MesonVisionItemComponent, GotUnequippedEvent>(OnItemGotUnequipped);
        SubscribeLocalEvent<MesonVisionItemComponent, ActionRemovedEvent>(OnItemActionRemoved);
        SubscribeLocalEvent<MesonVisionItemComponent, ComponentRemove>(OnItemRemove);
        SubscribeLocalEvent<MesonVisionItemComponent, EntityTerminatingEvent>(OnItemTerminating);
    }

    private void OnVisionStartup(Entity<MesonVisionComponent> ent, ref ComponentStartup args)
        => MesonVisionChanged(ent);

    private void OnVisionAfterHandle(Entity<MesonVisionComponent> ent, ref AfterAutoHandleStateEvent args)
        => MesonVisionChanged(ent);

    private void OnVisionMapInit(Entity<MesonVisionComponent> ent, ref MapInitEvent args)
        => UpdateAlert(ent);

    private void OnVisionRemove(Entity<MesonVisionComponent> ent, ref ComponentRemove args)
    {
        if (ent.Comp.Alert is { } alert)
            _alerts.ClearAlert(ent.Owner, alert);

        MesonVisionRemoved(ent);
    }

    private void OnItemGetActions(Entity<MesonVisionItemComponent> ent, ref GetItemActionsEvent args)
    {
        if (args.InHands || !ent.Comp.Toggleable)
            return;

        if (ent.Comp.SlotFlags != args.SlotFlags)
            return;

        args.AddAction(ref ent.Comp.Action, ent.Comp.ActionId);
    }

    private void OnItemToggle(Entity<MesonVisionItemComponent> ent, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ToggleItem(ent, args.Performer);
    }

    private void OnItemGotEquipped(Entity<MesonVisionItemComponent> ent, ref GotEquippedEvent args)
    {
        if (ent.Comp.SlotFlags != args.SlotFlags)
            return;

        EnableItem(ent, args.EquipTarget);
    }

    private void OnItemGotUnequipped(Entity<MesonVisionItemComponent> ent, ref GotUnequippedEvent args)
    {
        if (ent.Comp.SlotFlags != args.SlotFlags)
            return;

        DisableItem(ent, args.EquipTarget);
    }

    private void OnItemActionRemoved(Entity<MesonVisionItemComponent> ent, ref ActionRemovedEvent args)
        => DisableItem(ent, ent.Comp.User);

    private void OnItemRemove(Entity<MesonVisionItemComponent> ent, ref ComponentRemove args)
        => DisableItem(ent, ent.Comp.User);

    private void OnItemTerminating(Entity<MesonVisionItemComponent> ent, ref EntityTerminatingEvent args)
        => DisableItem(ent, ent.Comp.User);

    /// <summary>
    /// Toggles the vision state of an entity that carries <see cref="MesonVisionComponent"/>.
    /// </summary>
    public void Toggle(Entity<MesonVisionComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        ent.Comp.State = ent.Comp.State switch
        {
            MesonVisionState.Off => MesonVisionState.Full,
            MesonVisionState.Full => MesonVisionState.Off,
            _ => throw new ArgumentOutOfRangeException(),
        };

        Dirty(ent);
        UpdateAlert((ent, ent.Comp));
    }
    private void UpdateAlert(Entity<MesonVisionComponent> ent)
    {
        if (ent.Comp.Alert is { } alert)
        {
            var level = MathF.Max((int)MesonVisionState.Off, (int)ent.Comp.State);
            var max = _alerts.GetMaxSeverity(alert);
            var severity = max - ContentHelpers.RoundToLevels(level, (int)MesonVisionState.Full, max + 1);
            _alerts.ShowAlert(ent.Owner, alert, (short)severity);
        }

        MesonVisionChanged(ent);
    }

    private void ToggleItem(Entity<MesonVisionItemComponent> item, EntityUid user)
    {
        if (item.Comp.User == user && item.Comp.Toggleable)
        {
            DisableItem(item, item.Comp.User);
            return;
        }

        EnableItem(item, user);
    }

    private void EnableItem(Entity<MesonVisionItemComponent> item, EntityUid user)
    {
        DisableItem(item, item.Comp.User);

        item.Comp.User = user;
        Dirty(item);

        _appearance.SetData(item, MesonVisionItemVisuals.Active, true);

        if (!_timing.ApplyingState)
        {
            var vision = EnsureComp<MesonVisionComponent>(user);
            vision.State = MesonVisionState.Full;
            Dirty(user, vision);
        }

        _actions.SetToggled(item.Comp.Action, true);
    }

    protected virtual void MesonVisionChanged(Entity<MesonVisionComponent> ent)
    {
    }

    protected virtual void MesonVisionRemoved(Entity<MesonVisionComponent> ent)
    {
    }

    protected void DisableItem(Entity<MesonVisionItemComponent> item, EntityUid? user)
    {
        _actions.SetToggled(item.Comp.Action, false);

        item.Comp.User = null;
        Dirty(item);

        _appearance.SetData(item, MesonVisionItemVisuals.Active, false);

        if (TryComp(user, out MesonVisionComponent? vision) && !vision.Innate)
        {
            RemCompDeferred<MesonVisionComponent>(user.Value);
        }
    }

}

