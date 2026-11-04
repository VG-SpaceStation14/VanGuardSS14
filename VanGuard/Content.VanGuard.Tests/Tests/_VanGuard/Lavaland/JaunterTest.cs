#nullable enable
using System.Numerics;
using Content.Server._VanGuard.Lavaland;
using Content.Shared.Chasm;
using Content.Shared.Damage.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Content.IntegrationTests.Tests.Movement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Lavaland;

/// <summary>
/// End-to-end checks for the jaunter: real item use, real portal collision,
/// and walking into a chasm. These deliberately avoid calling the jaunter
/// system's internals.
/// </summary>
[TestFixture]
public sealed class JaunterTest : MovementTest
{
    private static readonly EntProtoId ChasmProto = "FloorChasmEntity";
    private static readonly EntProtoId BeaconProto = "WarpPointBeacon";
    private static readonly EntProtoId JaunterProto = "Jaunter";

    [Test]
    public async Task ChasmFallWithJaunterFallsFirstThenSaves()
    {
        var beacon = await SpawnBeaconNearPlayer();
        var jaunter = await PlaceInHands(JaunterProto);
        await SpawnTarget(ChasmProto);

        await Move(DirectionFlag.East, 0.5f);

        Assert.That(HasComp<ChasmFallingComponent>(Player), Is.True,
            "A player carrying a jaunter did not start falling into the chasm.");
        Assert.That(HasComp<JaunterComponent>(jaunter), Is.True,
            "The carried jaunter was consumed before the fall completed.");

        var beaconPos = Transform.GetWorldPosition(ToServer(beacon));
        var fallPos = Transform.GetWorldPosition(SPlayer);
        Assert.That((fallPos - beaconPos).Length(), Is.GreaterThan(1f),
            "The player was teleported at the start of the fall instead of falling first.");

        await Pair.RunSeconds(2.2f);

        Assert.That(HasComp<ChasmFallingComponent>(Player), Is.False,
            "The jaunter did not clear the falling state after the rescue.");
        Assert.That((Transform.GetWorldPosition(SPlayer) - beaconPos).Length(), Is.LessThan(1f),
            "The player was not rescued to the beacon after the fall completed.");
        Assert.That(SEntMan.EntityExists(ToServer(jaunter)), Is.False,
            "The jaunter was not consumed by the rescue.");
        Assert.That(SEntMan.TryGetComponent<StaminaComponent>(SPlayer, out var stamina), Is.True);
        Assert.That(stamina!.StaminaDamage, Is.GreaterThanOrEqualTo(stamina.CritThreshold),
            "The rescue did not push the player into stamina crit.");
    }

    [Test]
    public async Task ChasmFallWithoutJaunterDeletesPlayer()
    {
        await SpawnTarget(ChasmProto);
        await Move(DirectionFlag.East, 0.5f);

        Assert.That(HasComp<ChasmFallingComponent>(Player), Is.True);
        await Pair.RunSeconds(2.2f);

        AssertDeleted(Player);
    }

