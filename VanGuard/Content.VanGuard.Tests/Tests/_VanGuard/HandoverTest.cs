using System.Linq;
using System.Numerics;
using Content.Server._VanGuard.Handover;
using Content.Server.Mind;
using Content.Client.Alerts;
using Content.Client.Popups;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction.Events;
using Content.Shared.Polymorph.Components;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Content.Shared.Weapons.Melee;
using Content.Shared._VanGuard.Handover;
using Content.Shared.Alert;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Standing;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard;

[TestFixture]
[NonParallelizable]
public sealed class HandoverTest : GameTest
{
    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        Dirty = true,
    };

    private async Task<(EntityUid User, EntityUid Target, EntityUid Item, MapId Map)> CreateHandover()
    {
        EntityUid? user = null;
        EntityUid? target = null;
        EntityUid? item = null;
        var mapId = MapId.Nullspace;

        await Server.WaitPost(() =>
        {
            var map = Server.System<SharedMapSystem>();
            map.CreateMap(out mapId);
            user = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(Vector2.Zero, mapId));
            target = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(1, 0), mapId));
            item = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(user.Value).Coordinates);
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user.Value, item.Value, checkActionBlocker: false), Is.True);
        });

        return (user!.Value, target!.Value, item!.Value, mapId);
    }

    private async Task AttachSession(EntityUid entity)
    {
        await Server.WaitPost(() =>
            Assert.That(Server.PlayerMan.SetAttachedEntity(ServerSession!, entity, force: true), Is.True));
    }

    private async Task ClickOfferAlert()
    {
        await Client.WaitPost(() =>
        {
            ProtoId<AlertPrototype> alertId = "Offer";
            Client.System<ClientAlertsSystem>().AlertClicked(alertId);
        });
        await Pair.RunTicksSync(3);
    }

    private async Task StartOffer(EntityUid user, EntityUid target)
    {
        var system = Server.System<HandoverSystem>();
        await Server.WaitPost(() => Assert.That(system.TryToggleTargeting(user), Is.True));
        await Server.WaitPost(() => Assert.That(system.TryCreateOffer(user, target), Is.True));
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.True));
    }

    [Test]
    public async Task TwoFreeHandsTransfersItem()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        var system = Server.System<HandoverSystem>();
        var offerId = SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId;
        await Server.WaitPost(() => Assert.That(system.TryAccept(target, offerId), Is.True));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.False);
        });
    }

    [Test]
    public async Task OneFreeNonActiveHandReceivesItem()
    {
        var (user, target, item, _) = await CreateHandover();
        EntityUid? filler = null;

        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var targetHands = SEntMan.GetComponent<HandsComponent>(target);
            var active = targetHands.ActiveHandId!;
            filler = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            Assert.That(hands.TryPickup(target, filler.Value, active, checkActionBlocker: false), Is.True);
        });

        await StartOffer(user, target);
        var system = Server.System<HandoverSystem>();
        var offerId = SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId;
        await Server.WaitPost(() => Assert.That(system.TryAccept(target, offerId), Is.True));

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True));
    }


    [Test]
    public async Task AlertClickImmediatelyTransfersItem()
    {
        var (user, target, item, _) = await CreateHandover();
        await AttachSession(target);
        await StartOffer(user, target);

        await ClickOfferAlert();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.False);
        });
    }

    [Test]
    public async Task RepeatedAlertClickDoesNotDuplicateOrThrow()
    {
        var (user, target, item, _) = await CreateHandover();
        await AttachSession(target);
        await StartOffer(user, target);

        await ClickOfferAlert();
        await ClickOfferAlert();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
        });
    }

    [Test]
    public async Task FullHandsRejectOfferWithoutLosingItem()
    {
        var (user, target, item, _) = await CreateHandover();

        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var targetHands = SEntMan.GetComponent<HandsComponent>(target);
            foreach (var hand in targetHands.SortedHands)
            {
                var filler = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(target).Coordinates);
                Assert.That(hands.TryPickup(target, filler, hand, checkActionBlocker: false), Is.True);
            }
        });

        var system = Server.System<HandoverSystem>();
        await Server.WaitPost(() => Assert.That(system.TryToggleTargeting(user), Is.True));
        await Server.WaitPost(() => Assert.That(system.TryCreateOffer(user, target), Is.False));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
        });
    }



    [Test]
    public async Task FullHandsAlertKeepsOfferUntilHandFreed()
    {
        var (user, target, item, _) = await CreateHandover();
        await AttachSession(target);
        await StartOffer(user, target);

        EntityUid? firstFiller = null;
        EntityUid? secondFiller = null;
        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var targetHands = SEntMan.GetComponent<HandsComponent>(target);
            var sorted = targetHands.SortedHands;
            firstFiller = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            secondFiller = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            Assert.That(hands.TryPickup(target, firstFiller.Value, sorted[0], checkActionBlocker: false), Is.True);
            Assert.That(hands.TryPickup(target, secondFiller.Value, sorted[1], checkActionBlocker: false), Is.True);
        });

        await ClickOfferAlert();
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.True);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.True);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.True);
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.True);
        });

        await Server.WaitPost(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryDrop(target, firstFiller!.Value), Is.True);
        });
        await ClickOfferAlert();
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True));
    }

    [Test]
    public async Task SecondOfferToBusyReceiverIsRejected()
    {
        var (user, target, _, mapId) = await CreateHandover();
        await StartOffer(user, target);

        EntityUid? secondUser = null;
        EntityUid? secondItem = null;
        await Server.WaitPost(() =>
        {
            secondUser = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(-1, 0), mapId));
            secondItem = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(secondUser.Value).Coordinates);
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(secondUser.Value, secondItem.Value, checkActionBlocker: false), Is.True);
        });

        var system = Server.System<HandoverSystem>();
        await Server.WaitPost(() => Assert.That(system.TryToggleTargeting(secondUser!.Value), Is.True));
        await Server.WaitPost(() => Assert.That(system.TryCreateOffer(secondUser!.Value, target), Is.False));
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<HandoverReceiverComponent>(target).Offerer, Is.EqualTo(user)));
    }

    [Test]
    public async Task MovingAwayCancelsOffer()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            Server.System<Robust.Shared.GameObjects.SharedTransformSystem>()
                .SetWorldPosition(target, new Vector2(20, 0)));
        await Pair.RunTicksSync(20);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }

    [Test]
    public async Task DeclineReturnsControlAndKeepsItem()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        var system = Server.System<HandoverSystem>();
        await Server.WaitPost(() => Assert.That(system.TryToggleTargeting(target), Is.True));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
        });
    }

    [Test]
    public async Task RepeatFWhileTargetingKeepsItem()
    {
        var (user, _, item, _) = await CreateHandover();
        var system = Server.System<HandoverSystem>();

        await Server.WaitPost(() => Assert.That(system.TryToggleTargeting(user), Is.True));
        await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<HandoverOfferComponent>(user).IsTargeting, Is.True));
        await Server.WaitPost(() => Assert.That(system.TryToggleTargeting(user), Is.True));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }

    [Test]
    public async Task ItemLockPreventsDropAndPickup()
    {
        var (user, target, item, mapId) = await CreateHandover();
        await StartOffer(user, target);

        EntityUid? third = null;
        await Server.WaitPost(() =>
            third = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(1, 0), mapId)));

        var hands = Server.System<SharedHandsSystem>();
        await Server.WaitPost(() =>
        {
            Assert.That(hands.TryDrop(user, item), Is.False);
            Assert.That(hands.TryPickupAnyHand(third!.Value, item, checkActionBlocker: false), Is.False);
        });
    }


    [Test]
    public async Task ExpiredOfferCleansUp()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitAssertion(() => SEntMan.GetComponent<HandoverOfferComponent>(user).ExpiresAt = TimeSpan.Zero);
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }




    [Test]
    public async Task ExpiredAlertClickDoesNotTransfer()
    {
        var (user, target, item, _) = await CreateHandover();
        await AttachSession(target);
        await StartOffer(user, target);

        await Server.WaitAssertion(() => SEntMan.GetComponent<HandoverOfferComponent>(user).ExpiresAt = TimeSpan.Zero);
        await ClickOfferAlert();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
        });
    }

    [Test]
    public async Task RemovingOfferComponentCleansReceiverAndLock()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() => SEntMan.RemoveComponent<HandoverOfferComponent>(user));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.False);
        });
    }

    [Test]
    public async Task RemovingReceiverComponentCleansOfferAndLock()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() => SEntMan.RemoveComponent<HandoverReceiverComponent>(target));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.False);
        });
    }

    [Test]
    public async Task SourceDeathCancelsOfferAndDoesNotLoseItem()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() =>
        {
            var damage = new Content.Shared.Damage.DamageSpecifier(
                SProtoMan.Index(new ProtoId<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt")), 200);
            Server.System<Content.Shared.Damage.Systems.DamageableSystem>()
                .TryChangeDamage(user, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.EntityExists(item), Is.True);
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.False);
        });
    }

    [Test]
    public async Task DeathCancelsOfferAndKeepsItem()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() =>
        {
            var damage = new Content.Shared.Damage.DamageSpecifier(
                SProtoMan.Index(new ProtoId<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Blunt")), 200);
            Server.System<Content.Shared.Damage.Systems.DamageableSystem>()
                .TryChangeDamage(target, damage, ignoreResistances: true);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }


    [Test]
    public async Task StunCancelsOffer()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            Server.System<Content.Shared.Stunnable.SharedStunSystem>()
                .TryAddStunDuration(target, TimeSpan.FromSeconds(2)));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }


    [Test]
    public async Task RepeatedAcceptIsRejected()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        var system = Server.System<HandoverSystem>();
        var offerId = SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId;
        await Server.WaitPost(() =>
        {
            Assert.That(system.TryAccept(target, offerId), Is.True);
            Assert.That(system.TryAccept(target, offerId), Is.False);
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.False);
        });
    }

    [Test]
    public async Task SimultaneousAcceptRequestsOnlyOneSucceeds()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        var system = Server.System<HandoverSystem>();
        var offerId = SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId;
        var first = false;
        var second = false;
        await Server.WaitPost(() =>
        {
            first = system.TryAccept(target, offerId);
            second = system.TryAccept(target, offerId);
        });

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.False);
        });
    }

    [Test]
    public async Task StaleAcceptAfterTransferDoesNotDuplicate()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        var system = Server.System<HandoverSystem>();
        var offerId = SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId;
        await Server.WaitPost(() => Assert.That(system.TryAccept(target, offerId), Is.True));
        await Server.WaitPost(() => Assert.That(system.TryAccept(target, offerId), Is.False));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.False);
        });
    }

    [Test]
    public async Task ExternalLockRemovalCancelsOffer()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() => SEntMan.RemoveComponent<HandoverLockComponent>(item));
        await Pair.RunTicksSync(15);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }

    [Test]
    public async Task ObstacleAfterOfferCancelsOffer()
    {
        var (user, target, item, mapId) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            SEntMan.SpawnEntity("WallSolid", new MapCoordinates(new Vector2(0.5f, 0), mapId)));
        await Pair.RunTicksSync(20);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }

    [Test]
    public async Task UseThrowAttackAndContainerRemovalAreBlocked()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitAssertion(() =>
        {
            var blocker = Server.System<ActionBlockerSystem>();
            var containers = Server.System<SharedContainerSystem>();
            var hands = SEntMan.GetComponent<HandsComponent>(user);

            Assert.That(blocker.CanUseHeldEntity(user, item), Is.False);
            Assert.That(blocker.CanThrow(user, item), Is.False);
            Assert.That(containers.TryGetContainer(user, hands.ActiveHandId!, out var handContainer), Is.True);
            Assert.That(containers.CanRemove(item, handContainer!), Is.False);

            Assert.That(SEntMan.TryGetComponent<MeleeWeaponComponent>(item, out var melee), Is.True);
            var attack = new AttackAttemptEvent(user, target, new Entity<MeleeWeaponComponent>(item, melee));
            SEntMan.EventBus.RaiseLocalEvent(user, attack);
            Assert.That(attack.Cancelled, Is.True);
        });
    }

    [Test]
    public async Task FailedPickupRestoresOfferAndLock()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() => SEntMan.AddComponent<ChameleonDisguiseComponent>(item));

        var system = Server.System<HandoverSystem>();
        var offerId = SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId;
        await Server.WaitPost(() => Assert.That(system.TryAccept(target, offerId), Is.False));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.True);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.True);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.True);
        });

        await Server.WaitPost(() => SEntMan.RemoveComponent<ChameleonDisguiseComponent>(item));
        await Server.WaitPost(() => Assert.That(system.TryAccept(target, offerId), Is.True));
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, item), Is.True));
    }

    [Test]
    public async Task SenderDisconnectCancelsOffer()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(user, new PlayerDetachedEvent(user, ServerSession!)));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }

    [Test]
    public async Task ReceiverDisconnectCancelsOffer()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(target, new PlayerDetachedEvent(target, ServerSession!)));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }


    [Test]
    public async Task ContainerInsertionIsBlocked()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        EntityUid? backpack = null;
        await Server.WaitPost(() =>
            backpack = SEntMan.SpawnEntity("ClothingBackpack", SEntMan.GetComponent<TransformComponent>(user).Coordinates));

        await Server.WaitAssertion(() =>
        {
            var containers = Server.System<SharedContainerSystem>();
            var storage = SEntMan.GetComponent<StorageComponent>(backpack!.Value);
            Assert.That(containers.Insert(item, storage.Container), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.True);
        });
    }

    [Test]
    public async Task ReceiverHandsRemovalCancelsOffer()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() => SEntMan.RemoveComponent<HandsComponent>(target));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }


    [Test]
    public async Task SourceDeletionEndsOfferWithoutLock()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() => SEntMan.QueueDeleteEntity(user));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.False);
        });
    }

    [Test]
    public async Task ReceiverDeletionEndsOfferAndKeepsItem()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() => SEntMan.QueueDeleteEntity(target));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }


    [Test]
    public async Task SourceHandSwitchCancelsOffer()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() =>
        {
            var hands = SEntMan.GetComponent<HandsComponent>(user);
            var other = hands.SortedHands.First(hand => hand != hands.ActiveHandId);
            SEntMan.System<SharedHandsSystem>().SetActiveHand(user, other);
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }


    [Test]
    public async Task StaleOfferIdCannotAcceptNewOffer()
    {
        var (user, target, firstItem, _) = await CreateHandover();
        await StartOffer(user, target);

        var system = Server.System<HandoverSystem>();
        var firstOfferId = SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId;
        await Server.WaitPost(() => Assert.That(system.TryAccept(target, firstOfferId), Is.True));

        EntityUid? secondItem = null;
        await Server.WaitPost(() =>
        {
            secondItem = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(user).Coordinates);
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user, secondItem.Value, checkActionBlocker: false), Is.True);
        });

        await Server.WaitPost(() => Assert.That(system.TryToggleTargeting(user), Is.True));
        await Server.WaitPost(() => Assert.That(system.TryCreateOffer(user, target), Is.True));
        var secondOfferId = SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId;

        await Server.WaitPost(() => Assert.That(system.TryAccept(target, firstOfferId), Is.False));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<HandoverOfferComponent>(user).OfferId, Is.EqualTo(secondOfferId));
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, secondItem!.Value), Is.True);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(target, firstItem), Is.True);
        });
    }


    private async Task<EntityUid> SpawnBodyNear(EntityUid near, MapId map)
    {
        EntityUid? body = null;
        await Server.WaitPost(() =>
            body = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(2, 0), map)));
        return body!.Value;
    }

    [Test]
    public async Task SenderMindTransferCleansOfferAndDoesNotThrow()
    {
        var (user, target, item, map) = await CreateHandover();
        await AttachSession(user);
        await StartOffer(user, target);

        var newBody = await SpawnBodyNear(user, map);
        await Server.WaitPost(() =>
            Server.System<MindSystem>().ControlMob(ServerSession!.UserId, newBody));

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(newBody));
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(Server.System<AlertsSystem>().IsShowingAlert(target, "Offer"), Is.False);
        });
    }

    [Test]
    public async Task ReceiverMindTransferCleansOfferAndDoesNotThrow()
    {
        var (user, target, item, map) = await CreateHandover();
        await AttachSession(target);
        await StartOffer(user, target);

        var newBody = await SpawnBodyNear(target, map);
        await Server.WaitPost(() =>
            Server.System<MindSystem>().ControlMob(ServerSession!.UserId, newBody));

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(newBody));
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }

    [Test]
    public async Task SenderDisconnectViaManagerCleansOfferAndDoesNotThrow()
    {
        var (user, target, item, _) = await CreateHandover();
        await AttachSession(user);
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            Assert.That(Server.PlayerMan.SetAttachedEntity(ServerSession!, null, force: true), Is.True));

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession!.AttachedEntity, Is.Null);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
        });
    }

    [Test]
    public async Task ReceiverDisconnectViaManagerCleansOfferAndDoesNotThrow()
    {
        var (user, target, item, _) = await CreateHandover();
        await AttachSession(target);
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            Assert.That(Server.PlayerMan.SetAttachedEntity(ServerSession!, null, force: true), Is.True));

        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession!.AttachedEntity, Is.Null);
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverLockComponent>(item), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(user, item), Is.True);
        });
    }

    [Test]
    public async Task DetachedSourceStillNotifiesAttachedReceiver()
    {
        var (user, target, _, _) = await CreateHandover();
        await AttachSession(target);
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(user, new PlayerDetachedEvent(user, ServerSession!)));
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
        });

        await Client.WaitAssertion(() =>
        {
            var labels = Client.System<PopupSystem>().WorldLabels.Select(label => label.Text).ToList();
            Assert.That(labels, Does.Contain(Loc.GetString("handover-cancelled-target")));
            Assert.That(labels, Does.Not.Contain(Loc.GetString("handover-cancelled-source")));
        });
    }

    [Test]
    public async Task DetachedReceiverStillNotifiesAttachedSender()
    {
        var (user, target, _, _) = await CreateHandover();
        await AttachSession(user);
        await StartOffer(user, target);

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(target, new PlayerDetachedEvent(target, ServerSession!)));
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
        });

        await Client.WaitAssertion(() =>
        {
            var labels = Client.System<PopupSystem>().WorldLabels.Select(label => label.Text).ToList();
            Assert.That(labels, Does.Contain(Loc.GetString("handover-cancelled-source")));
            Assert.That(labels, Does.Not.Contain(Loc.GetString("handover-cancelled-target")));
        });
    }


    [Test]
    public async Task RepeatedDetachAfterCleanupIsSafe()
    {
        var (user, target, _, _) = await CreateHandover();
        await AttachSession(user);
        await StartOffer(user, target);

        await Server.WaitPost(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(user, new PlayerDetachedEvent(user, ServerSession!));
            SEntMan.EventBus.RaiseLocalEvent(user, new PlayerDetachedEvent(user, ServerSession!));
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
        });
    }


    [Test]
    public async Task ItemDeletionEndsOffer()
    {
        var (user, target, item, _) = await CreateHandover();
        await StartOffer(user, target);

        await Server.WaitPost(() => SEntMan.QueueDeleteEntity(item));
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<HandoverOfferComponent>(user), Is.False);
            Assert.That(SEntMan.HasComponent<HandoverReceiverComponent>(target), Is.False);
        });
    }
}
