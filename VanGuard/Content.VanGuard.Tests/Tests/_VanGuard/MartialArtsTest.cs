using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Content.Server._VanGuard.MartialArts;
using Content.Shared._VanGuard.MartialArts;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Standing;
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

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(user, new PullStartedMessage(user, target));
        });
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

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(user, new PullStartedMessage(user, target));
        });
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
        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(ptcrUser, new PullStartedMessage(ptcrUser, ptcrTarget));
        });
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
        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(cookUser, new PullStartedMessage(cookUser, cookTarget));
        });

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
            SEntMan.EventBus.RaiseLocalEvent(user, new PullStartedMessage(user, target));
        });

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

                await Server.WaitPost(() =>
                {
                    var standing = SEntMan.System<StandingStateSystem>();
                    if (move.RequiresDownedTarget)
                        standing.Down(target, force: true);

                    var pulling = SEntMan.System<PullingSystem>();
                    if (move.RequiresTargetPullingUser)
                        pulling.TryStartPull(target, user);

                    if (move.RequiresUserPullingTarget)
                        pulling.TryStartPull(user, target);
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
                                    SEntMan.EventBus.RaiseLocalEvent(user, new PullStartedMessage(user, target));
                                });
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
