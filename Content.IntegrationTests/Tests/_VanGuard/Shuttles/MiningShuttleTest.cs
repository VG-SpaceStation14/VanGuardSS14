#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client.Shuttles.BUI;
using Content.Client._VanGuard.Shuttles.UI;
using Content.Server._VanGuard.Lavaland;
using Content.Server._VanGuard.Shuttles.Components;
using Content.Server._VanGuard.Shuttles.Guest;
using Content.Server._VanGuard.Shuttles.Systems;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._VanGuard.Shuttles;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Access.Components;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.GameTicking.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Power.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Station.Components;
using Content.IntegrationTests.Fixtures;
using Robust.Client.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VanGuard.Shuttles;

[TestFixture]
[NonParallelizable]
public sealed class MiningShuttleTest : GameTest
{
    private const string ShuttlePath = "/Maps/_VanGuard/Shuttles/NT - Mining.yml";
    private const string OutpostPath = "/Maps/_VanGuard/Lavaland/mining_outpost.yml";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        DummyTicker = false,
        Fresh = true,
    };

    [Test]
    public async Task MapsUseTheVanGuardPrototypeIds()
    {
        var stationGrid = GetStationGrid();
        var mapId = Xform(stationGrid).MapID;

        var shuttle = await LoadGrid(mapId, ShuttlePath);
        try
        {
            var ids = await EntityPrototypeIds(shuttle);
            Assert.That(ids, Does.Not.Contain("AirlockExternalShuttleLocked"));
            Assert.That(ids, Does.Not.Contain("ComputerShuttle"));
            Assert.That(ids, Contains.Item("AirlockShuttleMiningLocked"));
            Assert.That(ids, Contains.Item("MiningShuttleConsole"));
        }
        finally
        {
            await DeleteEntity(shuttle);
        }

        var outpost = await LoadGrid(mapId, OutpostPath);
        try
        {
            var ids = await EntityPrototypeIds(outpost);
            Assert.That(ids, Does.Not.Contain("AirlockExternalShuttleLocked"));
            Assert.That(ids, Does.Not.Contain("ComputerShuttle"));
            Assert.That(ids, Contains.Item("AirlockShuttleMiningLocked"));
            Assert.That(ids, Contains.Item("MiningShuttleConsole"));
        }
        finally
        {
            await DeleteEntity(outpost);
        }
    }

    [Test]
    public async Task StandardConsolesKeepStandardBui()
    {
        var stationGrid = GetStationGrid();
        foreach (var proto in new[] { "ComputerShuttle", "ComputerShuttleCargo", "ComputerShuttleSalvage" })
        {
            var console = await SpawnOnGrid(proto, stationGrid, new Vector2(0, 0));
            try
            {
                await Server.WaitPost(() =>
                {
                    Assert.That(SEntMan.HasComponent<ShuttleConsoleComponent>(console), Is.True,
                        $"{proto} has no ShuttleConsoleComponent.");
                    Assert.That(SEntMan.HasComponent<MiningShuttleConsoleComponent>(console), Is.False,
                        $"{proto} incorrectly has MiningShuttleConsoleComponent.");
                    var ui = SEntMan.GetComponent<UserInterfaceComponent>(console);
                    var interfaces = typeof(UserInterfaceComponent).GetField(
                        "Interfaces", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ui)
                        as Dictionary<Enum, InterfaceData>;
                    Assert.That(interfaces, Is.Not.Null);
                    Assert.That(interfaces!.TryGetValue(ShuttleConsoleUiKey.Key, out var data), Is.True);
                    Assert.That(data!.ClientType, Is.EqualTo(typeof(ShuttleConsoleBoundUserInterface).Name),
                        $"{proto} has wrong UI client type for ShuttleConsoleUiKey.");
                });

                var bui = await OpenUi(console, ShuttleConsoleUiKey.Key);
                Assert.That(bui, Is.InstanceOf<ShuttleConsoleBoundUserInterface>(),
                    $"{proto} was opened with the wrong BUI type: {bui.GetType().Name}.");
                Assert.That(bui, Is.Not.InstanceOf<MiningConsoleBoundUserInterface>());
            }
            finally
            {
                await DeleteEntity(console);
            }
        }
    }

    [Test]
    public async Task MiningConsoleHasExactlyTwoDirections()
    {
        var stationGrid = GetStationGrid();
        var station = GetStation(stationGrid);
        var shuttle = await LoadGrid(Xform(stationGrid).MapID, ShuttlePath);
        EntityUid? card = null;

        try
        {
            await FastFtl();
            await PrepareShuttle(shuttle, station);
            card = await UseConsoleAccess();

            var console = await FindConsole(shuttle);
            var system = SEntMan.System<MiningShuttleConsoleSystem>();
            List<MiningShuttleDestination>? destinations = null;
            await Server.WaitPost(() => destinations = system.GetDestinations(console, shuttle));

            Assert.That(destinations, Is.Not.Null);
            Assert.That(destinations!.Count, Is.EqualTo(2));
            string[]? expectedNames = null;
            await Server.WaitPost(() => expectedNames = new[]
            {
                Loc.GetString("mining-console-station"),
                Loc.GetString("mining-console-lavaland"),
            });
            Assert.That(destinations.Select(destination => destination.Name),
                Is.EqualTo(expectedNames));

            var bui = await OpenUi(console.Owner, MiningShuttleConsoleUiKey.MiningShuttleConsole);
            Assert.That(bui, Is.InstanceOf<MiningConsoleBoundUserInterface>());
        }
        finally
        {
            if (card is { } cardUid && SEntMan.EntityExists(cardUid))
                await DeleteEntity(cardUid);
            if (SEntMan.EntityExists(shuttle))
                await DeleteEntity(shuttle);
        }
    }

    [Test]
    public async Task FallbackToCompatibleDockWithoutPriorityTag()
    {
        var stationGrid = GetStationGrid();
        var station = GetStation(stationGrid);
        var shuttle = await LoadGrid(Xform(stationGrid).MapID, ShuttlePath);
        var targetDock = await SpawnDock(stationGrid, "AirlockExternalShuttleLocked", new Vector2(55, 0));
        EntityUid? card = null;
        try
        {
            await FastFtl();
            await PrepareShuttle(shuttle, station);
            card = await UseConsoleAccess();
            var console = await FindConsole(shuttle);
            var system = SEntMan.System<MiningShuttleConsoleSystem>();
            var player = Pair.Player!.AttachedEntity!.Value;

            var travelled = false;
            string? error = null;
            await Server.WaitPost(() => travelled = system.TryTravel(console, 0, player, out error));
            Assert.That(travelled, Is.True, error);
            await WaitForDock(shuttle, Xform(targetDock).GridUid!.Value);
            Assert.That(SEntMan.GetComponent<DockingComponent>(targetDock).Docked, Is.True);
        }
        finally
        {
            if (card is { } cardUid && SEntMan.EntityExists(cardUid))
                await DeleteEntity(cardUid);
            if (SEntMan.EntityExists(targetDock))
                await DeleteEntity(targetDock);
            if (SEntMan.EntityExists(shuttle))
                await DeleteEntity(shuttle);
        }
    }

    [Test]
    public async Task StationConsoleUsesTheOnlyMiningConsole()
    {
        var stationGrid = GetStationGrid();
        var station = GetStation(stationGrid);
        var shuttle = await LoadGrid(Xform(stationGrid).MapID, ShuttlePath);
        var consoleUid = await SpawnOnGrid("MiningShuttleConsole", stationGrid, new Vector2(0, 0));
        EntityUid? card = null;

        try
        {
            await FastFtl();
            await PrepareShuttle(shuttle, station);
            card = await UseConsoleAccess();

            var console = (consoleUid, SEntMan.GetComponent<MiningShuttleConsoleComponent>(consoleUid));
            var system = SEntMan.System<MiningShuttleConsoleSystem>();
            var controlled = false;
            await Server.WaitPost(() => controlled = system.TryGetShuttle(console, out var found) && found == shuttle);
            Assert.That(controlled, Is.True, "The station console did not discover the marked shuttle.");

            var bui = await OpenUi(consoleUid, MiningShuttleConsoleUiKey.MiningShuttleConsole);
            Assert.That(bui, Is.InstanceOf<MiningConsoleBoundUserInterface>());
        }
        finally
        {
            if (card is { } cardUid && SEntMan.EntityExists(cardUid))
                await DeleteEntity(cardUid);
            if (SEntMan.EntityExists(consoleUid))
                await DeleteEntity(consoleUid);
            if (SEntMan.EntityExists(shuttle))
                await DeleteEntity(shuttle);
        }
    }

    [Test]
    public async Task FullCycleStationOutpostStationThroughConsole()
    {
        var ticker = Server.System<ServerGameTicker>();
        var stationGrid = GetStationGrid();
        var station = GetStation(stationGrid);
        var stationMap = Xform(stationGrid).MapID;
        var stationDock = await SpawnDock(stationGrid, "AirlockShuttleMiningLocked", new Vector2(50, 0));
        EntityUid? shuttle = null;
        EntityUid? card = null;
        Entity<GameRuleComponent>? lavalandRule = null;

        try
        {
            await FastFtl();
            await Server.WaitPost(() => lavalandRule = ticker.AddGameRule(new EntProtoId("LavalandLoader")));
            Assert.That(lavalandRule, Is.Not.Null);
            var lavaland = SEntMan.GetComponent<LavalandOutpostRuleComponent>(lavalandRule!.Value.Owner);
            Assert.That(lavaland.MapId, Is.Not.Null);
            Assert.That(lavaland.OutpostGrid, Is.Not.Null);
            var lavalandMap = lavaland.MapId!.Value;
            var outpostGrid = lavaland.OutpostGrid!.Value;

            shuttle = await LoadGrid(stationMap, ShuttlePath);
            await PrepareShuttle(shuttle.Value, station);
            card = await UseConsoleAccess();

            var console = await FindConsole(shuttle.Value);
            var system = SEntMan.System<MiningShuttleConsoleSystem>();
            var player = Pair.Player!.AttachedEntity!.Value;

            List<MiningShuttleDestination>? destinations = null;
            await Server.WaitPost(() => destinations = system.GetDestinations(console, shuttle.Value));
            Assert.That(destinations, Is.Not.Null);
            Assert.That(destinations!.Count, Is.EqualTo(2));
            Assert.That(destinations[1].Available, Is.True, destinations[1].Reason);

            var toLavaland = false;
            string? error = null;
            await Server.WaitPost(() => toLavaland = system.TryTravel(console, 1, player, out error));
            Assert.That(toLavaland, Is.True, error);
            Assert.That(SEntMan.HasComponent<FTLComponent>(shuttle.Value), Is.True,
                "Travel must actually start FTL, not only report success.");
            await WaitForDock(shuttle.Value, outpostGrid, "Lavaland");
            Assert.That(Xform(shuttle.Value).MapID, Is.EqualTo(lavalandMap));
            Assert.That(SEntMan.EntityExists(shuttle.Value), Is.True);
            await WaitForFtlReady(shuttle.Value);

            var outpostConsole = await FindConsoleOnGrid(outpostGrid);
            var outpostConsoleEntity = (outpostConsole, SEntMan.GetComponent<MiningShuttleConsoleComponent>(outpostConsole));
            var controlled = false;
            await Server.WaitPost(() => controlled = system.TryGetShuttle(outpostConsoleEntity, out var found) && found == shuttle.Value);
            Assert.That(controlled, Is.True, "The outpost console did not find the returning shuttle.");

            var toStation = false;
            string? returnError = null;
            await Server.WaitPost(() => toStation = system.TryTravel(outpostConsoleEntity, 0, player, out returnError));
            Assert.That(toStation, Is.True, returnError);
            Assert.That(SEntMan.HasComponent<FTLComponent>(shuttle.Value), Is.True,
                "Return travel must actually start FTL.");
            await WaitForDock(shuttle.Value, Xform(stationDock).GridUid!.Value, "Station");
            Assert.That(Xform(shuttle.Value).MapID, Is.EqualTo(stationMap));
            Assert.That(SEntMan.EntityExists(shuttle.Value), Is.True);
            Assert.That(SEntMan.GetComponent<DockingComponent>(stationDock).Docked, Is.True);
            await WaitForFtlReady(shuttle.Value);

            var returnedUi = await OpenUi(console.Owner, MiningShuttleConsoleUiKey.MiningShuttleConsole);
            Assert.That(returnedUi, Is.InstanceOf<MiningConsoleBoundUserInterface>(),
                "The console did not provide its UI after the return trip.");

            // A repeated trip must work and must not leave the grid in FTL.
            var repeat = false;
            string? repeatError = null;
            await Server.WaitPost(() => repeat = system.TryTravel(console, 1, player, out repeatError));
            Assert.That(repeat, Is.True, repeatError);
            await WaitForDock(shuttle.Value, outpostGrid, "Lavaland repeat");
            Assert.That(Xform(shuttle.Value).MapID, Is.EqualTo(lavalandMap));
            Assert.That(SEntMan.EntityExists(shuttle.Value), Is.True);
        }
        finally
        {
            if (card is { } cardUid && SEntMan.EntityExists(cardUid))
                await DeleteEntity(cardUid);
            if (SEntMan.EntityExists(stationDock))
                await DeleteEntity(stationDock);
            if (shuttle is { } shuttleUid && SEntMan.EntityExists(shuttleUid))
                await DeleteEntity(shuttleUid);
            if (lavalandRule is { } rule && SEntMan.EntityExists(rule.Owner))
            {
                var lavaland = SEntMan.GetComponent<LavalandOutpostRuleComponent>(rule.Owner);
                if (lavaland.Map is { } map && SEntMan.EntityExists(map))
                    await DeleteEntity(map);
                await Server.WaitPost(() => ticker.EndGameRule((rule.Owner, (GameRuleComponent?) rule.Comp)));
            }
        }
    }

    [Test]
    public void MinerJobAndLockerPrototypesExist()
    {
        var jobId = new ProtoId<JobPrototype>("Miner");
        var job = SProtoMan.Index(jobId);
        Assert.That(job.StartingGear, Is.Not.Null);
        Assert.That(SProtoMan.HasIndex(new ProtoId<RoleLoadoutPrototype>("JobMiner")), Is.True);

        var cargo = SProtoMan.Index(new ProtoId<DepartmentPrototype>("Cargo"));
        Assert.That(cargo.Roles, Contains.Item("Miner"));

        Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("LockerMinerFilled")), Is.True);
        Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("SuitStorageMiner")), Is.True);
        Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("MinerPDA")), Is.True);
        Assert.That(SProtoMan.HasIndex(new ProtoId<EntityPrototype>("MinerIDCard")), Is.True);
    }

    [Test]
    public async Task OpenConsoleRefreshesAfterCooldown()
    {
        var stationGrid = GetStationGrid();
        var station = GetStation(stationGrid);
        var shuttle = await LoadGrid(Xform(stationGrid).MapID, ShuttlePath);
        var dock = await SpawnDock(stationGrid, "AirlockShuttleMiningLocked", new Vector2(55, 0));
        EntityUid? card = null;

        try
        {
            await FastFtl();
            await Server.WaitPost(() =>
            {
                var marker = SEntMan.EnsureComponent<MiningShuttleComponent>(shuttle);
                marker.Station = station;
                SEntMan.GetComponent<ShuttleComponent>(shuttle).FTLCooldownOverride = TimeSpan.FromSeconds(2);
            });
            card = await UseConsoleAccess();
            var console = await FindConsole(shuttle);
            var system = SEntMan.System<MiningShuttleConsoleSystem>();
            var player = Pair.Player!.AttachedEntity!.Value;

            var travelled = false;
            string? error = null;
            await Server.WaitPost(() => travelled = system.TryTravel(console, 0, player, out error));
            Assert.That(travelled, Is.True, error);
            await WaitForDock(shuttle, Xform(dock).GridUid!.Value);

            // Open while the arrival cooldown is still running.
            var bui = await OpenUi(console.Owner, MiningShuttleConsoleUiKey.MiningShuttleConsole);
            Assert.That(bui, Is.InstanceOf<MiningConsoleBoundUserInterface>());

            var cooldownState = false;
            await Server.WaitPost(() =>
            {
                cooldownState = Server.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<MiningShuttleConsoleState>(
                        (console.Owner, null),
                        MiningShuttleConsoleUiKey.MiningShuttleConsole,
                        out var state) &&
                    state!.FTLState == FTLState.Cooldown;
            });
            Assert.That(cooldownState, Is.True, "The open console did not observe the cooldown state.");

            await WaitForFtlReady(shuttle);
            await Pair.RunTicksSync(35);

            var available = false;
            await Server.WaitPost(() =>
            {
                available = Server.System<SharedUserInterfaceSystem>()
                    .TryGetUiState<MiningShuttleConsoleState>(
                        (console.Owner, null),
                        MiningShuttleConsoleUiKey.MiningShuttleConsole,
                        out var state) &&
                    state!.FTLState == FTLState.Available;
            });
            Assert.That(available, Is.True,
                "The already-open console did not refresh to Available after cooldown.");
        }
        finally
        {
            if (card is { } cardUid && SEntMan.EntityExists(cardUid))
                await DeleteEntity(cardUid);
            if (SEntMan.EntityExists(dock))
                await DeleteEntity(dock);
            if (SEntMan.EntityExists(shuttle))
                await DeleteEntity(shuttle);
        }
    }

    [Test]
    public async Task RoundstartStationGridSpawnLoadsShuttle()
    {
        var ticker = Server.System<ServerGameTicker>();

        try
        {
            await Server.WaitPost(() => Server.CfgMan.SetCVar(CCVars.GridFill, true));
            await Pair.WaitCommand("golobby");
            await Pair.WaitClientCommand("toggleready True");
            await Pair.WaitCommand("setgamepreset Extended 9999");
            await Pair.WaitCommand("startround");
            await Pair.RunUntilSynced();

            var stationGrid = GetStationGrid();
            var stationMap = Xform(stationGrid).MapID;

            HashSet<EntityUid> shuttles = new();
            EntityUid? shuttle = null;
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<MiningShuttleComponent>();
                while (query.MoveNext(out var uid, out _))
                {
                    shuttles.Add(uid);
                    shuttle ??= uid;
                }
            });

            Assert.That(shuttles.Count, Is.EqualTo(1), "Roundstart should spawn exactly one mining shuttle.");
            Assert.That(shuttle, Is.Not.Null);
            Assert.That(Xform(shuttle!.Value).MapID, Is.EqualTo(stationMap),
                "The mining shuttle must spawn on the station map, not on Lavaland.");

            var ids = await EntityPrototypeIds(shuttle.Value);
            Assert.That(ids, Contains.Item("MiningShuttleConsole"));
            Assert.That(ids, Contains.Item("AirlockShuttleMiningLocked"));

            var guestCount = 0;
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<GuestShuttleComponent>();
                while (query.MoveNext(out _, out _))
                    guestCount++;
            });
            Assert.That(guestCount, Is.EqualTo(1), "Roundstart should spawn exactly one guest shuttle.");

        }
        finally
        {
            await Pair.WaitCommand("golobby");
            await Server.WaitPost(() => ticker.SetGamePreset((GamePresetPrototype?) null));
            await Pair.WaitClientCommand("toggleready True");
            await Pair.WaitCommand("startround");
            await Pair.RunUntilSynced();
        }
    }

    [Test]
    public async Task GuestShuttleConsoleTogglesLocalShutter()
    {
        var stationGrid = GetStationGrid();
        var station = GetStation(stationGrid);
        var shuttle = await LoadGrid(Xform(stationGrid).MapID, "/Maps/_VanGuard/Shuttles/NT - Guest.yml");
        EntityUid? card = null;
        EntityUid shutter = default;

        try
        {
            await Server.WaitPost(() =>
            {
                var marker = SEntMan.EnsureComponent<GuestShuttleComponent>(shuttle);
                marker.Station = station;
            });

            var console = await FindGuestConsoleOnGrid(shuttle);
            var player = Pair.Player!.AttachedEntity!.Value;
            card = await UseConsoleAccess();

            await Server.WaitPost(() =>
            {
                var grid = SEntMan.GetComponent<MapGridComponent>(shuttle);
                var coords = new EntityCoordinates(shuttle, new Vector2(0, 0));
                SEntMan.System<SharedMapSystem>().SetTile(shuttle, grid, new Vector2i(0, 0), new Tile(1));
                shutter = SEntMan.SpawnEntity("ShuttersNormal", coords);
                SEntMan.EnsureComponent<GuestShuttleShutterComponent>(shutter);
                SEntMan.System<SharedTransformSystem>().SetParent(shutter, shuttle);
                SEntMan.System<SharedTransformSystem>().AnchorEntity(
                    shutter,
                    SEntMan.GetComponent<TransformComponent>(shutter),
                    grid);
            });

            var consoleEntity = (console, SEntMan.GetComponent<GuestShuttleConsoleComponent>(console));
            var system = SEntMan.System<GuestShuttleConsoleSystem>();

            var bui = await OpenUi(console, GuestShutterConsoleUiKey.GuestShutterConsole);
            Assert.That(bui, Is.InstanceOf<GuestShutterConsoleBoundUserInterface>());

            bool toggled = false;
            string? error = null;
            await Server.WaitPost(() => toggled = system.TryToggle(consoleEntity, shutter, player, out error));
            Assert.That(toggled, Is.True, error);
            await Pair.RunTicksSync(20);

            var state = SEntMan.GetComponent<DoorComponent>(shutter).State;
            Assert.That(state, Is.EqualTo(DoorState.Open).Or.EqualTo(DoorState.Opening));

            await Server.WaitPost(() =>
            {
                Assert.That(Server.System<SharedUserInterfaceSystem>().TryGetUiState<GuestShutterConsoleState>(
                    (console, null), GuestShutterConsoleUiKey.GuestShutterConsole, out var uiState), Is.True);
                Assert.That(uiState!.Shutters.Any(entry => entry.Door == SEntMan.GetNetEntity(shutter)), Is.True);
            });
        }
        finally
        {
            if (card is { } cardUid && SEntMan.EntityExists(cardUid))
                await DeleteEntity(cardUid);
            if (shutter != EntityUid.Invalid && SEntMan.EntityExists(shutter))
                await DeleteEntity(shutter);
            if (SEntMan.EntityExists(shuttle))
                await DeleteEntity(shuttle);
        }
    }

    private async Task FastFtl()
    {
        await Server.WaitPost(() =>
        {
            var shuttleSystem = SEntMan.System<ShuttleSystem>();
            shuttleSystem.DefaultStartupTime = 0;
            shuttleSystem.DefaultTravelTime = 0;
        });
    }

    private async Task PrepareShuttle(EntityUid shuttle, EntityUid station)
    {
        await Server.WaitPost(() =>
        {
            var marker = SEntMan.EnsureComponent<MiningShuttleComponent>(shuttle);
            marker.Station = station;
            SEntMan.GetComponent<ShuttleComponent>(shuttle).FTLCooldownOverride = TimeSpan.Zero;
        });
    }

    private async Task<EntityUid> UseConsoleAccess()
    {
        var player = Pair.Player!.AttachedEntity!.Value;
        EntityUid card = default;
        await Server.WaitPost(() =>
        {
            card = SEntMan.SpawnEntity("PassengerIDCard", Xform(player).Coordinates);
            SEntMan.EnsureComponent<AccessComponent>(card).Tags.Add("Salvage");
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(player, card), Is.True);
        });
        return card;
    }

    private async Task<BoundUserInterface> OpenUi(EntityUid owner, Enum key)
    {
        var player = Pair.Player!.AttachedEntity!.Value;
        var serverUi = Server.System<SharedUserInterfaceSystem>();
        await Pair.RunTicksSync(5);
        await Server.WaitPost(() =>
        {
            if (SEntMan.TryGetComponent<ApcPowerReceiverComponent>(owner, out var receiver))
            {
                receiver.NeedsPower = false;
                receiver.Powered = true;
            }

            SEntMan.RemoveComponent<ActivatableUIRequiresPowerComponent>(owner);

            Assert.That(SEntMan.HasComponent<UserInterfaceComponent>(owner), Is.True,
                $"Entity {owner} has no UserInterfaceComponent.");
            Assert.That(serverUi.HasUi(owner, key), Is.True,
                $"Entity {owner} has no UI for key {key}.");

            // Test harness: map test computers have no APC. Keep the UI open for
            // the BUI/type/network-path assertions; production access is checked
            // separately by TryTravel and the AccessReader component.
            var ui = SEntMan.GetComponent<UserInterfaceComponent>(owner);
            var interfaces = typeof(UserInterfaceComponent).GetField(
                "Interfaces", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ui)
                as Dictionary<Enum, InterfaceData>;
            if (interfaces != null && interfaces.TryGetValue(key, out var data))
                data.RequireInputValidation = false;
            Assert.That(serverUi.TryOpenUi((owner, null), key, player), Is.True);
        });
        await Pair.RunUntilSynced();

        var clientUid = Pair.ToClientUid(owner);
        BoundUserInterface? bui = null;
        await Client.WaitPost(() =>
        {
            var clientUi = Client.System<UserInterfaceSystem>();
            if (!clientUi.TryGetOpenUi((clientUid, null), key, out bui))
            {
                clientUi.OpenUi((clientUid, null), key);
                clientUi.TryGetOpenUi((clientUid, null), key, out bui);
            }
        });
        await Pair.RunUntilSynced();

        Assert.That(bui, Is.Not.Null);
        return bui!;
    }

    private async Task WaitForFtlReady(EntityUid shuttle)
    {
        for (var i = 0; i < 600; i++)
        {
            var ready = false;
            await Server.WaitPost(() => ready = !SEntMan.HasComponent<FTLComponent>(shuttle));
            if (ready)
                return;

            await Pair.RunTicksSync(1);
        }

        Assert.Fail("Timed out waiting for FTL cooldown.");
    }

    private async Task WaitForDock(EntityUid shuttle, EntityUid targetGrid, string label = "")
    {
        for (var i = 0; i < 600; i++)
        {
            var docked = false;
            await Server.WaitPost(() =>
            {
                var query = SEntMan.EntityQueryEnumerator<DockingComponent, TransformComponent>();
                while (query.MoveNext(out _, out var dock, out var xform))
                {
                    if (xform.GridUid == targetGrid && dock.Docked)
                    {
                        docked = true;
                        return;
                    }
                }
            });
            if (docked)
                return;

            await Pair.RunTicksSync(1);
        }

        Assert.Fail($"Timed out waiting for the shuttle to dock ({label}).");
    }

    private TransformComponent Xform(EntityUid uid)
    {
        return SEntMan.GetComponent<TransformComponent>(uid);
    }

    private EntityUid GetStationGrid()
    {
        var stations = SEntMan.EntityQueryEnumerator<StationDataComponent>();
        var stationSystem = Server.System<ServerStationSystem>();
        while (stations.MoveNext(out var station, out var data))
        {
            if (stationSystem.GetLargestGrid((station, data)) is { } grid)
                return grid;
        }

        throw new InvalidOperationException("Test station has no grid.");
    }

    private EntityUid GetStation(EntityUid grid)
    {
        var station = Server.System<ServerStationSystem>().GetOwningStation(grid);
        if (station is { } stationUid)
            return stationUid;

        throw new InvalidOperationException("Test grid is not part of a station.");
    }

    private async Task<EntityUid> LoadGrid(MapId mapId, string path)
    {
        EntityUid? grid = null;
        await Server.WaitPost(() =>
        {
            var loader = SEntMan.System<MapLoaderSystem>();
            Assert.That(loader.TryLoadGrid(
                mapId,
                new ResPath(path),
                out var loaded,
                new DeserializationOptions { InitializeMaps = true }), $"Failed to load {path}.");
            Assert.That(loaded, Is.Not.Null);
            grid = loaded!.Value.Owner;
        });
        Assert.That(grid, Is.Not.Null);
        return grid!.Value;
    }

    private async Task DeleteEntity(EntityUid uid)
    {
        await Server.WaitPost(() =>
        {
            if (SEntMan.EntityExists(uid))
                SEntMan.DeleteEntity(uid);
        });
    }

    private async Task<List<string>> EntityPrototypeIds(EntityUid grid)
    {
        var ids = new List<string>();
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var metadata, out var xform))
            {
                if (xform.GridUid == grid && metadata.EntityPrototype is { } proto)
                    ids.Add(proto.ID);
            }
        });
        return ids;
    }

    private async Task<EntityUid> SpawnOnGrid(string proto, EntityUid grid, Vector2 position)
    {
        EntityUid? uid = null;
        await Server.WaitPost(() =>
        {
            var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
            var indices = new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
            SEntMan.System<SharedMapSystem>().SetTile(grid, gridComp, indices, new Tile(1));

            var coordinates = new EntityCoordinates(grid, position);
            uid = SEntMan.SpawnEntity(proto, coordinates);
            var transform = SEntMan.System<SharedTransformSystem>();
            transform.SetParent(uid.Value, grid);
            transform.AnchorEntity(uid.Value, SEntMan.GetComponent<TransformComponent>(uid.Value), gridComp);
        });
        Assert.That(uid, Is.Not.Null);
        return uid!.Value;
    }

    private async Task<EntityUid> SpawnDock(EntityUid grid, string proto, Vector2 position)
    {
        return await SpawnOnGrid(proto, grid, position);
    }

    private async Task<(EntityUid Owner, MiningShuttleConsoleComponent Comp)> FindConsole(EntityUid shuttle)
    {
        (EntityUid, MiningShuttleConsoleComponent)? console = null;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<MiningShuttleConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var comp, out var xform))
            {
                if (xform.GridUid == shuttle)
                {
                    console = (uid, comp);
                    return;
                }
            }
        });
        Assert.That(console, Is.Not.Null, "The shuttle map has no mining console.");
        return console!.Value;
    }

    private async Task<EntityUid> FindGuestConsoleOnGrid(EntityUid grid)
    {
        EntityUid? console = null;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<GuestShuttleConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                {
                    console = uid;
                    return;
                }
            }
        });
        Assert.That(console, Is.Not.Null, "The guest shuttle has no shutter console.");
        return console!.Value;
    }

    private async Task<EntityUid> FindConsoleOnGrid(EntityUid grid)
    {
        EntityUid? console = null;
        await Server.WaitPost(() =>
        {
            var query = SEntMan.EntityQueryEnumerator<MiningShuttleConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                {
                    console = uid;
                    return;
                }
            }
        });
        Assert.That(console, Is.Not.Null, "The outpost has no mining console.");
        return console!.Value;
    }
}
