#nullable enable
using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Roles;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Clothing;

/// <summary>
///     Long-service clothing must be visible in the loadout UI but gated by the same playtime
///     requirements as before.
/// </summary>
[TestFixture]
public sealed class ClothingTimeGateTest : GameTest
{
    private static IPrototypeManager Proto => IoCManager.Instance!.Resolve<IPrototypeManager>();

    private static readonly ProtoId<RoleLoadoutPrototype> SalvageRole = "JobSalvageSpecialist";
    private static readonly ProtoId<RoleLoadoutPrototype> AtmosRole = "JobAtmosphericTechnician";
    private static readonly ProtoId<LoadoutGroupPrototype> ScientistNeckGroup = "ScientistNeck";

    private static readonly (string Group, string Kind, string Id, int Hours)[] Requirements =
    {
        ("SalvageElite", "Role", "JobSalvageSpecialist", 100),
        ("SalvageVeteran", "Role", "JobSalvageSpecialist", 300),
        ("AtmosVeteran", "Role", "JobAtmosphericTechnician", 180),
        ("CargoVeteran", "Department", "Cargo", 15),
    };

    private static readonly (string Loadout, string Group)[] GatedLoadouts =
    {
        ("SalvageSpecialistSupremeCloak", "SalvageElite"),
        ("SalvageSpecialistGloomyCloak", "SalvageVeteran"),
        ("SalvageSpecialistGrimyJumpsuit", "SalvageVeteran"),
        ("AtmosphericTechnicianCloak", "AtmosVeteran"),
        ("AtmosphericTechnicianBomber", "AtmosVeteran"),
        ("SeniorResearcherCloak", "SeniorResearcher"),
        ("ClothingNeckCloakCapFormalAltLoadout", "MasterCap"),
        ("ClothingUniformJumpskirtCaptainWhiteLoadout", "MasterCap"),
        ("ClothingUniformJumpsuitCommandCaptainLoadout", "MasterCap"),
        ("ClothingNeckCloakHosOriginalLoadout", "MasterHoS"),
        ("ClothingNeckCloakHosAltLoadout", "MasterHoS"),
        ("ClothingNeckLongCloakHOSLoadout", "MasterHoS"),
        ("ClothingNeckCloakRdAltLoadout", "MasterRD"),
        ("ClothingNeckLongCloakRDLoadout", "MasterRD"),
        ("ClothingNeckCloakQmAltLoadout", "MasterQM"),
        ("ClothingNeckLongCloakQMLoadout", "MasterQM"),
        ("ClothingNeckCloakCmoAltLoadout", "MasterCMO"),
        ("ClothingNeckCloakCmoTurquoiseLoadout", "MasterCMO"),
        ("ClothingNeckLongCloakCMOLoadout", "MasterCMO"),
        ("ClothingNeckLongCloakCELoadout", "MasterCE"),
        ("ClothingNeckLongCloakHOPLoadout", "MasterHoP"),
        ("ClothingUniformJumpsuitCargoPostalLoadout", "CargoVeteran"),
        ("ClothingUniformJumpskirtCargoPostalLoadout", "CargoVeteran"),
    };

    [Test]
    public async Task RewardItemsExist()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var item in new[]
                     {
                         "ClothingNeckGloomyCloakSalvager", "ClothingUniformJumpsuitSalvageSpecialistAlt",
                         "ClothingCloakAtmosian", "ClothingOuterAtmosianBomberJacket",
                         "ClothingNeckCloakSalvagerSupreme", "ClothingNeckCloakRdOriginal",
                     })
            {
                var proto = Proto.Index<EntityPrototype>(item);
                Assert.That(proto.Components.TryGetComponent("Clothing", out var clothing), Is.True,
                    $"{item} should be wearable");
                Assert.That(clothing, Is.InstanceOf<ClothingComponent>());
            }
        });
    }

    [Test]
    public async Task TimeRequirementGroupsAreCorrect()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var (groupId, kind, expected, hours) in Requirements)
            {
                var group = Proto.Index<LoadoutEffectGroupPrototype>(groupId);
                var effect = group.Effects.OfType<JobRequirementLoadoutEffect>().SingleOrDefault();
                Assert.That(effect, Is.Not.Null, $"{groupId} should contain a time requirement");

                switch (kind)
                {
                    case "Role":
                    {
                        var requirement = effect!.Requirement as RoleTimeRequirement;
                        Assert.That(requirement, Is.Not.Null, $"{groupId} should use a role time requirement");
                        Assert.That(requirement!.Role.Id, Is.EqualTo(expected), $"{groupId} role");
                        Assert.That(requirement.Time.TotalHours, Is.EqualTo(hours), $"{groupId} hours");
                        break;
                    }
                    case "Department":
                    {
                        var requirement = effect!.Requirement as DepartmentTimeRequirement;
                        Assert.That(requirement, Is.Not.Null, $"{groupId} should use a department time requirement");
                        Assert.That(requirement!.Department.Id, Is.EqualTo(expected), $"{groupId} department");
                        Assert.That(requirement.Time.TotalHours, Is.EqualTo(hours), $"{groupId} hours");
                        break;
                    }
                }
            }
        });
    }

    [Test]
    public async Task GatedLoadoutsCarryTheirEffects()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var (loadoutId, groupId) in GatedLoadouts)
            {
                var loadout = Proto.Index<LoadoutPrototype>(loadoutId);
                var effect = loadout.Effects.OfType<GroupLoadoutEffect>().SingleOrDefault();
                Assert.That(effect, Is.Not.Null, $"{loadoutId} should be gated");
                Assert.That(effect!.Proto.Id, Is.EqualTo(groupId), $"{loadoutId} gate");
            }

            // Free wardrobe entries stay free.
            foreach (var freeId in new[]
                     {
                         "ClothingNeckCloakCapOriginalLoadout",
                         "ClothingNeckCloakCapAltLoadout",
                         "ClothingNeckCloakRdOriginalLoadout",
                         "ClothingNeckMantleCMOOriginalLoadout",
                     })
            {
                var loadout = Proto.Index<LoadoutPrototype>(freeId);
                Assert.That(loadout.Effects, Is.Empty, $"{freeId} should be available without playtime");
            }
        });
    }

    [Test]
    public async Task GatedLoadoutsAreVisibleInRoleLoadouts()
    {
        await Server.WaitAssertion(() =>
        {
            var salvage = Proto.Index(SalvageRole);
            Assert.That(salvage.Groups.Select(g => g.Id), Does.Contain("SalvageSpecialistNeck"));
            Assert.That(salvage.Groups.Select(g => g.Id), Does.Contain("SalvageSpecialistJumpsuit"));

            var atmos = Proto.Index(AtmosRole);
            Assert.That(atmos.Groups.Select(g => g.Id), Does.Contain("AtmosphericTechnicianNeck"));

            var scientistNeck = Proto.Index(ScientistNeckGroup);
            Assert.That(scientistNeck.Loadouts.Select(l => l.Id), Does.Contain("SeniorResearcherCloak"));
        });
    }
}
