#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._VanGuard.VendingMachines.UI;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._VanGuard.Vending;

/// <summary>
///     Regression test for the compact vending cards rendering as empty squares:
///     builds a real <see cref="FancyVendingMachineItemCard"/> inside the client UI
///     root and asserts that every text control inside it carries text and gets a
///     non-zero pixel size after the layout pass.
/// </summary>
[TestFixture]
public sealed class VendingMachineUiTest : InteractionTest
{
    private FancyVendingMachineItemCard? _card;

    private record struct TextControl(string? Text, Control Control);

    [Test]
    public async Task CardLabelsHaveTextAndSize()
    {
        await Client.WaitPost(() =>
        {
            _card = new FancyVendingMachineItemCard();
            _card.SetItem(new EntProtoId("DrinkColaCan"), "Space Cola", 5, 3, allForFree: false, enabled: true);
            UiMan.StateRoot.AddChild(_card);
        });

        await RunTicksSync(5);

        await Client.WaitPost(() =>
        {
            Assert.That(_card, Is.Not.Null);
            Assert.That(_card!.PixelSize, Is.Not.EqualTo(Vector2i.Zero), "card has zero pixel size");

            var labels = new List<TextControl>();
            CollectTextControls(_card, labels);

            // The card carries the price, stock count and item name.
            var contentLabels = labels.Where(l => !string.IsNullOrEmpty(l.Text)).ToList();
            Assert.That(contentLabels.Count, Is.GreaterThanOrEqualTo(3),
                $"expected 3+ content labels, found {contentLabels.Count}");

            foreach (var label in contentLabels)
            {
                Assert.That(label.Control.PixelSize, Is.Not.EqualTo(Vector2i.Zero),
                    $"label '{label.Text}' has zero pixel size");
            }
        });
    }

    [Test]
    public async Task MenuGridLaysOutCardLabels()
    {
        FancyVendingMachineMenu? menu = null;

        await Client.WaitPost(() =>
        {
            menu = new FancyVendingMachineMenu();
            var entries = new List<VendingMachineInventoryEntry>
            {
                new(InventoryType.Regular, new EntProtoId("DrinkColaCan"), 3) { Price = 5 },
                new(InventoryType.Regular, new EntProtoId("DrinkGrapeCan"), 2) { Price = 4 },
            };
            menu.Populate(entries, true, false);
            UiMan.StateRoot.AddChild(menu);
        });

        await RunTicksSync(5);

        await Client.WaitPost(() =>
        {
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu!.PixelSize, Is.Not.EqualTo(Vector2i.Zero), "menu has zero pixel size");

            var labels = new List<TextControl>();
            CollectTextControls(menu, labels);

            // 2 cards x (price, count, name) = 6 content labels.
            var contentLabels = labels.Where(l => l.Control.Visible && !string.IsNullOrEmpty(l.Text)).ToList();
            Assert.That(contentLabels.Count, Is.GreaterThanOrEqualTo(6),
                $"expected 6+ content labels, found {contentLabels.Count}");

            foreach (var label in contentLabels)
            {
                Assert.That(label.Control.PixelSize, Is.Not.EqualTo(Vector2i.Zero),
                    $"label '{label.Text}' has zero pixel size");
            }
        });
    }

    [Test]
    public async Task OpenWindowThroughBuiShowsText()
    {
        await SpawnTarget("VendingMachineCola");

        // The machine needs grid power before it opens its UI on activation.
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicksSync(1);

        await Activate();
        Assert.That(IsUiOpen(VendingMachineUiKey.Key), "vending BUI failed to open.");

        await RunTicksSync(5);

        var menu = GetWindow<FancyVendingMachineMenu>();

        await RunTicksSync(5);

        await Client.WaitPost(() =>
        {
            Assert.That(menu.PixelSize, Is.Not.EqualTo(Vector2i.Zero), "open window has zero pixel size");

            var labels = new List<TextControl>();
            CollectTextControls(menu, labels);
            var contentLabels = labels.Where(l => l.Control.Visible && !string.IsNullOrEmpty(l.Text)).ToList();
            Assert.That(contentLabels.Count, Is.GreaterThanOrEqualTo(3),
                $"expected content labels in the open window, found {contentLabels.Count}");

            foreach (var label in contentLabels)
            {
                Assert.That(label.Control.PixelSize, Is.Not.EqualTo(Vector2i.Zero),
                    $"label '{label.Text}' has zero pixel size");
            }
        });
    }

    private static void CollectTextControls(Control control, List<TextControl> result)
    {
        switch (control)
        {
            case Label label:
                result.Add(new TextControl(label.Text, label));
                break;
            case RichTextLabel rich:
                result.Add(new TextControl(rich.GetMessage(), rich));
                break;
        }

        foreach (var child in control.Children)
            CollectTextControls(child, result);
    }
}
