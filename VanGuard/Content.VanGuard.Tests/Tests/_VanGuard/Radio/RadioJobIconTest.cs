#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._VanGuard.Radio;
using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Content.Shared.StatusIcon;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._VanGuard.Radio;

/// <summary>
///     Covers the job icon shown in radio messages: the server supplies an icon prototype id,
///     while the client turns it into an actual inline texture.
/// </summary>
[TestFixture]
public sealed class RadioJobIconTest : GameTest
{
    private static readonly ProtoId<JobIconPrototype> CaptainIcon = "JobIconCaptain";
    private static readonly ProtoId<JobIconPrototype> NoIdIcon = "JobIconNoId";

    [Test]
    public async Task MarkupUsesEquippedIdCardIcon()
    {
        EntityUid mob = default;

        await Server.WaitPost(() =>
        {
            mob = SSpawn("MobHuman");
            var card = SSpawn("CaptainIDCard");

            SEntMan.GetComponent<IdCardComponent>(card).JobIcon = "JobIconCaptain";
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(mob, card, "id", force: true), Is.True,
                "the ID card should be equippable");
        });

        var markup = string.Empty;
        await Server.WaitPost(() => markup = SEntMan.System<RadioJobIconSystem>().GetIconMarkup(mob));

        Assert.That(markup, Is.EqualTo("[jobicon=\"JobIconCaptain\"/]"));
    }

    [Test]
    public async Task MarkupFallsBackWithoutIdCard()
    {
        EntityUid mob = default;
        await Server.WaitPost(() => mob = SSpawn("MobHuman"));

        var markup = string.Empty;
        await Server.WaitPost(() => markup = SEntMan.System<RadioJobIconSystem>().GetIconMarkup(mob));

        Assert.That(markup, Is.EqualTo("[jobicon=\"JobIconNoId\"/]"));
    }

    [Test]
    public async Task MarkupFallsBackForUnknownIcon()
    {
        EntityUid mob = default;
        EntityUid card = default;

        await Server.WaitPost(() =>
        {
            mob = SSpawn("MobHuman");
            card = SSpawn("PassengerIDCard");

            Assert.That(SEntMan.System<InventorySystem>().TryEquip(mob, card, "id", force: true), Is.True,
                "the ID card should be equippable");
        });

        var markup = string.Empty;
        await Server.WaitPost(() =>
        {
            var idCard = SEntMan.GetComponent<IdCardComponent>(card);
            var originalIcon = idCard.JobIcon;

            idCard.JobIcon = "JobIconDoesNotExist";
            markup = SEntMan.System<RadioJobIconSystem>().GetIconMarkup(mob);

            // Do not leave a deliberately broken prototype id on a networked component.
            idCard.JobIcon = originalIcon;
        });

        Assert.That(markup, Is.EqualTo("[jobicon=\"JobIconNoId\"/]"));
    }

    [Test]
    public async Task RadioWrapRendersInlineJobIcon()
    {
        await Client.WaitAssertion(() =>
        {
            var wrapped = Loc.GetString("chat-radio-message-wrap",
                ("color", Color.White),
                ("fontType", "Default"),
                ("fontSize", 12),
                ("verb", "says"),
                ("channel", "[Common]"),
                ("name", "Test"),
                ("jobIcon", "[jobicon=\"JobIconCaptain\"/]"),
                ("message", "Hello"));

            var label = new RichTextLabel();
            label.SetMessage(FormattedMessage.FromMarkupOrThrow(wrapped), tagsAllowed: null);

            var icon = label.Children.OfType<TextureRect>().SingleOrDefault();
            Assert.That(icon, Is.Not.Null, "the radio message did not create an inline job icon");
            Assert.That(icon!.Texture, Is.Not.Null, "the inline job icon has no texture");
            Assert.That(icon.SetWidth, Is.EqualTo(16f));
            Assert.That(icon.SetHeight, Is.EqualTo(16f));
            Assert.That(icon.ToolTip,
                Is.EqualTo(CProtoMan.Index(CaptainIcon).LocalizedJobName));

            label.Orphan();
        });
    }

    [Test]
    public async Task ClientFallsBackForUnknownIcon()
    {
        await Client.WaitAssertion(() =>
        {
            var label = new RichTextLabel();
            label.SetMessage(FormattedMessage.FromMarkupOrThrow("[jobicon=\"JobIconDoesNotExist\"/] Test"),
                tagsAllowed: null);

            var icon = label.Children.OfType<TextureRect>().SingleOrDefault();
            Assert.That(icon, Is.Not.Null, "the fallback job icon was not created");
            Assert.That(icon!.Texture, Is.Not.Null, "the fallback job icon has no texture");
            Assert.That(icon.ToolTip,
                Is.EqualTo(CProtoMan.Index(NoIdIcon).LocalizedJobName));

            label.Orphan();
        });
    }
}
