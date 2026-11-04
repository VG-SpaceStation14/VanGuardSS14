using System.Collections.Generic;
using System.Linq;
using Content.Client.Humanoid;
using Content.IntegrationTests.Pair;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._VanGuard.Markings;

/// <summary>
///     VG-Tweak: checks that markings whose colors are generated automatically (skin/fur and eye colors)
///     keep following the character's colors, while manually picked colors stay untouched.
/// </summary>
[TestFixture]
[TestOf(typeof(MarkingsViewModel))]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class MarkingAutomaticColorTest
{
    private static readonly ProtoId<SpeciesPrototype> TestSpecies = "Moth";
    private static readonly ProtoId<OrganCategoryPrototype> Head = "Head";
    private static readonly ProtoId<MarkingPrototype> MothAntennasDefault = "MothAntennasDefault";

    public TestPair Pair = default!;
    public RobustIntegrationTest.ClientIntegrationInstance Client => Pair.Client;
    private MarkingsViewModel _model = default!;

    [SetUp]
    public async Task SetUp()
    {
        Pair = await PoolManager.GetServerClient(testContext: new NUnitTestContextWrap(TestContext.CurrentContext, TestContext.Out));
        await Client.WaitPost(() =>
        {
            var manager = Client.ResolveDependency<MarkingManager>();

            _model = new MarkingsViewModel();
            _model.OrganData = manager.GetMarkingData(TestSpecies);
            _model.OrganProfileData = manager.GetProfileData(TestSpecies, Sex.Male, Color.White, Color.White);
            _model.ValidateMarkings();
        });
    }

    [TearDown]
    public async Task TearDown()
    {
        await Pair.CleanReturnAsync();
    }

    [Test]
    public async Task AutomaticMarkingColorsFollowSkinColor()
    {
        await Client.WaitAssertion(() =>
        {
            // The default moth antennae use the skin-derived coloration, so they start out white.
            Assert.That(HeadTopColors()[0], Is.EqualTo(Color.White));

            _model.SetOrganSkinColor(Color.Red);
            Assert.That(HeadTopColors()[0], Is.EqualTo(Color.Red),
                "Markings with automatic colors should follow the skin color");

            _model.SetOrganSkinColor(Color.Blue);
            Assert.That(HeadTopColors()[0], Is.EqualTo(Color.Blue),
                "Markings with automatic colors should keep following the skin color");
        });
    }

    [Test]
    public async Task CustomMarkingColorsDoNotFollowSkinColor()
    {
        await Client.WaitAssertion(() =>
        {
            _model.TrySetMarkingColor(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault, 0, Color.Green);

            _model.SetOrganSkinColor(Color.Red);

            Assert.That(HeadTopColors()[0], Is.EqualTo(Color.Green),
                "Markings with manually picked colors should ignore the skin color");
        });
    }

    [Test]
    public async Task AutomaticMarkingColorsAreNotRestoredOnReselect()
    {
        await Client.WaitAssertion(() =>
        {
            _model.EnforceLimits = false;
            _model.SetOrganSkinColor(Color.Red);

            Assert.That(_model.TryDeselectMarking(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault), Is.True);

            _model.SetOrganSkinColor(Color.Blue);

            Assert.That(_model.TrySelectMarking(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault), Is.True);
            Assert.That(HeadTopColors()[0], Is.EqualTo(Color.Blue),
                "Re-selecting a marking with automatic colors should use the current skin color");
        });
    }

    private List<Color> HeadTopColors()
    {
        return _model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)![0].MarkingColors.ToList();
    }
}