    [Test]
    public async Task UsedPortalTeleportsAndCollapses()
    {
        var beacon = await SpawnBeaconNearPlayer();
        var jaunter = await PlaceInHands(JaunterProto);

        await UseInHand();
        await Pair.RunTicksSync(3);

        Assert.That(SEntMan.EntityExists(ToServer(jaunter)), Is.False,
            "The jaunter was not consumed when the wormhole opened.");

        EntityUid portal = default;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<JaunterPortalComponent>();
            Assert.That(query.MoveNext(out portal, out _), Is.True, "No jaunter wormhole was created.");
            Assert.That(Server.System<LinkedEntitySystem>().GetLink(portal, out var target), Is.True);
            Assert.That(target, Is.EqualTo(ToServer(beacon)),
                "The wormhole was not linked to the station beacon.");
        });

        var portalPos = Transform.GetWorldPosition(portal);
        var playerPos = Transform.GetWorldPosition(SPlayer);
        Assert.That((portalPos - playerPos).Length(), Is.LessThanOrEqualTo(2.1f),
            "The wormhole was not created near the user.");
        Assert.That((portalPos - playerPos).Length(), Is.GreaterThan(0.1f),
            "The wormhole was spawned exactly on top of the user.");

        // Walk into the wormhole so the real portal collision fires.
        var beaconPos = Transform.GetWorldPosition(ToServer(beacon));
        for (var i = 0; i < 10; i++)
        {
            if ((Transform.GetWorldPosition(SPlayer) - beaconPos).Length() < 1f)
                break;

            var delta = Transform.GetWorldPosition(portal) - Transform.GetWorldPosition(SPlayer);
            var dir = DirectionFlag.None;
            if (delta.X > 0.15f) dir |= DirectionFlag.East;
            else if (delta.X < -0.15f) dir |= DirectionFlag.West;
            if (delta.Y > 0.15f) dir |= DirectionFlag.North;
            else if (delta.Y < -0.15f) dir |= DirectionFlag.South;

            if (dir == DirectionFlag.None)
            {
                await Pair.RunTicksSync(2);
                continue;
            }

            await Move(dir, 0.35f);
        }
        await Pair.RunTicksSync(3);

        Assert.That((Transform.GetWorldPosition(SPlayer) - beaconPos).Length(), Is.LessThan(1f),
            "Entering the wormhole did not teleport the player to the beacon.");
        Assert.That(SEntMan.TryGetComponent<StaminaComponent>(SPlayer, out var stamina), Is.True);
        Assert.That(stamina!.StaminaDamage, Is.GreaterThanOrEqualTo(stamina.CritThreshold),
            "Entering the wormhole did not empty the stamina bar.");

        // A used wormhole gets a short three-second life instead of its full 15.
        await Pair.RunSeconds(3.5f);
        Assert.That(SEntMan.EntityExists(portal), Is.False,
            "The used wormhole did not collapse after the traveller entered it.");
    }

    [Test]
    public async Task LavalandBeaconIsNotUsedAsDestination()
    {
        // A procedurally generated map must never substitute for the station.
        var planet = await CreatePlanetWithBeacon();

        var stationBeacon = await SpawnBeaconNearPlayer();
        await PlaceInHands(JaunterProto);
        await UseInHand();
        await Pair.RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<JaunterPortalComponent>();
            Assert.That(query.MoveNext(out var portal, out _), Is.True);
            Assert.That(Server.System<LinkedEntitySystem>().GetLink(portal, out var target), Is.True);
            Assert.That(target, Is.EqualTo(ToServer(stationBeacon)),
                "The jaunter linked to the Lavaland outpost beacon instead of the station beacon.");
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(planet));
    }

    [Test]
    public async Task NoStationBeaconCreatesLethalDeadEnd()
    {
        var planet = await CreatePlanetWithBeacon();

        var jaunter = await PlaceInHands(JaunterProto);
        await UseInHand();
        await Pair.RunTicksSync(3);

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.EntityExists(ToServer(jaunter)), Is.False);
            var query = SEntMan.EntityQueryEnumerator<JaunterKillPortalComponent>();
            Assert.That(query.MoveNext(out _, out _), Is.True,
                "With no valid destination the jaunter did not create a lethal dead-end wormhole.");
            var normal = SEntMan.EntityQueryEnumerator<JaunterPortalComponent>();
            Assert.That(normal.MoveNext(out _, out _), Is.False,
                "A normal wormhole was created even though only a Lavaland beacon existed.");
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(planet));
    }

    private async Task<EntityUid> CreatePlanetWithBeacon()
    {
        EntityUid map = default;
        await Server.WaitPost(() =>
        {
            map = SEntMan.System<SharedMapSystem>().CreateMap(out _);
            SEntMan.EnsureComponent<BiomeComponent>(map);
            SEntMan.SpawnEntity(BeaconProto, new EntityCoordinates(map, 0, 0));
        });

        return map;
    }

    private async Task<NetEntity> SpawnBeaconNearPlayer()
    {
        NetCoordinates coords = default;
        await Server.WaitPost(() => coords = SEntMan.GetNetCoordinates(
            SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(2, 2))));

        return await Spawn(BeaconProto, coords);
    }
}
