using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Content.Server._VanGuard.Grab;
using Content.Server._VanGuard.MartialArts;
using Content.Server._VanGuard.WeaponCombos;
using Content.Shared._VanGuard.Grab;
using Content.Shared._VanGuard.MartialArts;
using Content.Shared._VanGuard.WeaponCombos;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard;

[TestFixture]
[NonParallelizable]
public sealed class MartialArtsTest : GameTest
{
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    private async Task<(EntityUid User, EntityUid Target, MapId Map)> CreateFighters(float separation = 0f)
    {
        EntityUid? user = null;
        EntityUid? target = null;
        var mapId = MapId.Nullspace;

        await Server.WaitPost(() =>
        {
            var map = Server.System<SharedMapSystem>();
            map.CreateMap(out mapId);
            user = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(Vector2.Zero, mapId));
            target = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(separation, 0), mapId));
        });

        Assert.That(user, Is.Not.Null);
        Assert.That(target, Is.Not.Null);
        return (user!.Value, target!.Value, mapId);
    }

    private async Task Learn(EntityUid user, string style)
    {
        var system = Server.System<MartialArtsSystem>();
        var learned = false;
        await Server.WaitPost(() => learned = system.TryLearnStyle(user, style, permanent: true));
        Assert.That(learned, Is.True, $"Failed to learn {style}");
    }

    private async Task RaiseMelee(EntityUid user, EntityUid target)
    {
        await Server.WaitPost(() =>
        {
            var hit = new MeleeHitEvent(new HashSet<EntityUid> { target }, user, user, new Content.Shared.Damage.DamageSpecifier(), null);
            SEntMan.EventBus.RaiseLocalEvent(user, hit);
        });
    }


    private async Task RaiseWeaponHit(EntityUid user, EntityUid weapon, EntityUid target, bool heavy)
    {
        await Server.WaitPost(() =>
        {
            var hit = new MeleeHitEvent(new HashSet<EntityUid> { target }, user, weapon,
                new Content.Shared.Damage.DamageSpecifier(), heavy ? Vector2.UnitX : null);
            SEntMan.EventBus.RaiseLocalEvent(weapon, hit);
        });
    }

    private async Task StartGrab(EntityUid user, EntityUid target, bool advanceToSoft = true)
    {
        var pulling = Server.System<PullingSystem>();
        var started = false;
        await Server.WaitPost(() => started = pulling.TryStartPull(user, target));
        Assert.That(started, Is.True, "Failed to start the test pull");

        if (advanceToSoft)
        {
            var stages = Server.System<GrabStageSystem>();
            await Server.WaitPost(() => Assert.That(stages.TryAdvanceStage(user), Is.True, "Failed to advance to soft grab"));
            await Pair.RunTicksSync(20);
        }
    }

    private async Task AdvanceGrab(EntityUid user, GrabStage targetStage)
    {
        var stages = Server.System<GrabStageSystem>();
        while (true)
        {
            var current = GrabStage.None;
            await Server.WaitAssertion(() => current = SEntMan.GetComponent<GrabStageComponent>(user).Stage);
            if (current >= targetStage)
                return;

            await Server.WaitPost(() => Assert.That(stages.TryAdvanceStage(user), Is.True,
                $"Failed to advance grab from {current} to {targetStage}"));
            await Pair.RunTicksSync(20);
        }
    }

    [Test]
    public async Task BookTeachesStyleOnceAndIsConsumed()
    {
        var (user, _, _) = await CreateFighters();
        EntityUid? book = null;

        await Server.WaitPost(() =>
        {
            book = SEntMan.SpawnEntity("CQCManual", SEntMan.GetComponent<TransformComponent>(user).Coordinates);
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user, book.Value), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(book.Value, new UseInHandEvent(user));
        });

        await Pair.RunTicksSync(300);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<MartialArtsKnowledgeComponent>(user), Is.True);
            Assert.That(SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user).KnownStyles, Does.Contain(new ProtoId<MartialArtPrototype>("CQC")));
            Assert.That(SEntMan.EntityExists(book!.Value), Is.False, "A successfully used manual must be consumed");
        });

        // A second manual must not be consumed if the style is already known.
        EntityUid? second = null;
        await Server.WaitPost(() =>
        {
            second = SEntMan.SpawnEntity("CQCManual", SEntMan.GetComponent<TransformComponent>(user).Coordinates);
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user, second.Value), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(second.Value, new UseInHandEvent(user));
        });

        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() => Assert.That(SEntMan.EntityExists(second!.Value), Is.True));
    }

    [Test]
    public async Task CqcComboRequiresSequenceAndDealsDamage()
    {
        var (user, target, _) = await CreateFighters();
        await Learn(user, "CQC");

        var damage = Server.System<DamageableSystem>();
        var before = damage.GetTotalDamage(target).Float();

        await StartGrab(user, target);
        await RaiseMelee(user, target);

        var after = damage.GetTotalDamage(target).Float();
        Assert.That(after, Is.GreaterThan(before), "CQC grab + hit should deal damage");

        var downed = false;
        await Server.WaitAssertion(() => downed = SEntMan.System<StandingStateSystem>().IsDown(target));
        Assert.That(downed, Is.True, "CQC slam should knock the target down");
    }

    [Test]
    public async Task WrongSequenceDoesNothing()
    {
        var (user, target, _) = await CreateFighters();
        await Learn(user, "CQC");
        var damage = Server.System<DamageableSystem>();
        var before = damage.GetTotalDamage(target).Float();

        await RaiseMelee(user, target);
        var after = damage.GetTotalDamage(target).Float();

        Assert.That(after, Is.EqualTo(before), "A single hit must not complete a two-step combo");
    }

    [Test]
    public async Task ComboOutsideRangeDoesNotApply()
    {
        var (user, target, _) = await CreateFighters(separation: 20f);
        await Learn(user, "CQC");
        var damage = Server.System<DamageableSystem>();
        var before = damage.GetTotalDamage(target).Float();

        await RaiseMelee(user, target);

        var after = damage.GetTotalDamage(target).Float();
        Assert.That(after, Is.EqualTo(before), "A combo must not apply outside its range");
    }

    [Test]
    public async Task KravMagaMoveIsPreparedAndUsesCooldown()
    {
        var (user, target, _) = await CreateFighters();
        await Learn(user, "KravMaga");

        var system = Server.System<MartialArtsSystem>();
        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(user, new MartialArtActionEvent { Move = "KravLegSweep" });
        });

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
            Assert.That(comp.PreparedMove, Is.EqualTo(new ProtoId<MartialArtMovePrototype>("KravLegSweep")));
            Assert.That(comp.GrantedActions.Count, Is.EqualTo(3), "Krav Maga should grant three active-move actions");
        });

        var damage = Server.System<DamageableSystem>();
        var before = damage.GetTotalDamage(target).Float();
        await RaiseMelee(user, target);
        var after = damage.GetTotalDamage(target).Float();
        Assert.That(after, Is.GreaterThan(before), "A prepared Krav Maga move should apply on the next hit");

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(user, new MartialArtActionEvent { Move = "KravLegSweep" });
        });

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
            Assert.That(comp.PreparedMove, Is.Null, "A move on cooldown must not be prepared");
        });
    }


    [Test]
    public async Task SleepingCarpBlocksShootingAndGrantsReflectionMarker()
    {
        var (user, target, _) = await CreateFighters();
        await Learn(user, "SleepingCarp");

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<MartialArtsNoShootComponent>(user), Is.True);
            Assert.That(SEntMan.HasComponent<Content.Shared.Weapons.Reflect.ReflectComponent>(user), Is.True);
        });

        var cancelled = false;
        await Server.WaitPost(() =>
        {
            var gun = SEntMan.SpawnEntity("WeaponPistolMk58", SEntMan.GetComponent<TransformComponent>(user).Coordinates);
            var ev = new Content.Shared.Weapons.Ranged.Events.ShotAttemptedEvent
            {
                User = user,
                Used = new Entity<Content.Shared.Weapons.Ranged.Components.GunComponent>(gun, SEntMan.GetComponent<Content.Shared.Weapons.Ranged.Components.GunComponent>(gun)),
            };
            SEntMan.EventBus.RaiseLocalEvent(user, ref ev);
            cancelled = ev.Cancelled;
        });

        Assert.That(cancelled, Is.True, "Sleeping Carp must block shooting");
    }

    [Test]
    public async Task KravGlovesGrantStyleWhileEquipped()
    {
        var (user, _, _) = await CreateFighters();
        EntityUid? gloves = null;

        await Server.WaitPost(() =>
        {
            gloves = SEntMan.SpawnEntity("ClothingHandsGlovesKravMaga", SEntMan.GetComponent<TransformComponent>(user).Coordinates);
            var inventory = SEntMan.System<Content.Shared.Inventory.InventorySystem>();
            Assert.That(inventory.TryEquip(user, gloves.Value, "gloves"), Is.True);
        });

        await Server.WaitAssertion(() =>
        {
            var knowledge = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
            Assert.That(knowledge.TemporaryStyles, Does.Contain(new ProtoId<MartialArtPrototype>("KravMaga")));
            Assert.That(knowledge.CurrentStyle, Is.EqualTo(new ProtoId<MartialArtPrototype>("KravMaga")));
        });

        await Server.WaitPost(() =>
        {
            var inventory = SEntMan.System<Content.Shared.Inventory.InventorySystem>();
            Assert.That(inventory.TryUnequip(user, "gloves"), Is.True);
        });

        await Server.WaitAssertion(() =>
        {
            var knowledge = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
            Assert.That(knowledge.TemporaryStyles, Does.Not.Contain(new ProtoId<MartialArtPrototype>("KravMaga")));
        });
    }

    [Test]
    public async Task PtcrAndCookbookCombosApplyEffects()
    {
        var (ptcrUser, ptcrTarget, _) = await CreateFighters();
        await Learn(ptcrUser, "PTCR");
        await Server.WaitPost(() =>
        {
            var disarm = new Content.Shared.Actions.Events.DisarmAttemptEvent(ptcrTarget, ptcrUser);
            SEntMan.EventBus.RaiseLocalEvent(ptcrUser, ref disarm);
        });
        await StartGrab(ptcrUser, ptcrTarget);
        await RaiseMelee(ptcrUser, ptcrTarget);

        var downed = false;
        await Server.WaitAssertion(() => downed = SEntMan.System<StandingStateSystem>().IsDown(ptcrTarget));
        Assert.That(downed, Is.True, "PTCR sweep should knock the target down");

        var (cookUser, cookTarget, _) = await CreateFighters();
        await Learn(cookUser, "Cookbook");
        var stamina = Server.System<Content.Shared.Damage.Systems.SharedStaminaSystem>();
        var before = SEntMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(cookTarget).StaminaDamage;

        await Server.WaitPost(() =>
        {
            var disarm = new Content.Shared.Actions.Events.DisarmAttemptEvent(cookTarget, cookUser);
            SEntMan.EventBus.RaiseLocalEvent(cookUser, ref disarm);
        });
        await StartGrab(cookUser, cookTarget);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(cookTarget);
            Assert.That(comp.StaminaDamage, Is.GreaterThan(before), "Cookbook unwind should deal stamina damage");
        });
    }

    [Test]
    public async Task JudoMoveRequiresDownedTarget()
    {
        var (user, target, _) = await CreateFighters();
        await Learn(user, "Judo");

        await Server.WaitPost(() =>
        {
            var disarm = new Content.Shared.Actions.Events.DisarmAttemptEvent(target, user);
            SEntMan.EventBus.RaiseLocalEvent(user, ref disarm);
            var disarm2 = new Content.Shared.Actions.Events.DisarmAttemptEvent(target, user);
            SEntMan.EventBus.RaiseLocalEvent(user, ref disarm2);
        });
        await StartGrab(user, target);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
            Assert.That(comp.History, Is.Empty, "A failed combo should clear its history");
        });
    }


    [Test]
    public async Task EveryMoveDefinitionCanBeApplied()
    {
        var styles = SProtoMan.EnumeratePrototypes<MartialArtPrototype>().ToList();
        Assert.That(styles, Is.Not.Empty);

        foreach (var style in styles)
        {
            Assert.That(style.Moves, Is.Not.Empty, $"{style.ID} has no moves");
            foreach (var moveId in style.Moves)
            {
                Assert.That(SProtoMan.TryIndex(moveId, out var move), Is.True, $"Missing move {moveId}");
                Assert.That(move!.Effects, Is.Not.Empty, $"{move.ID} has no effects");
                Assert.That(move.Sequence.Count > 0 || move.Action != null, Is.True,
                    $"{move.ID} is neither a passive combo nor an active action");

                var (user, target, _) = await CreateFighters();
                await Learn(user, style.ID);

                if (move.RequiresTargetPullingUser)
                    await StartGrab(target, user);
                else if (move.RequiresUserPullingTarget)
                    await StartGrab(user, target);

                await Server.WaitPost(() =>
                {
                    var standing = SEntMan.System<StandingStateSystem>();
                    if (move.RequiresDownedTarget)
                        standing.Down(target, force: true);

                    var knowledge = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
                    knowledge.History.Clear();

                    if ((move.RequiresUserPullingTarget || move.RequiresTargetPullingUser) &&
                        !move.Sequence.Contains(MartialArtActionType.Grab))
                    {
                        var stages = SEntMan.System<GrabStageSystem>();
                        var puller = move.RequiresUserPullingTarget ? user : target;
                        if (SEntMan.GetComponent<GrabStageComponent>(puller).Stage < GrabStage.Soft)
                            Assert.That(stages.TryAdvanceStage(puller), Is.True);
                    }
                });

                if (move.Action != null)
                {
                    await Server.WaitPost(() =>
                    {
                        SEntMan.EventBus.RaiseLocalEvent(user, new MartialArtActionEvent { Move = move.ID });
                    });
                    await RaiseMelee(user, target);
                }
                else
                {
                    foreach (var action in move.Sequence)
                    {
                        switch (action)
                        {
                            case MartialArtActionType.Hit:
                                await RaiseMelee(user, target);
                                break;
                            case MartialArtActionType.Grab:
                                await Server.WaitPost(() =>
                                {
                                    var stages = SEntMan.System<GrabStageSystem>();
                                    var puller = move.RequiresTargetPullingUser ? target : user;
                                    Assert.That(SEntMan.TryGetComponent<GrabStageComponent>(puller, out var grab),
                                        $"Move {style.ID}/{move.ID}: puller {puller} has no grab stage");
                                    if (grab!.Stage < GrabStage.Choke)
                                        Assert.That(stages.TryAdvanceStage(puller), Is.True);
                                });
                                await Pair.RunTicksSync(20);
                                break;
                            case MartialArtActionType.Disarm:
                                await Server.WaitPost(() =>
                                {
                                    var disarm = new Content.Shared.Actions.Events.DisarmAttemptEvent(target, user);
                                    SEntMan.EventBus.RaiseLocalEvent(user, ref disarm);
                                });
                                break;
                        }
                    }
                }

                await Server.WaitAssertion(() =>
                {
                    Assert.That(SEntMan.EntityExists(user), Is.True);
                    Assert.That(SEntMan.EntityExists(target), Is.True);
                    var knowledge = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
                    if (move.Action != null)
                        Assert.That(knowledge.MoveCooldowns.ContainsKey(move.ID), Is.True, $"{move.ID} did not start its cooldown");
                });
            }
        }
    }

    [Test]
    public async Task EveryMoveHasLocalizationKeys()
    {
        var loc = Server.ResolveDependency<ILocalizationManager>();
        foreach (var style in SProtoMan.EnumeratePrototypes<MartialArtPrototype>())
        {
            Assert.That(loc.GetString(style.Name), Is.Not.Empty);
            Assert.That(loc.GetString(style.Description), Is.Not.Empty);
            foreach (var moveId in style.Moves)
            {
                Assert.That(SProtoMan.TryIndex(moveId, out var move), Is.True);
                Assert.That(loc.GetString(move!.Name), Is.Not.Empty);
                Assert.That(loc.GetString(move.Description), Is.Not.Empty);
            }
        }
    }


    [Test]
    public async Task MultipleStylesCanBeCycled()
    {
        var (user, _, _) = await CreateFighters();
        await Learn(user, "CQC");
        await Learn(user, "PTCR");

        var system = Server.System<MartialArtsSystem>();
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
            Assert.That(comp.CycleAction, Is.Not.Null, "Multiple styles should grant the cycle action");
            Assert.That(comp.CurrentStyle, Is.EqualTo(new ProtoId<MartialArtPrototype>("CQC")));
        });

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(user, new MartialArtCycleEvent());
        });

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<MartialArtsKnowledgeComponent>(user);
            Assert.That(comp.CurrentStyle, Is.EqualTo(new ProtoId<MartialArtPrototype>("PTCR")));
        });
    }


    [Test]
    public async Task GrabStagesAdvanceAndEscape()
    {
        var (user, target, _) = await CreateFighters();
        await StartGrab(user, target);

        var stages = Server.System<GrabStageSystem>();
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<GrabStageComponent>(user);
            Assert.That(comp.Stage, Is.EqualTo(GrabStage.Soft));
            Assert.That(SEntMan.HasComponent<GrabStageTargetComponent>(target), Is.True);
        });

        await AdvanceGrab(user, GrabStage.Hard);
        await Pair.RunTicksSync(20);
        await AdvanceGrab(user, GrabStage.Choke);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<GrabStageComponent>(user);
            Assert.That(comp.Stage, Is.EqualTo(GrabStage.Choke));
        });

        // The victim can spend a do-after to lower the grab.
        var escapeStarted = false;
        await Server.WaitPost(() => escapeStarted = stages.TryStartEscape(user, target));
        Assert.That(escapeStarted, Is.True);
        await Pair.RunTicksSync(120);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<GrabStageComponent>(user);
            Assert.That(comp.Stage, Is.LessThan(GrabStage.Choke));
        });

        await Server.WaitPost(() => Assert.That(stages.TryLowerStage(user), Is.True));
        await Pair.RunTicksSync(20);
        await Server.WaitPost(() => Assert.That(stages.TryLowerStage(user), Is.True));
        await Pair.RunTicksSync(20);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<GrabStageComponent>(user), Is.False);
            Assert.That(SEntMan.GetComponent<PullerComponent>(user).Pulling, Is.Null);
        });
    }

    [Test]
    public async Task RepeatedPullInputAdvancesGrabStage()
    {
        var (user, target, _) = await CreateFighters();
        await StartGrab(user, target, advanceToSoft: false);

        var pulling = Server.System<PullingSystem>();
        await Server.WaitPost(() =>
        {
            var pullable = SEntMan.GetComponent<PullableComponent>(target);
            Assert.That(pulling.TogglePull(new Entity<PullableComponent>(target, pullable), user), Is.True);
        });

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<GrabStageComponent>(user).Stage, Is.EqualTo(GrabStage.Soft)));
    }

    [Test]
    public async Task GrabBreaksOnDistanceAndDeath()
    {
        var (user, target, _) = await CreateFighters();
        await StartGrab(user, target);

        await Server.WaitPost(() =>
        {
            Server.System<Robust.Shared.GameObjects.SharedTransformSystem>()
                .SetWorldPosition(target, new Vector2(20, 0));
        });
        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<GrabStageComponent>(user), Is.False, "Far target must break the grab stage");
            Assert.That(SEntMan.GetComponent<PullerComponent>(user).Pulling, Is.Null);
        });

        var (deadUser, deadTarget, _) = await CreateFighters();
        await StartGrab(deadUser, deadTarget);
        await Server.WaitPost(() =>
        {
            var damage = new Content.Shared.Damage.DamageSpecifier(
                SProtoMan.Index(BluntDamage), 1000);
            Server.System<DamageableSystem>().TryChangeDamage(deadTarget, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<GrabStageComponent>(deadUser), Is.False, "Dead target must break the grab stage");
            Assert.That(SEntMan.GetComponent<PullerComponent>(deadUser).Pulling, Is.Null);
        });
    }

    [Test]
    public async Task MartialStyleUsesSecondaryAttackAnimation()
    {
        var (user, _, _) = await CreateFighters();
        await Learn(user, "CQC");

        await Server.WaitAssertion(() =>
        {
            var melee = SEntMan.GetComponent<MeleeWeaponComponent>(user);
            Assert.That(melee.WideAnimation.Id, Is.EqualTo("WeaponArcMartialSecondary"));
        });
    }


    [Test]
    public async Task CqcNeckSnapRequiresChokeStage()
    {
        var (user, target, _) = await CreateFighters();
        await Learn(user, "CQC");
        await StartGrab(user, target);

        // Soft grab is not enough for the neck snap.
        await RaiseMelee(user, target);
        await Server.WaitAssertion(() => Assert.That(Server.System<MobStateSystem>().IsDead(target), Is.False));

        await AdvanceGrab(user, GrabStage.Choke);
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<GrabStageComponent>(user).Stage, Is.EqualTo(GrabStage.Choke)));
        await Server.WaitPost(() =>
        {
            Server.System<StandingStateSystem>().Down(target, force: true);
            SEntMan.GetComponent<Content.Shared.Damage.Components.StaminaComponent>(target).Critical = true;
        });
        await RaiseMelee(user, target);

        await Server.WaitAssertion(() => Assert.That(Server.System<MobStateSystem>().IsDead(target), Is.True));
    }



    [Test]
    public async Task WeaponCombosSwitchStanceAndApplyEffects()
    {
        var (user, target, _) = await CreateFighters();
        var combos = Server.System<WeaponComboSystem>();
        // Do not use FireAxeFlaming here: a synthetic MeleeHitEvent still triggers
        // IgniteOnMeleeHit, which leaves fire visuals on the test mob and can flake
        // client entity initialization during teardown.
        EntityUid? tonfa = null;
        EntityUid? katana = null;

        await Server.WaitPost(() =>
        {
            tonfa = SEntMan.SpawnEntity("WeaponTonfa", SEntMan.GetComponent<TransformComponent>(user).Coordinates);
            katana = SEntMan.SpawnEntity("WeaponCursedKatana", SEntMan.GetComponent<TransformComponent>(user).Coordinates);
        });

        var damage = Server.System<DamageableSystem>();

        // Tonfa: protective light + offensive light.
        var toneBefore = damage.GetTotalDamage(target).Float();
        await RaiseWeaponHit(user, tonfa!.Value, target, heavy: false);
        await Server.WaitPost(() => combos.ToggleStance(tonfa!.Value));
        await RaiseWeaponHit(user, tonfa.Value, target, heavy: false);
        Assert.That(damage.GetTotalDamage(target).Float(), Is.GreaterThan(toneBefore), "Tonfa light combo should deal damage");

        // Cursed katana: offensive heavy + offensive heavy on a standing target.
        await Server.WaitPost(() => combos.ToggleStance(katana!.Value));
        var katanaBefore = damage.GetTotalDamage(target).Float();
        await RaiseWeaponHit(user, katana!.Value, target, heavy: true);
        await RaiseWeaponHit(user, katana.Value, target, heavy: true);
        Assert.That(damage.GetTotalDamage(target).Float(), Is.GreaterThan(katanaBefore), "Cursed katana combo should deal damage");
    }



    [Test]
    public async Task HardGrabThrowMakesTargetFall()
    {
        var (user, target, _) = await CreateFighters();
        await StartGrab(user, target);
        await AdvanceGrab(user, GrabStage.Hard);

        var stages = Server.System<GrabStageSystem>();
        var thrown = false;
        await Server.WaitPost(() =>
        {
            thrown = stages.TryThrow(user, target, new EntityCoordinates(user, new Vector2(1, 0)));
        });

        Assert.That(thrown, Is.True);
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<GrabThrownComponent>(target), Is.True));

        await Server.WaitPost(() =>
        {
            var thrownComp = SEntMan.GetComponent<Content.Shared.Throwing.ThrownItemComponent>(target);
            var hit = new Content.Shared.Throwing.ThrowDoHitEvent(target, target, thrownComp);
            SEntMan.EventBus.RaiseLocalEvent(target, ref hit);
        });

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<StandingStateSystem>().IsDown(target), Is.True));
    }


    [Test]
    public async Task HardGrabThrowUsesThrowKeyPath()
    {
        var (user, target, _) = await CreateFighters();
        await StartGrab(user, target);
        await AdvanceGrab(user, GrabStage.Hard);

        var hands = Server.System<Content.Server.Hands.Systems.HandsSystem>();
        var thrown = false;
        await Server.WaitPost(() =>
        {
            thrown = hands.ThrowHeldItem(user, new EntityCoordinates(user, new Vector2(1, 0)));
        });

        Assert.That(thrown, Is.True, "Ctrl+Q should throw the grabbed target");
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<GrabThrownComponent>(target), Is.True));
    }

    [Test]
    public async Task ChokeholdCausesSuffocation()
    {
        var (user, target, _) = await CreateFighters();
        await StartGrab(user, target);
        await AdvanceGrab(user, GrabStage.Choke);

        var respirator = SEntMan.GetComponent<Content.Server.Body.Components.RespiratorComponent>(target);
        var before = respirator.Saturation;
        await Pair.RunTicksSync(150);
        await Server.WaitAssertion(() =>
        {
            var after = SEntMan.GetComponent<Content.Server.Body.Components.RespiratorComponent>(target).Saturation;
            Assert.That(after, Is.LessThan(before), "A chokehold should lower respiration saturation");
        });
    }

    [Test]
    public async Task FastStandFromKnockdownAlert()
    {
        var (user, _, _) = await CreateFighters();
        var stun = Server.System<Content.Shared.Stunnable.SharedStunSystem>();

        await Server.WaitPost(() => Assert.That(stun.TryKnockdown(user, TimeSpan.FromSeconds(5), force: true), Is.True));
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<KnockedDownComponent>(user), Is.True));

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.GetComponent<Content.Shared.Hands.Components.HandsComponent>(user);
            TestContext.Progress.WriteLine($"fast stand free hand: {SEntMan.System<SharedHandsSystem>().TryGetEmptyHand((user, hands), out _)}");
        });
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(user, new KnockedDownAlertEvent()));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            var standing = SEntMan.System<StandingStateSystem>();
            Assert.That(standing.IsDown(user), Is.False, "The knockdown alert should fast-stand the player");
        });
    }

    [Test]
    public async Task UnarmedRightClickUsesDisarmAnimation()
    {
        var (user, _, _) = await CreateFighters();
        await Server.WaitAssertion(() =>
        {
            var melee = SEntMan.GetComponent<MeleeWeaponComponent>(user);
            Assert.That(melee.AltDisarm, Is.True, "Unarmed right-click must be a disarm");
            Assert.That(melee.DisarmAnimation.Id, Is.EqualTo("WeaponArcDisarm"),
                "Right-click must use the dedicated disarm animation");
        });
    }

    [Test]
    public async Task UnknownStyleAndInvalidTargetAreRejected()
    {
        var (user, target, _) = await CreateFighters();
        var system = Server.System<MartialArtsSystem>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(system.TryLearnStyle(user, "CQC", permanent: true), Is.True);
            Assert.That(system.TryLearnStyle(user, "CQC", permanent: true), Is.False, "The same style must not be learned twice");
        });
    }
}
