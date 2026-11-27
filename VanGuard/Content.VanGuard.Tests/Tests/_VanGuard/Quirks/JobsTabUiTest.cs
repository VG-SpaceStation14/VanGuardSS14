#nullable enable
using System.Linq;
using Content.Client.Humanoid;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.IntegrationTests.Tests._VanGuard.Quirks;

/// <summary>
///     Layout regression test for the jobs tab: every department block must start
///     with its header. The inter-section spacer used to end up inside the colored
///     department panel, which pushed the contents of every block except the first
///     one down behind dead space.
/// </summary>
[TestFixture]
public sealed class JobsTabUiTest : GameTest
{
    public override PoolSettings PoolSettings => new() { InLobby = true };

    [Test]
    public async Task DepartmentBlocksAreUniform()
    {
        HumanoidProfileEditor? editor = null;

        await Client.WaitAssertion(() =>
        {
            editor = new HumanoidProfileEditor(
                Client.Resolve<IClientPreferencesManager>(),
                Client.Resolve<IConfigurationManager>(),
                CEntMan,
                Client.Resolve<IFileDialogManager>(),
                Client.Resolve<ILogManager>(),
                Client.Resolve<IPlayerManager>(),
                CProtoMan,
                Client.Resolve<IResourceManager>(),
                Client.Resolve<JobRequirementsManager>(),
                Client.Resolve<MarkingManager>());

            editor.SetProfile(HumanoidCharacterProfile.DefaultWithSpecies("Human", Sex.Male), 0);
        });

        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var jobList = editor!.FindControl<BoxContainer>("JobList");
            var blocks = jobList.Children.OfType<PanelContainer>().ToList();
            Assert.That(blocks, Is.Not.Empty, "the jobs tab has no department blocks");

            // Inter-section spacers live between the colored blocks...
            Assert.That(jobList.ChildCount, Is.EqualTo(blocks.Count + blocks.Count - 1),
                "expected exactly one separator control between department blocks");

            foreach (var block in blocks)
            {
                // ...and every block starts with its header, so no block has dead
                // space above the department name.
                Assert.That(block.GetChild(0), Is.TypeOf<BoxContainer>());
                var category = (BoxContainer) block.GetChild(0);
                Assert.That(category.GetChild(0), Is.TypeOf<PanelContainer>(),
                    "a department block must start with its header, not with a spacer");
            }
        });

        await Client.WaitAssertion(() => editor!.Dispose());
    }
}
