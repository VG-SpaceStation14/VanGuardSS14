#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Radio;
using Content.Shared._VanGuard.Radio;
using Content.Shared.Robotics.Components;
using Content.Shared.StatusIcon;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VanGuard.Radio;

/// <summary>
///     Covers the per-prototype radio icons used by machines and devices that do not wear ID cards.
/// </summary>
[TestFixture]
public sealed class RadioMachineIconTest : GameTest
{
    [TestCase("ComputerResearchAndDevelopment", "JobIconMachine")]
    [TestCase("ComputerRoboticsControl", "JobIconMachine")]
    [TestCase("ComputerXenoborgsControl", "JobIconMachine")]
    [TestCase("ComputerSalvageJobBoard", "JobIconMachine")]
    [TestCase("ComputerCriminalRecords", "JobIconCriminalRecords")]
    [TestCase("ComputerCargoOrders", "JobIconCargoConsole")]
    [TestCase("BoxFolderQmClipboard", "JobIconCargoConsole")]
    [TestCase("SalvageMagnet", "JobIconSalvageMagnet")]
    [TestCase("MachineAnomalyGenerator", "JobIconAnomalyGenerator")]
    [TestCase("Emitter", "JobIconMachine")]
    [TestCase("MachineAPE", "JobIconMachine")]
    [TestCase("Protolathe", "JobIconMachine")]
    [TestCase("ExperimentScanner", "JobIconMachine")]
    [TestCase("ExperimentFloorScanner", "JobIconMachine")]
    [TestCase("BorgChassisGeneric", "JobIconBorg")]
    [TestCase("AiHeld", "JobIconStationAi")]
    public async Task OverrideIconsAreResolved(string prototype, string expectedIcon)
    {
        EntityUid entity = default;
        await Server.WaitPost(() => entity = SSpawn(prototype));

        var markup = string.Empty;
        await Server.WaitPost(() => markup = SEntMan.System<RadioJobIconSystem>().GetIconMarkup(entity));

        Assert.That(markup, Is.EqualTo($"[jobicon=\"{expectedIcon}\"/]"));
    }

    [Test]
    public async Task XenoborgConsoleKeepsItsRadioChannel()
    {
        await Server.WaitAssertion(() =>
        {
            var entity = SSpawn("ComputerXenoborgsControl");
            var console = SEntMan.GetComponent<RoboticsConsoleComponent>(entity);
            Assert.That(console.RadioChannel.Id, Is.EqualTo("Xenoborg"));
        });
    }

    [Test]
    public async Task AllOverridesReferenceExistingJobIcons()
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var prototype in SProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (!prototype.TryComp<RadioIconOverrideComponent>(out var component, SEntMan.ComponentFactory))
                    continue;

                Assert.That(SProtoMan.HasIndex<JobIconPrototype>(component.Icon), Is.True,
                    $"{prototype.ID} references a missing job icon: {component.Icon}");
            }
        });
    }

    [Test]
    public async Task MachineIconRsiIsValid()
    {
        await Client.WaitAssertion(() =>
        {
            var rsi = Client.Resolve<IResourceCache>().GetResource<RSIResource>(
                new ResPath("/Textures/_VanGuard/Interface/Misc/machine_job_icons.rsi"));

            Assert.That(rsi.RSI.Size, Is.EqualTo(new Vector2i(8, 8)));

            foreach (var state in new[]
                     {
                         "Machine",
                         "SalvageMagnet",
                         "AnomalyGenerator",
                         "CargoConsole",
                         "CriminalRecords",
                     })
            {
                Assert.That(rsi.RSI.TryGetState(state, out _), Is.True,
                    $"the machine job icon RSI is missing the {state} state");
            }
        });
    }

    [Test]
    public async Task OverrideIconTextureLoadsOnClient()
    {
        await Client.WaitAssertion(() =>
        {
            string[] iconIds =
            [
                "JobIconMachine",
                "JobIconSalvageMagnet",
                "JobIconAnomalyGenerator",
                "JobIconCargoConsole",
                "JobIconCriminalRecords",
            ];

            foreach (var iconId in iconIds)
            {
                var label = new RichTextLabel();
                label.SetMessage(FormattedMessage.FromMarkupOrThrow($"[jobicon=\"{iconId}\"/] Test"),
                    tagsAllowed: null);

                var icon = label.Children.OfType<TextureRect>().SingleOrDefault();
                Assert.That(icon, Is.Not.Null, $"{iconId} did not create an inline icon");
                Assert.That(icon!.Texture, Is.Not.Null, $"{iconId} has no texture");

                var prototype = CProtoMan.Index<JobIconPrototype>(iconId);
                Assert.That(prototype.LocalizedJobName, Is.Not.EqualTo(prototype.JobName),
                    $"{iconId} has a missing localization key");
                Assert.That(icon.ToolTip, Is.EqualTo(prototype.LocalizedJobName));

                label.Orphan();
            }
        });
    }
}
