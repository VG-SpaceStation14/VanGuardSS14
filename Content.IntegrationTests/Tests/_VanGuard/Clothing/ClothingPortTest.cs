#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Clothing;

/// <summary>
///     Verifies the restored command wardrobe: every item prototype exists with a clothing slot and every
///     loadout entry is wired into the matching loadout group.
/// </summary>
[TestFixture]
public sealed class ClothingPortTest : GameTest
{
    private static IPrototypeManager Proto => IoCManager.Instance!.Resolve<IPrototypeManager>();

    private static readonly (string Item, string Slot, string Group)[] Items =
    {
        ("ClothingNeckCloakCapOriginal", "NECK", "CaptainNeck"),
        ("ClothingNeckCloakCapAlt", "NECK", "CaptainNeck"),
        ("ClothingNeckCloakCapFormalAlt", "NECK", "CaptainNeck"),
        ("ClothingNeckCaptainWide", "NECK", "CaptainNeck"),
        ("ClothingNeckMantleCaptainAlt", "NECK", "CaptainNeck"),
        ("ClothingNeckMantleCaptainWhite", "NECK", "CaptainNeck"),
        ("ClothingNeckCloakHopAlt", "NECK", "HoPNeck"),
        ("ClothingNeckLongCloakHOP", "NECK", "HoPNeck"),
        ("ClothingNeckCloakHosOriginal", "NECK", "HeadofSecurityNeck"),
        ("ClothingNeckCloakHosAlt", "NECK", "HeadofSecurityNeck"),
        ("ClothingNeckLongCloakHOS", "NECK", "HeadofSecurityNeck"),
        ("ClothingNeckCloakCeOriginal", "NECK", "ChiefEngineerNeck"),
        ("ClothingNeckCloakCeAlt", "NECK", "ChiefEngineerNeck"),
        ("ClothingNeckLongCloakCE", "NECK", "ChiefEngineerNeck"),
        ("ClothingNeckMantleCEOriginal", "NECK", "ChiefEngineerNeck"),
        ("ClothingNeckCloakCmoAlt", "NECK", "ChiefMedicalOfficerNeck"),
        ("ClothingNeckCloakCmoTurquoise", "NECK", "ChiefMedicalOfficerNeck"),
        ("ClothingNeckLongCloakCMO", "NECK", "ChiefMedicalOfficerNeck"),
        ("ClothingNeckMantleCMOOriginal", "NECK", "ChiefMedicalOfficerNeck"),
        ("ClothingNeckCloakRdOriginal", "NECK", "ResearchDirectorNeck"),
        ("ClothingNeckCloakRdAlt", "NECK", "ResearchDirectorNeck"),
        ("ClothingNeckLongCloakRD", "NECK", "ResearchDirectorNeck"),
        ("ClothingNeckMantleRDOriginal", "NECK", "ResearchDirectorNeck"),
        ("ClothingNeckCloakQmAlt", "NECK", "QuartermasterNeck"),
        ("ClothingNeckLongCloakQM", "NECK", "QuartermasterNeck"),
        ("ClothingHeadHatBeretCaptainWhite", "HEAD", "CaptainHead"),
        ("ClothingHeadHatCapcapWhite", "HEAD", "CaptainHead"),
        ("ClothingHeadHatBeretCmoOriginal", "HEAD", "ChiefMedicalOfficerHead"),
        ("ClothingHeadHatCapSec", "HEAD", "HeadofSecurityHead"),
        ("ClothingHeadHatFlightCapSec", "HEAD", "HeadofSecurityHead"),
        ("ClothingHeadHatCargoPostalsoft", "HEAD", "CargoTechnicianHead"),
        ("ClothingUniformJumpsuitCaptainWhite", "INNERCLOTHING", "CaptainJumpsuit"),
        ("ClothingUniformJumpskirtCaptainWhite", "INNERCLOTHING", "CaptainJumpsuit"),
        ("ClothingUniformJumpsuitCommandCaptain", "INNERCLOTHING", "CaptainJumpsuit"),
        ("ClothingUniformJumpsuitCaptainParadeMale", "INNERCLOTHING", "CaptainJumpsuit"),
        ("ClothingUniformJumpsuitHoSOriginal", "INNERCLOTHING", "HeadofSecurityJumpsuit"),
        ("ClothingUniformJumpsuitHoSParadeMaleOriginal", "INNERCLOTHING", "HeadofSecurityJumpsuit"),
        ("ClothingUniformJumpskirtHoSOriginal", "INNERCLOTHING", "HeadofSecurityJumpsuit"),
        ("ClothingUniformJumpskirtHoSParadeFemaleOriginal", "INNERCLOTHING", "HeadofSecurityJumpsuit"),
        ("ClothingUniformJumpsuitScientistOriginal", "INNERCLOTHING", "ScientistJumpsuit"),
        ("ClothingUniformJumpskirtScientistOriginal", "INNERCLOTHING", "ScientistJumpsuit"),
        ("ClothingUniformJumpsuitRndAlt", "INNERCLOTHING", "ScientistJumpsuit"),
        ("ClothingUniformJumpsuitPsychologistOriginal", "INNERCLOTHING", "PsychologistJumpsuit"),
        ("ClothingUniformJumpskirtPsychologistOriginal", "INNERCLOTHING", "PsychologistJumpsuit"),
        ("ClothingUniformJumpsuitCargoPostal", "INNERCLOTHING", "CargoTechnicianJumpsuit"),
        ("ClothingUniformJumpskirtCargoPostal", "INNERCLOTHING", "CargoTechnicianJumpsuit"),
        ("ClothingUniformJumpsuitAtmosCasualOriginal", "INNERCLOTHING", "AtmosphericTechnicianJumpsuit"),
    };

    [Test]
    public async Task RestoredItemsAreClothing()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var (item, slot, _) in Items)
            {
                var proto = Proto.Index<EntityPrototype>(item);
                Assert.That(proto.Components.TryGetComponent("Clothing", out var clothing), Is.True,
                    $"{item} must be wearable");
                var expected = slot switch
                {
                    "NECK" => SlotFlags.NECK,
                    "HEAD" => SlotFlags.HEAD,
                    _ => SlotFlags.INNERCLOTHING,
                };
                Assert.That(((ClothingComponent)clothing!).Slots.HasFlag(expected), Is.True,
                    $"{item} should use the {slot} slot");
            }
        });
    }

    [Test]
    public async Task RestoredItemsAreReachableThroughLoadouts()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var (item, _, groupId) in Items)
            {
                var loadoutId = item + "Loadout";
                Assert.That(Proto.HasIndex<LoadoutPrototype>(loadoutId), Is.True,
                    $"{loadoutId} should exist");
                var group = Proto.Index<LoadoutGroupPrototype>(groupId);
                Assert.That(group.Loadouts.Select(l => l.Id), Does.Contain(loadoutId),
                    $"{loadoutId} should be offered by the {groupId} group");
            }
        });
    }
}