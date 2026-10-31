#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Roles;
using Robust.Shared.IoC;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Access;

/// <summary>
///     Guards the access audit: the cargo jobs stay separated, officers can escort prisoners and the
///     medical emergency jobs can reach external areas, without stretching any of them into all-access.
/// </summary>
[TestFixture]
public sealed class JobAccessAuditTest : GameTest
{
    private static IPrototypeManager Proto => IoCManager.Instance!.Resolve<IPrototypeManager>();

    private static readonly EntProtoId SalvageLocker = "LockerSalvageSpecialist";
    private static readonly EntProtoId QmLocker = "LockerQuarterMaster";
    private static readonly EntProtoId SalvageDoor = "DoorElectronicsSalvage";
    private static readonly EntProtoId CargoDoor = "DoorElectronicsCargo";
    private static readonly EntProtoId QmDoor = "DoorElectronicsQuartermaster";

    private static readonly ProtoId<JobPrototype> CargoTech = "CargoTechnician";
    private static readonly ProtoId<JobPrototype> HeadOfPersonnel = "HeadOfPersonnel";
    private static readonly ProtoId<JobPrototype> Salvage = "SalvageSpecialist";
    private static readonly ProtoId<JobPrototype> Quartermaster = "Quartermaster";
    private static readonly ProtoId<JobPrototype> Officer = "SecurityOfficer";
    private static readonly ProtoId<JobPrototype> Cadet = "SecurityCadet";
    private static readonly ProtoId<JobPrototype> Warden = "Warden";
    private static readonly ProtoId<JobPrototype> Paramedic = "Paramedic";
    private static readonly ProtoId<JobPrototype> Brigmedic = "Brigmedic";
    private static string[] Ids(IEnumerable<ProtoId<AccessLevelPrototype>> access)
        => access.Select(a => a.Id).ToArray();

    private static string[] GroupIds(IEnumerable<ProtoId<AccessGroupPrototype>> groups)
        => groups.Select(g => g.Id).ToArray();

    [Test]
    public async Task CargoJobsStaySeparated()
    {
        await Server.WaitAssertion(() =>
        {
            var cargoTech = Proto.Index(CargoTech);
            Assert.That(Ids(cargoTech.Access), Does.Contain("Cargo"), "Cargo techs work in the cargo bay");
            Assert.That(Ids(cargoTech.Access), Does.Not.Contain("Salvage"),
                "Cargo techs must not enter the salvage area");
            Assert.That(Ids(cargoTech.ExtendedAccess), Does.Not.Contain("Salvage"),
                "Cargo techs must not receive salvage access even on lowpop");

            var salvage = Proto.Index(Salvage);
            Assert.That(Ids(salvage.Access), Does.Contain("Salvage"), "Salvage specialists own the salvage area");
            Assert.That(Ids(salvage.Access), Does.Contain("Cargo"), "Salvage specialists hand their finds over in cargo");
            Assert.That(Ids(salvage.Access), Does.Not.Contain("Quartermaster"), "Salvage specialists are not the QM");

            var qm = Proto.Index(Quartermaster);
            Assert.That(Ids(qm.Access), Does.Contain("Quartermaster"), "The QM office belongs to the QM");
            Assert.That(Ids(qm.Access), Does.Contain("Cargo"), "The QM coordinates the cargo bay");
            Assert.That(Ids(qm.Access), Does.Contain("Salvage"), "The QM coordinates the salvage team");
            Assert.That(Ids(qm.Access), Does.Not.Contain("Armory"), "The QM has no security armory access");
            Assert.That(Ids(qm.Access), Does.Not.Contain("Atmospherics"), "The QM has no engineering access");
            Assert.That(Ids(qm.Access), Does.Not.Contain("Research"), "The QM has no science access");
        });
    }

    [Test]
    public async Task SecurityEscortAccessIsConsistent()
    {
        await Server.WaitAssertion(() =>
        {
            var officer = Proto.Index(Officer);
            Assert.That(Ids(officer.Access), Does.Contain("GenpopEnter"),
                "Officers escort prisoners into genpop");
            Assert.That(Ids(officer.Access), Does.Contain("GenpopLeave"),
                "Officers escort prisoners out of genpop");

            var cadet = Proto.Index(Cadet);
            Assert.That(Ids(cadet.Access), Does.Not.Contain("External"),
                "Cadets patrol outside only with an officer");
            Assert.That(Ids(cadet.ExtendedAccess), Does.Contain("External"),
                "Cadets keep external access on lowpop");

            var warden = Proto.Index(Warden);
            Assert.That(Ids(warden.Access), Does.Contain("GenpopEnter"), "The warden manages genpop");
            Assert.That(Ids(warden.Access), Does.Contain("Armory"), "The warden manages the armory");
        });
    }

    [Test]
    public async Task MedicalEmergencyJobsCanReachExternalAreas()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var jobId in new[] { Paramedic, Brigmedic })
            {
                var job = Proto.Index(jobId);
                Assert.That(Ids(job.Access), Does.Contain("External"),
                    $"{jobId} should be able to reach patients outside the department");
            }
        });
    }

    [Test]
    public async Task LeadershipDoesNotReplaceSecurityOrServiceStaff()
    {
        await Server.WaitAssertion(() =>
        {
            var hop = Proto.Index(HeadOfPersonnel);
            Assert.That(Ids(hop.Access), Does.Not.Contain("GenpopEnter"),
                "The HoP manages personnel, not prisoners");
            Assert.That(Ids(hop.Access), Does.Not.Contain("GenpopLeave"),
                "The HoP manages personnel, not prisoners");
            Assert.That(Ids(hop.Access), Does.Contain("Service"),
                "The HoP still coordinates the service department");
        });
    }

    [Test]
    public async Task CargoDoorsAndLockersRequireTheRightAccess()
    {
        void AssertReader(EntProtoId id, string expected)
        {
            var proto = Proto.Index(id);
            Assert.That(proto.Components.TryGetComponent("AccessReader", out var component), Is.True,
                $"{id} should have an access reader");
            var reader = (AccessReaderComponent)component!;
            var tags = reader.AccessLists.SelectMany(set => set).Select(tag => tag.Id).ToArray();
            Assert.That(tags, Does.Contain(expected), $"{id} should require {expected}");
        }

        await Server.WaitAssertion(() =>
        {
            AssertReader(SalvageLocker, "Salvage");
            AssertReader(QmLocker, "Quartermaster");
            AssertReader(SalvageDoor, "Salvage");
            AssertReader(CargoDoor, "Cargo");
            AssertReader(QmDoor, "Quartermaster");
        });
    }

    [Test]
    public async Task EveryJobAccessResolves()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var job in Proto.EnumeratePrototypes<JobPrototype>())
            {
                foreach (var level in job.Access.Concat(job.ExtendedAccess))
                {
                    Assert.That(Proto.HasIndex<AccessLevelPrototype>(level), Is.True,
                        $"{job.ID} references unknown access level {level}");
                }

                foreach (var group in job.AccessGroups.Concat(job.ExtendedAccessGroups))
                {
                    Assert.That(Proto.HasIndex<AccessGroupPrototype>(group), Is.True,
                        $"{job.ID} references unknown access group {group}");
                }
            }
        });
    }
}
