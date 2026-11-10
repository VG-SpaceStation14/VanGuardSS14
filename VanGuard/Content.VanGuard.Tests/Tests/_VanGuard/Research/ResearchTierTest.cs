#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Research.Systems;
using Robust.Shared.GameObjects;
using System.Reflection;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using Content.Shared.Research.Prototypes;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Research;

[TestFixture]
public sealed class ResearchTierTest : InteractionTest
{
    [Test]
    public async Task ExplicitPrerequisitesAreTheProgressGate()
    {
        var db = new TechnologyDatabaseComponent();
        Set(db, "SupportedDisciplines", new List<ProtoId<TechDisciplinePrototype>> { new("Industrial") });

        var compact = SProtoMan.Index<TechnologyPrototype>(new ProtoId<TechnologyPrototype>("CompactTechnologies"));
        Assert.That(Server.System<SharedResearchSystem>().IsTechnologyAvailable(db, compact), Is.False,
            "T2 must stay unavailable until its explicit prerequisite is unlocked");

        AddUnlocked(db, "AdvancedToolsTechnology");
        Assert.That(Server.System<SharedResearchSystem>().IsTechnologyAvailable(db, compact), Is.True,
            "T2 must not require a global percentage of the previous tier");
    }

    [Test]
    public async Task LockoutTierIsHard()
    {
        var db = new TechnologyDatabaseComponent();
        Set(db, "SupportedDisciplines", new List<ProtoId<TechDisciplinePrototype>>
        {
            new("Industrial"),
            new("Arsenal"),
        });
        Set(db, "MainDiscipline", (ProtoId<TechDisciplinePrototype>?) new ProtoId<TechDisciplinePrototype>("Arsenal"));
        AddUnlocked(db, "AdvancedToolsTechnology");
        AddUnlocked(db, "IndustrialEngineering");

        var locked = SProtoMan.Index<TechnologyPrototype>(new ProtoId<TechnologyPrototype>("MassExcavation"));
        var belowLockout = SProtoMan.Index<TechnologyPrototype>(new ProtoId<TechnologyPrototype>("CompactTechnologies"));
        var research = Server.System<SharedResearchSystem>();

        Assert.That(research.IsTechnologyAvailable(db, locked), Is.False,
            "T3 of a non-main discipline must be blocked by LockoutTier");
        Assert.That(research.IsTechnologyAvailable(db, belowLockout), Is.True,
            "LockoutTier must not block lower-tier technologies");
    }
    private static void Set<T>(TechnologyDatabaseComponent db, string field, T value)
    {
        typeof(TechnologyDatabaseComponent)
            .GetField(field, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(db, value);
    }

    private static void AddUnlocked(TechnologyDatabaseComponent db, string id)
    {
        var unlocked = (List<ProtoId<TechnologyPrototype>>)typeof(TechnologyDatabaseComponent)
            .GetField("UnlockedTechnologies", BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(db)!;
        unlocked.Add(new ProtoId<TechnologyPrototype>(id));
    }

    [Test]
    public async Task PurchaseUsesServerPrerequisites()
    {
        EntityUid server = default;
        EntityUid client = default;
        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            server = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", coords);
            client = SEntMan.SpawnEntity("Autolathe", coords);
            SEntMan.GetComponent<ResearchServerComponent>(server).Points = 100_000;

            var research = Server.System<ResearchSystem>();
            research.RegisterClient(client, server);
        });

        var researchSystem = Server.System<ResearchSystem>();
        await Server.WaitAssertion(() =>
        {
            Assert.That(researchSystem.UnlockTechnology(client, "CompactTechnologies", SPlayer), Is.False,
                "purchase must reject a technology whose explicit prerequisite is missing");

            researchSystem.AddTechnology(server, "AdvancedToolsTechnology");
            Assert.That(researchSystem.UnlockTechnology(client, "CompactTechnologies", SPlayer), Is.True,
                "purchase must accept the technology after its prerequisite is on the server");
        });
    }

}
