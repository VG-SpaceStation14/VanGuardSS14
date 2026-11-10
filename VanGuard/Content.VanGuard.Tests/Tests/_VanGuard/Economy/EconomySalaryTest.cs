using System.Collections.Generic;
#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Economy.Rules.Components;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

[TestFixture]
public sealed class EconomySalaryTest : GameTest
{
    private static readonly HashSet<string> AuditedJobs = new()
    {
        "AtmosphericTechnician", "Bartender", "Borg", "Botanist", "Brigmedic", "CBURN", "Captain", "CargoTechnician", "CentralCommandOfficial", "Chaplain", "Chef", "Chemist", "ChiefEngineer", "ChiefMedicalOfficer", "Clown", "DeathSquad", "Detective", "ERTChaplain", "ERTEngineer", "ERTJanitor", "ERTLeader", "ERTMedical", "ERTSecurity", "HeadOfPersonnel", "HeadOfSecurity", "IAA", "Janitor", "Lawyer", "Librarian", "Magistrate", "MedicalDoctor", "MedicalIntern", "Mime", "Miner", "Musician", "Paramedic", "Passenger", "Pilot", "Psychologist", "Quartermaster", "Reporter", "ResearchAssistant", "ResearchDirector", "SalvageSpecialist", "Scientist", "SecurityCadet", "SecurityOfficer", "SeniorEngineer", "SeniorOfficer", "SeniorPhysician", "SeniorResearcher", "ServiceWorker", "StationAi", "StationEngineer", "TechnicalAssistant", "TramDriver", "Visitor", "Warden"
    };

    [Test]
    public async Task AllJobsHaveAnExplicitSalaryAndTenMinuteInterval()
    {
        var jobs = SProtoMan.EnumeratePrototypes<JobPrototype>()
            .Where(job => AuditedJobs.Contains(job.ID))
            .ToList();
        Assert.That(jobs, Has.Count.EqualTo(58), "every production job prototype should be audited");

        foreach (var job in jobs)
        {
            Assert.That(job.Salary, Is.Not.Null,
                $"job {job.ID} must have an explicit salary, including event/admin roles represented by 0");
            Assert.That(job.Salary, Is.GreaterThanOrEqualTo(0),
                $"job {job.ID} must not have a negative salary");
        }

        Assert.Multiple(() =>
        {
            Assert.That(GetJob("Captain").Salary, Is.EqualTo(700));
            Assert.That(GetJob("HeadOfSecurity").Salary, Is.EqualTo(550));
            Assert.That(GetJob("SecurityOfficer").Salary, Is.EqualTo(400));
            Assert.That(GetJob("SecurityCadet").Salary, Is.EqualTo(220));
            Assert.That(GetJob("Passenger").Salary, Is.EqualTo(100));
            Assert.That(GetJob("Borg").Salary, Is.EqualTo(0));
        });

        var rule = new EconomyPaydayRuleComponent();
        Assert.That(rule.Interval, Is.EqualTo(TimeSpan.FromMinutes(10)));
    }

    [Test]
    public async Task SalaryHierarchyIsConsistent()
    {
        var captain = GetJob("Captain").Salary ?? 0;
        var head = GetJob("HeadOfSecurity").Salary ?? 0;
        var officer = GetJob("SecurityOfficer").Salary ?? 0;
        var cadet = GetJob("SecurityCadet").Salary ?? 0;

        Assert.That(captain, Is.GreaterThan(head));
        Assert.That(head, Is.GreaterThan(officer));
        Assert.That(officer, Is.GreaterThan(cadet));
        Assert.That(cadet, Is.GreaterThan(0));
    }
    private JobPrototype GetJob(string id)
    {
        return SProtoMan.Index(new ProtoId<JobPrototype>(id));
    }

}
