using System.Linq;
using Content.Server._VanGuard.MartialArts;
using Content.Shared._VanGuard.MartialArts;
using Content.Shared._VanGuard.WeaponCombos;
using Content.Shared.Audio;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._VanGuard.WeaponCombos;

/// <summary>
/// Server-side stance and combo handling for weapons with <see cref="WeaponComboComponent"/>.
/// The actual effects are executed by the existing martial arts effect executor.
/// </summary>
public sealed partial class WeaponComboSystem : EntitySystem
{
    private static readonly SoundPathSpecifier DefaultSwapSound = new("/Audio/Effects/thudswoosh.ogg");

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MartialArtsSystem _martialArts = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeaponComboComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<WeaponComboComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<WeaponComboComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<WeaponComboComponent> ent, ref ComponentStartup args)
    {
        UpdateVisuals(ent);
    }

    private void OnMeleeHit(Entity<WeaponComboComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        var target = args.HitEntities.First();
        if (target == args.User ||
            !HasComp<HumanoidProfileComponent>(target) ||
            !HasComp<MobStateComponent>(target) ||
            _mobState.IsDead(target))
            return;

        var heavy = args.Direction != null;
        var action = ent.Comp.Stance switch
        {
            WeaponComboStance.Offensive => heavy ? WeaponComboAction.OffensiveHeavy : WeaponComboAction.OffensiveLight,
            _ => heavy ? WeaponComboAction.ProtectiveHeavy : WeaponComboAction.ProtectiveLight,
        };

        var now = _timing.CurTime;
        ent.Comp.History.RemoveAll(entry => now - entry.At > TimeSpan.FromSeconds(3) || entry.Target != target);
        if (ent.Comp.History.Count > 0 && ent.Comp.History[^1].Target != target)
            ent.Comp.History.Clear();

        ent.Comp.History.Add(new WeaponComboAttack(action, target, now));
        while (ent.Comp.History.Count > 5)
            ent.Comp.History.RemoveAt(0);

        if (now < ent.Comp.NextComboAt)
            return;

        WeaponComboMove? match = null;
        var matchLength = 0;
        foreach (var move in ent.Comp.Moves)
        {
            if (move.Actions.Count == 0 || move.Actions.Count <= matchLength || move.Actions.Count > ent.Comp.History.Count)
                continue;

            var offset = ent.Comp.History.Count - move.Actions.Count;
            var matched = true;
            for (var i = 0; i < move.Actions.Count; i++)
            {
                if (ent.Comp.History[offset + i].Action != move.Actions[i])
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
                continue;

            match = move;
            matchLength = move.Actions.Count;
        }

        if (match == null)
            return;

        foreach (var effect in match.Effects)
            _martialArts.ApplyEffect(args.User, target, effect);

        ent.Comp.History.Clear();
        ent.Comp.NextComboAt = now + TimeSpan.FromSeconds(0.5);
    }

    private void OnGetVerbs(Entity<WeaponComboComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("weapon-combo-switch-stance"),
            Act = () => ToggleStance(ent.Owner, ent.Comp),
            DoContactInteraction = false,
        });
    }

    public void ToggleStance(EntityUid weapon, WeaponComboComponent? comp = null)
    {
        if (!Resolve(weapon, ref comp))
            return;

        comp.Stance = comp.Stance == WeaponComboStance.Protective
            ? WeaponComboStance.Offensive
            : WeaponComboStance.Protective;

        Dirty(weapon, comp);
        UpdateVisuals((weapon, comp));
        _audio.PlayPvs(comp.SwapSound ?? DefaultSwapSound, weapon);
        _popup.PopupEntity(Loc.GetString("weapon-combo-stance-changed",
            ("stance", Loc.GetString(comp.Stance == WeaponComboStance.Offensive
                ? "weapon-combo-stance-offensive"
                : "weapon-combo-stance-protective"))), weapon, weapon);
    }

    private void UpdateVisuals(Entity<WeaponComboComponent> ent)
    {
        _appearance.SetData(ent.Owner, WeaponComboVisuals.State, ent.Comp.Stance);
    }
}
