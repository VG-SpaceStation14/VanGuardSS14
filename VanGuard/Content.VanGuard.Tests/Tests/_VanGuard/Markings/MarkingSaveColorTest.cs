using System.Collections.Generic;
using System.Linq;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Markings;

/// <summary>
///     VG-Tweak: regression test for default markings losing their manually picked colors after saving a character.
///     The save path sanitizes the profile with <see cref="HumanoidCharacterAppearance.EnsureValid"/> and an empty
///     sponsor prototype list, because this fork has no sponsors manager implementation.
/// </summary>
/// <remarks>
///     A marking that is the required default of a markings group must not be sponsor-only: profile sanitization
///     would strip it and re-add it as a default, resetting its colors.
/// </remarks>
[TestFixture]
[TestOf(typeof(HumanoidCharacterAppearance))]
public sealed class MarkingSaveColorTest
{
    private static readonly Color SkinColor = new(0.6f, 0.4f, 0.2f);
    private static readonly Color CustomColor = new(0.2f, 0.5f, 0.8f);

    [Test]
    [TestCase("Vulpkanin", "FoxEar", TestName = "Vulpkanin ears keep their color on save")]
    [TestCase("Ipc", "IpcOrangeS", TestName = "Ipc snout keeps its color on save")]
    [TestCase("Tajaran", "TajaranOutears", TestName = "Tajaran ears keep their color on save")]
    public async Task DefaultMarkingColorSurvivesProfileSanitization(string speciesId, string markingId)
    {
        var species = new ProtoId<SpeciesPrototype>(speciesId);

        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var markingManager = server.ResolveDependency<MarkingManager>();

            var markingProto = proto.Index<MarkingPrototype>(markingId);
            var organData = markingManager.GetMarkingData(species);
            var organ = organData
                .First(data => data.Value.Layers.Contains(markingProto.BodyPart))
                .Key;
            var group = proto.Index(organData[organ].Group);

            // Make sure the test case really describes a required default marking of that species.
            Assert.That(group.Limits.TryGetValue(markingProto.BodyPart, out var limits), Is.True,
                $"{markingId} should be part of a limited layer of the {group.ID} group");
            Assert.That(limits!.Required, Is.True,
                $"Layer {markingProto.BodyPart} of the {group.ID} group should be a required layer");
            Assert.That(limits.Default.Select(it => it.Id), Does.Contain(markingId),
                $"{markingId} should be the default marking of layer {markingProto.BodyPart} of the {group.ID} group");

            var colorCount = markingProto.Sprites.Count;
            var customColors = Enumerable.Repeat(CustomColor, colorCount).ToList();

            var appearance = new HumanoidCharacterAppearance(
                Color.Black,
                SkinColor,
                new Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>>
                {
                    [organ] = new()
                    {
                        [markingProto.BodyPart] = new()
                        {
                            new Marking(markingProto.ID, customColors),
                        },
                    },
                });

            // Mirrors the sanitization done by ClientPreferencesManager and ServerPreferencesManager on save.
            var ensured = HumanoidCharacterAppearance.EnsureValid(appearance, species, Sex.Male, []);

            Assert.That(ensured.Markings.TryGetValue(organ, out var ensuredOrgan), Is.True,
                $"{markingId} should still be stored on organ {organ}");
            Assert.That(ensuredOrgan!.TryGetValue(markingProto.BodyPart, out var ensuredMarkings), Is.True,
                $"{markingId} should still be present on layer {markingProto.BodyPart}");

            Assert.That(ensuredMarkings, Has.Count.EqualTo(1),
                $"{markingId} should survive profile sanitization as a single marking");
            Assert.That(ensuredMarkings[0].MarkingId.Id, Is.EqualTo(markingId),
                $"{markingId} should not be replaced with a default one");
            Assert.That(ensuredMarkings[0].MarkingColors, Is.EqualTo(customColors),
                "A manually picked color should not be reset to the default color on save");
        });

        await pair.CleanReturnAsync();
    }
}
