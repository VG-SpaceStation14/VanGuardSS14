#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._VanGuard.Economy.UI;
using Content.Client.CartridgeLoader;
using Content.Server._VanGuard.Economy.Components;
using Content.Server._VanGuard.Economy.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.PDA;
using Content.Shared.Station.Components;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VanGuard.Economy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._VanGuard.Economy;

[TestFixture]
public sealed class BankUiTest : InteractionTest
{
    [Test]
    public async Task AccountControlRebuildsHistoryInsteadOfAppending()
    {
        await Client.WaitPost(() =>
        {
            var control = new BankAccountControl();
            var starting = new BankUiTransaction(TimeSpan.FromMinutes(1), 1000, "starting-payroll", null);
            var payroll = new BankUiTransaction(TimeSpan.FromMinutes(11), 200, "payroll", null);

            var one = new BankAccountUiState(
                "123456789012",
                "Tester",
                1000,
                new List<BankUiTransaction> { starting });

            var two = new BankAccountUiState(
                "123456789012",
                "Tester",
                1200,
                new List<BankUiTransaction> { starting, payroll });

            var empty = new BankAccountUiState(
                "123456789012",
                "Tester",
                0,
                new List<BankUiTransaction>());

            control.UpdateState(one);
            control.UpdateState(two);
            control.UpdateState(two);

            var texts = Labels(control).Select(label => label.Text).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(
                    texts.Count(text => text?.Contains(Loc.GetString("bank-ui-history-starting-payroll")) == true),
                    Is.EqualTo(1),
                    "the opening balance must be rendered once regardless of update count");
                Assert.That(
                    texts.Count(text => text?.Contains(Loc.GetString("bank-ui-history-payroll")) == true),
                    Is.EqualTo(1),
                    "the salary must be rendered once regardless of update count");
            });

            control.UpdateState(empty);
            control.UpdateState(empty);

            texts = Labels(control).Select(label => label.Text).ToList();
            Assert.That(
                texts.Count(text => text == Loc.GetString("bank-ui-history-empty")),
                Is.EqualTo(1),
                "the empty statement must be rendered once regardless of update count");
        });
    }

    [Test]
    public async Task QuickAmountButtonsRecomputeOperationAvailability()
    {
        BankAccountControl? control = null;
        LineEdit? input = null;
        Button? quick10 = null;
        Button? quick50 = null;
        Button? quick100 = null;
        Button? max = null;
        Button? deposit = null;
        Button? withdraw = null;

        await Client.WaitPost(() =>
        {
            control = new BankAccountControl();
            UiMan.RootControl.AddChild(control);
            control.UpdateState(new BankAccountUiState(
                "123456789012",
                "Tester",
                50,
                new List<BankUiTransaction>(),
                authorized: true,
                hasCard: true,
                cash: 100));
            control.Arrange(new UIBox2(0, 0, 500, 700));

            input = Controls<LineEdit>(control).Single();
            quick10 = Controls<Button>(control).Single(button => button.Text == "+10");
            quick50 = Controls<Button>(control).Single(button => button.Text == "+50");
            quick100 = Controls<Button>(control).Single(button => button.Text == "+100");
            max = Controls<Button>(control).Single(button => button.Text == Loc.GetString("bank-ui-max"));
            deposit = Controls<Button>(control).Single(button => button.Text == Loc.GetString("bank-ui-deposit"));
            withdraw = Controls<Button>(control).Single(button => button.Text == Loc.GetString("bank-ui-withdraw"));

            Assert.That(deposit.Disabled, Is.True, "operations start disabled without an amount");
            Assert.That(withdraw.Disabled, Is.True, "operations start disabled without an amount");
        });

        await ClickControl(quick10!);
        await Client.WaitPost(() =>
        {
            Assert.That(input!.Text, Is.EqualTo("10"));
            Assert.That(deposit!.Disabled, Is.False);
            Assert.That(withdraw!.Disabled, Is.False);
        });

        await ClickControl(quick50!);
        await Client.WaitPost(() => Assert.That(input!.Text, Is.EqualTo("50")));

        await ClickControl(quick100!);
        await Client.WaitPost(() =>
        {
            Assert.That(input!.Text, Is.EqualTo("100"));
            Assert.That(deposit!.Disabled, Is.False);
            Assert.That(withdraw!.Disabled, Is.True, "withdraw above the current balance must be disabled");
        });

        await ClickControl(max!);
        await Client.WaitPost(() =>
        {
            Assert.That(input!.Text, Is.EqualTo("50"));
            Assert.That(withdraw!.Disabled, Is.False);
            UiMan.RootControl.RemoveChild(control!);
        });
    }

    [Test]
    public async Task CashControlsAndDepositAllResultUseServerState()
    {
        await Client.WaitPost(() =>
        {
            var control = new BankAccountControl();
            UiMan.RootControl.AddChild(control);

            control.UpdateState(new BankAccountUiState(
                "123456789012",
                "Tester",
                500,
                new List<BankUiTransaction>(),
                authorized: true,
                hasCard: true,
                cash: 0));

            var depositAll = Controls<Button>(control)
                .Single(button => button.Text == Loc.GetString("bank-ui-deposit-all"));
            Assert.That(depositAll.Disabled, Is.True, "deposit-all must be unavailable without cash");
            Assert.That(Labels(control).Any(label =>
                    label.Text == Loc.GetString("bank-ui-cash-available", ("cash", "0"))),
                Is.True);

            control.UpdateState(new BankAccountUiState(
                "123456789012",
                "Tester",
                750,
                new List<BankUiTransaction>(),
                lastStatus: BankTransactionStatus.Success,
                lastRequestId: 1,
                lastOperation: BankOperation.DepositAll,
                lastAmount: 250,
                authorized: true,
                hasCard: true,
                cash: 25));

            Assert.That(depositAll.Disabled, Is.False, "cash in hand must enable deposit-all");
            Assert.That(Labels(control).Any(label =>
                    label.Text == Loc.GetString("bank-ui-cash-available", ("cash", "25"))),
                Is.True);
            Assert.That(Labels(control).Any(label =>
                    label.Text == Loc.GetString("bank-ui-result-deposit-all",
                        ("amount", "250"), ("balance", "750"), ("cash", "25"))),
                Is.True);

            UiMan.RootControl.RemoveChild(control);
        });
    }

    [Test]
    public async Task AmountValidationUsesBalanceAndLimit()
    {
        await Client.WaitPost(() =>
        {
            var control = new BankAccountControl();
            control.UpdateState(new BankAccountUiState(
                "123456789012",
                "Tester",
                100,
                new List<BankUiTransaction>(),
                authorized: true,
                hasCard: true,
                cash: BankTransactionLimits.MaxTransaction));

            var input = Controls<LineEdit>(control).Single();
            var deposit = Controls<Button>(control).Single(button => button.Text == Loc.GetString("bank-ui-deposit"));
            var withdraw = Controls<Button>(control).Single(button => button.Text == Loc.GetString("bank-ui-withdraw"));

            input.SetText("100", invokeEvent: true);
            Assert.That(deposit.Disabled, Is.False);
            Assert.That(withdraw.Disabled, Is.False);

            input.SetText("101", invokeEvent: true);
            Assert.That(deposit.Disabled, Is.False);
            Assert.That(withdraw.Disabled, Is.True);

            input.SetText("0", invokeEvent: true);
            Assert.That(deposit.Disabled, Is.True);
            Assert.That(withdraw.Disabled, Is.True);

            input.SetText(BankTransactionLimits.MaxTransaction.ToString(), invokeEvent: true);
            Assert.That(deposit.Disabled, Is.False);
            Assert.That(withdraw.Disabled, Is.True);

            input.SetText((BankTransactionLimits.MaxTransaction + 1).ToString(), invokeEvent: true);
            Assert.That(deposit.Disabled, Is.True);
            Assert.That(withdraw.Disabled, Is.True);

            input.SetText("-10", invokeEvent: true);
            Assert.That(deposit.Disabled, Is.True);
            Assert.That(withdraw.Disabled, Is.True);
        });
    }

    [Test]
    public async Task AtmHeaderIsInsideWindowContents()
    {
        await Client.WaitPost(() =>
        {
            var window = new BankAtmWindow();
            var headers = Labels(window)
                .Where(label => label.Text == Loc.GetString("bank-atm-header"))
                .ToList();

            Assert.That(headers, Is.Not.Empty, "the ATM header labels must exist");
            Assert.That(headers.All(header => IsInside(header, window.ContentsContainer)), Is.True,
                "the bank header must be laid out inside the FancyWindow contents, below its title");
        });
    }

    [Test]
    public async Task CardHasNoDecorativeGlyphs()
    {
        await Client.WaitPost(() =>
        {
            var card = new BankCardDisplay();
            Assert.That(
                Labels(card).Any(label => label.Text?.Contains('◈') == true),
                Is.False,
                "the bank card must not render decorative diamond glyphs");
        });
    }

    [Test]
    public async Task BankLocalizationResolves()
    {
        await Client.WaitPost(() =>
        {
            var loc = Client.ResolveDependency<ILocalizationManager>();

            Assert.Multiple(() =>
            {
                Assert.That(loc.GetString("ent-BankATM"), Is.Not.EqualTo("ent-BankATM"),
                    "BankATM needs entity localization");
                Assert.That(loc.GetString("ent-BankATM.desc"), Is.Not.EqualTo("ent-BankATM.desc"),
                    "BankATM needs a localized description");
                Assert.That(loc.GetString("ent-BankCartridge"), Is.Not.EqualTo("ent-BankCartridge"),
                    "BankCartridge needs entity localization");
                Assert.That(loc.GetString("ent-BankCartridge.desc"), Is.Not.EqualTo("ent-BankCartridge.desc"),
                    "BankCartridge needs a localized description");
            });

            var window = new BankAtmWindow();
            var card = new BankCardDisplay();
            var account = new BankAccountControl();

            var displayedKeys = Labels(window)
                .Concat(Labels(card))
                .Concat(Labels(account))
                .Select(label => label.Text)
                .Where(text => text != null && text.StartsWith("bank-"))
                .ToList();

            Assert.That(displayedKeys, Is.Empty, "no raw bank localization key may be displayed");
        });
    }

    private static IEnumerable<T> Controls<T>(Control control) where T : Control
    {
        foreach (var child in control.Children)
        {
            if (child is T typed)
                yield return typed;

            foreach (var nested in Controls<T>(child))
                yield return nested;
        }
    }

    private static bool IsInside(Control control, Control ancestor)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent == ancestor)
                return true;
        }

        return false;
    }

    [Test]
    public async Task RealPdaSearchFillsTheTransferList()
    {
        var stationSystem = SEntMan.System<ServerStationSystem>();
        var bank = SEntMan.System<EconomyBankSystem>();
        EntityUid station = default;
        EntityUid recipientBody = default;

        await Server.WaitPost(() =>
        {
            station = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, default));
            SEntMan.AddComponent<StationDataComponent>(station);
            stationSystem.AddGridToStation(station, MapData.Grid);
        });
        await RunTicks(2);

        var recipientUser = new NetUserId(Guid.NewGuid());

        await Server.WaitPost(() =>
        {
            var mindSystem = SEntMan.System<SharedMindSystem>();
            SEntMan.EnsureComponent<MindContainerComponent>(SPlayer);
            var senderMindEnt = mindSystem.CreateMind(ClientSession.UserId, "Alice");
            mindSystem.TransferTo(senderMindEnt, SPlayer, mind: senderMindEnt);
            Assert.That(mindSystem.TryGetMind(SPlayer, out var senderMind, out var senderMindComp), Is.True);
            var senderAccount = bank.EnsureAccount(senderMind, senderMindComp);
            senderAccount.Balance = 500;
            senderAccount.OwningStation = station;

            recipientBody = SEntMan.SpawnEntity("InteractionTestMob",
                new EntityCoordinates(MapData.Grid, new Vector2(0.5f, 0.5f)));
            SEntMan.EnsureComponent<MindContainerComponent>(recipientBody);
            var recipientMind = mindSystem.CreateMind(null, "Bob");
            // Set the id directly: SetUserId validates against the player manager,
            // and a synthetic id is enough for the search filter under test.
            typeof(MindComponent).GetProperty("UserId")!.SetValue(recipientMind.Comp, recipientUser);
            mindSystem.TransferTo(recipientMind, recipientBody, mind: recipientMind);
            var recipientAccount = bank.EnsureAccount(recipientMind.Owner, recipientMind.Comp);
            recipientAccount.OwningStation = station;
        });
        await RunTicks(3);

        await SpawnTarget("PassengerPDA");
        var pda = ToServer(Target)!.Value;
        await Pickup();
        await UseInHand();

        var booted = false;
        for (var i = 0; i < 100 && !booted; i++)
        {
            await RunTicks(2);
            await Server.WaitPost(() => booted = SEntMan.GetComponent<PdaComponent>(pda).Booted);
        }
        Assert.That(booted, Is.True, "the PDA must finish booting");

        EntityUid cartridge = default;
        await Server.WaitPost(() =>
        {
            cartridge = SEntMan.System<CartridgeLoaderSystem>().GetAllPrograms(pda)
                .Single(uid => SEntMan.HasComponent<BankCartridgeComponent>(uid));
        });
        await SendBui(PdaUiKey.Key, new CartridgeLoaderUiMessage(
            SEntMan.GetNetEntity(cartridge), CartridgeUiMessageAction.Activate));
        await RunTicks(30);

        BankTransferControl? transfer = null;
        await Client.WaitPost(() =>
        {
            Assert.That(CEntMan.TryGetComponent<UserInterfaceComponent>(
                CEntMan.GetEntity(Target!.Value), out var uiComp), Is.True);
            Assert.That(uiComp!.ClientOpenInterfaces.TryGetValue(PdaUiKey.Key, out var bui), Is.True);

            var cartridgeUi = typeof(CartridgeLoaderBoundUserInterface)
                .GetField("_activeCartridgeUI", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(bui);
            Assert.That(cartridgeUi, Is.InstanceOf<BankCartridgeUi>());

            var fragment = (BankCartridgeUi)cartridgeUi!;
            var root = fragment.GetUIFragmentRoot();
            Controls<TabContainer>(root).Single().CurrentTab = 1;
            transfer = Controls<BankTransferControl>(root).Single();
            var search = Controls<LineEdit>(transfer)
                .Single(line => line.PlaceHolder == Loc.GetString("bank-ui-transfer-search-placeholder"));
            search.SetText("Bob", invokeEvent: true);
        });

        await RunTicks(60);

        await Server.WaitPost(() =>
        {
            var component = SEntMan.GetComponent<BankCartridgeComponent>(cartridge);
            Assert.That(component.TransferQuery, Is.EqualTo("Bob"));
            Assert.That(component.TransferSearchStatus, Is.EqualTo(BankTransferSearchStatus.Results));
            Assert.That(component.TransferResults, Has.Count.EqualTo(1));
            Assert.That(component.TransferResults[0].Name, Is.EqualTo("Bob"));
        });

        await Client.WaitPost(() =>
        {
            var bob = Labels(transfer!).First(label => label.Text == "Bob");
            Assert.That(bob.VisibleInTree, Is.True,
                "the actual PDA transfer tab must render the server result");
            Assert.That(bob.Size.X, Is.GreaterThan(0));
            Assert.That(bob.Size.Y, Is.GreaterThan(0));
            var select = Controls<Button>(transfer!)
                .First(button => button.Text == Loc.GetString("bank-ui-transfer-select"));
            Assert.That(select.VisibleInTree, Is.True);
            Assert.That(select.Size.X, Is.GreaterThan(0));
            Assert.That(select.Size.Y, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task TransferControlSelectsConfirmsAndHandlesResults()
    {
        BankTransferControl? control = null;
        LineEdit? amountInput = null;
        BankTransferRecipient? pendingRecipient = null;
        var requestId = 0;
        var requestedAmount = 0;

        var recipient = new BankTransferRecipient(
            new NetEntity(123),
            "Bob",
            "Cargo Technician",
            "•••• •••• •••• 5678");
        var results = new List<BankTransferRecipient> { recipient };

        await Client.WaitPost(() =>
        {
            control = new BankTransferControl();
            UiMan.RootControl.AddChild(control);
            control.Arrange(new UIBox2(0, 0, 500, 700));

            var search = Controls<LineEdit>(control)
                .Single(line => line.PlaceHolder == Loc.GetString("bank-ui-transfer-search-placeholder"));
            search.SetText("Bob", invokeEvent: true);

            control.UpdateState(
                new BankTransferUiState(BankTransferSearchStatus.Results, "Bob", results,
                    BankTransferStatus.None, 0, 0),
                new BankAccountUiState("123456789012", "Alice", 500, new List<BankUiTransaction>(),
                    authorized: true, hasCard: true));
            control.Arrange(new UIBox2(0, 0, 500, 700));

            amountInput = Controls<LineEdit>(control).Single(line => line != search);

            control.OnTransferRequested += (sentRecipient, sentAmount, sentRequest) =>
            {
                pendingRecipient = sentRecipient;
                requestedAmount = sentAmount;
                requestId = sentRequest;
            };
        });

        await Client.WaitPost(() =>
        {
            control!.SelectRecipient(recipient);
            amountInput!.SetText("50", invokeEvent: true);
            control.ShowConfirmation();
            control.Arrange(new UIBox2(0, 0, 500, 700));

            var confirmText = Loc.GetString("bank-ui-transfer-confirm",
                ("amount", "50"), ("name", "Bob"));
            Assert.That(Labels(control).Any(label => label.Text == confirmText && label.VisibleInTree), Is.True,
                "confirmation text must be shown before sending");

            control.ConfirmTransfer();
            Assert.That(pendingRecipient, Is.Not.Null);
            Assert.That(requestedAmount, Is.EqualTo(50));
        });

        await Client.WaitPost(() =>
        {
            control!.UpdateState(
                new BankTransferUiState(BankTransferSearchStatus.Results, "Bob", results,
                    BankTransferStatus.Success, requestId, 50),
                new BankAccountUiState("123456789012", "Alice", 450, new List<BankUiTransaction>(),
                    authorized: true, hasCard: true));

            Assert.That(amountInput!.Text, Is.Empty, "a successful transfer must clear the amount");
            Assert.That(Labels(control).Any(label =>
                    label.Text == Loc.GetString("bank-ui-transfer-success",
                        ("amount", "50"), ("name", "Bob"))),
                Is.True);
        });

        // The selected recipient and entered amount survive a failed transfer.
        await Client.WaitPost(() =>
        {
            control!.SelectRecipient(recipient);
            amountInput!.SetText("50", invokeEvent: true);
            control.ShowConfirmation();
            control.ConfirmTransfer();
        });

        await Client.WaitPost(() =>
        {
            control!.UpdateState(
                new BankTransferUiState(BankTransferSearchStatus.Results, "Bob", results,
                    BankTransferStatus.InsufficientFunds, requestId, 50),
                new BankAccountUiState("123456789012", "Alice", 450, new List<BankUiTransaction>(),
                    authorized: true, hasCard: true));

            Assert.That(amountInput!.Text, Is.EqualTo("50"));
            Assert.That(Labels(control).Any(label => label.Text == "Bob"), Is.True);
            Assert.That(Labels(control).Any(label =>
                    label.Text == Loc.GetString("bank-ui-status-insufficient-funds")),
                Is.True);

            UiMan.RootControl.RemoveChild(control!);
        });
    }

    [Test]
    public async Task TransferSearchEmitsDebouncedQuery()
    {
        BankTransferControl? control = null;
        string? emittedQuery = null;

        await Client.WaitPost(() =>
        {
            control = new BankTransferControl();
            UiMan.RootControl.AddChild(control);
            control.Arrange(new UIBox2(0, 0, 500, 700));
            var search = Controls<LineEdit>(control)
                .Single(line => line.PlaceHolder == Loc.GetString("bank-ui-transfer-search-placeholder"));
            control.OnSearchRequested += query => emittedQuery = query;
            search.SetText("Bob", invokeEvent: true);
        });

        await RunTicks(60);
        await Client.WaitPost(() =>
        {
            Assert.That(emittedQuery, Is.EqualTo("Bob"),
                "typing must emit a debounced search request");
            UiMan.RootControl.RemoveChild(control!);
        });
    }

    [Test]
    public async Task CartridgeHasOverviewAndTransfersTabs()
    {
        await Client.WaitPost(() =>
        {
            var cartridge = new BankCartridgeUi();
            cartridge.Setup(null!, null);

            var root = cartridge.GetUIFragmentRoot();
            var tabs = Controls<TabContainer>(root).Single();
            Assert.That(tabs.ChildCount, Is.EqualTo(2));
            Assert.That(TabContainer.GetTabTitle(tabs.Children.ElementAt(0)),
                Is.EqualTo(Loc.GetString("bank-tab-overview")));
            Assert.That(TabContainer.GetTabTitle(tabs.Children.ElementAt(1)),
                Is.EqualTo(Loc.GetString("bank-tab-transfers")));

            var account = new BankAccountUiState("123456789012", "Alice", 500,
                new List<BankUiTransaction>
                {
                    new(TimeSpan.FromMinutes(1), 500, "starting-payroll", null),
                }, authorized: true, hasCard: true);
            cartridge.UpdateState(new BankCartridgeUiState(
                account,
                new BankTransferUiState(BankTransferSearchStatus.Idle, string.Empty,
                    new List<BankTransferRecipient>(), BankTransferStatus.None, 0, 0)));

            Assert.That(Labels(root).Any(label => label.Text == "Alice"), Is.True);
            Assert.That(Labels(root).Any(label =>
                    label.Text == Loc.GetString("bank-ui-history-starting-payroll")),
                Is.True);
        });
    }

    private static IEnumerable<Label> Labels(Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is Label label)
                yield return label;

            foreach (var nested in Labels(child))
                yield return nested;
        }
    }
}
