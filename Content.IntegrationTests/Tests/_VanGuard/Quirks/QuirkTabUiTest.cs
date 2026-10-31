#nullable enable
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
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.IntegrationTests.Tests._VanGuard.Quirks;

/// <summary>
///     Smoke test for the VanGuard quirk tab: building the character editor with quirks selected must not
///     throw, and the profile must keep its quirk selection.
/// </summary>
[TestFixture]
public sealed class QuirkTabUiTest : GameTest
{
    public override PoolSettings PoolSettings => new() { InLobby = true };

    [Test]
    public async Task EditorBuildsQuirkTab()
    {
        await Client.WaitAssertion(() =>
        {
            var editor = new HumanoidProfileEditor(
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

            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human", Sex.Male)
                .WithTraitPreference("QuirkFleetFooted", CProtoMan)
                .WithTraitPreference("Pacifist", CProtoMan);

            editor.SetProfile(profile, 0);

            Assert.That(editor.Profile, Is.Not.Null);
            Assert.That(editor.Profile!.TraitPreferences, Does.Contain("QuirkFleetFooted"));
            Assert.That(editor.Profile.TraitPreferences, Does.Contain("Pacifist"));

            // The tab is rebuilt after every change, it must stay safe.
            editor.RefreshTraits();

            editor.Dispose();
        });
    }
}
