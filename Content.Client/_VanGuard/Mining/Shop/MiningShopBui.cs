using Content.Client.Stylesheets;
using Content.Shared._VanGuard.Mining.Points;
using Content.Shared._VanGuard.Mining.Shop;
using Content.Shared._VanGuard.Mining.Shop.Components;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using System.Collections.Generic;
using System.Numerics;
using static System.StringComparison;

namespace Content.Client._VanGuard.Mining.Shop;

/// <summary>
/// Bound user interface for the mining shop vendor window: a search bar, category tabs
/// and a grid of item cards, with a checkout row (orders + express delivery) at the bottom.
/// </summary>
public sealed partial class MiningShopBui : BoundUserInterface
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private readonly MiningPointsSystem _miningPoints;

    private MiningShopWindow? _window;
    private Button? _allTab;

    private readonly List<(MiningShopSectionPrototype Section, Label Header, GridContainer Grid, List<(MiningShopItemCard Card, uint? Price)> Items)> _blocks = new();
    private readonly Dictionary<string, Button> _tabButtons = new();
    private readonly List<Button> _allTabs = new();

    private string? _currentSectionId;
    private string _searchText = string.Empty;

    public MiningShopBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _miningPoints = EntMan.System<MiningPointsSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = new MiningShopWindow();
        _window.OnClose += Close;
        _window.Title = EntMan.GetComponentOrNull<MetaDataComponent>(Owner)?.EntityName ?? "MiningShop";
        _window.ShopNameLabel.Text = _window.Title;

        if (!EntMan.TryGetComponent(Owner, out MiningShopComponent? vendor))
            return;

        BuildTabs();
        BuildItems();

        _window.Search.OnTextChanged += OnSearch;

        // Collecting the order requires a hold to confirm, so the player cannot
        // accidentally hand out the delivery bag.
        _window.Express.HoldDuration = 1.25f;
        _window.Express.FillColor = new Color(201, 162, 39, 150);
        _window.Express.ToolTip = Loc.GetString("mining-shop-hold-to-collect");
        _window.Express.Confirmed += () => SendMessage(new MiningShopExpressDeliveryBuiMsg());

        _window.OpenCentered();
        Refresh();
    }

    private void BuildTabs()
    {
        _allTab = new Button
        {
            Text = Loc.GetString("mining-shop-section-all"),
            ToggleMode = true,
            Pressed = true,
        };
        _allTab.OnPressed += _ => SetSection(null);
        _window!.Tabs.AddChild(_allTab);
        _allTabs.Add(_allTab);

        foreach (var section in _proto.EnumeratePrototypes<MiningShopSectionPrototype>())
        {
            var tab = new Button
            {
                Text = GetSectionName(section),
                ToggleMode = true,
            };

            var sectionId = section.ID;
            tab.OnPressed += _ => SetSection(sectionId);

            _window.Tabs.AddChild(tab);
            _allTabs.Add(tab);
            _tabButtons[sectionId] = tab;
        }
    }

    private void BuildItems()
    {
        foreach (var section in _proto.EnumeratePrototypes<MiningShopSectionPrototype>())
        {
            var items = new List<(MiningShopItemCard Card, uint? Price)>();

            var header = new Label
            {
                Text = GetSectionName(section),
                Margin = new Thickness(0, 10, 0, 4),
            };
            header.AddStyleClass(StyleClass.LabelHeading);

            var grid = new GridContainer
            {
                Columns = 4,
                HorizontalExpand = true,
                HorizontalAlignment = Control.HAlignment.Left,
                HSeparationOverride = 4,
                VSeparationOverride = 4,
            };

            foreach (var entry in section.Entries)
            {
                if (!_proto.TryIndex(entry.Id, out var entity))
                    continue;

                var card = new MiningShopItemCard();
                var name = entry.Name?.Replace("\\n", "\n") ?? entity.Name;

                card.SetItem(entry.Id, name, entity.Description);
                card.SetPrice(entry.Price);
                card.OnPressed += () => SendMessage(new MiningShopVendBuiMsg(entry));

                grid.AddChild(card);
                items.Add((card, entry.Price));
            }

            _window!.Catalog.AddChild(header);
            _window.Catalog.AddChild(grid);
            _blocks.Add((section, header, grid, items));
        }
    }

    private void SetSection(string? sectionId)
    {
        _currentSectionId = sectionId;

        foreach (var tab in _allTabs)
            tab.Pressed = false;

        if (sectionId == null)
            _allTab!.Pressed = true;
        else if (_tabButtons.TryGetValue(sectionId, out var tab))
            tab.Pressed = true;

        ApplyFilter();
    }

    private void OnSearch(LineEdit.LineEditEventArgs args)
    {
        _searchText = args.Text.Trim();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var any = false;
        foreach (var (section, header, grid, items) in _blocks)
        {
            var inSection = _currentSectionId == null || section.ID == _currentSectionId;
            var anyMatch = false;

            foreach (var (card, _) in items)
            {
                var matches = _searchText.Length == 0 || card.SearchText.Contains(_searchText, OrdinalIgnoreCase);
                card.Visible = inSection && matches;
                anyMatch |= card.Visible;
            }

            header.Visible = inSection && anyMatch;
            grid.Visible = inSection && anyMatch;
            any |= anyMatch;
        }

        if (_window == null)
            return;

        _window.EmptyLabel.Visible = !any;
        _window.Catalog.Visible = any;
    }

    public void Refresh()
    {
        if (_window == null || _player.LocalEntity == null)
            return;

        if (!EntMan.TryGetComponent(Owner, out MiningShopComponent? vendor))
            return;

        var userPoints = _miningPoints.TryFindIdCard(_player.LocalEntity.Value)?.Comp?.Points ?? 0;

        _window.PointsLabel.Text = Loc.GetString("mining-shop-points", ("points", userPoints));
        _window.Express.Disabled = vendor.OrderList.Count <= 0;

        // Rebuild the checkout order list with a cancel button per order.
        _window.OrderList.RemoveAllChildren();

        if (vendor.OrderList.Count == 0)
        {
            _window.OrderList.AddChild(new Label
            {
                Text = Loc.GetString("mining-shop-orders-empty"),
                StyleClasses = { StyleClass.LabelSubText },
            });
        }
        else
        {
            for (var i = 0; i < vendor.OrderList.Count; i++)
            {
                var order = vendor.OrderList[i];
                var index = i;
                var name = _proto.TryIndex(order.Id, out var entity) ? entity.Name : order.Name;

                var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
                row.AddChild(new Label
                {
                    Text = name ?? order.Id,
                    HorizontalExpand = true,
                    VerticalAlignment = Control.VAlignment.Center,
                });

                var cancel = new Button
                {
                    Text = "×",
                    ToolTip = Loc.GetString("mining-shop-cancel-order"),
                    MinSize = new Vector2(24, 24),
                };
                cancel.OnPressed += _ => SendMessage(new MiningShopCancelOrderBuiMsg(index));
                row.AddChild(cancel);

                _window.OrderList.AddChild(row);
            }
        }

        foreach (var (_, _, _, items) in _blocks)
        {
            foreach (var (card, price) in items)
            {
                var disabled = price != null && userPoints < price;
                card.SetDisabled(disabled);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        switch (message)
        {
            case MiningShopRefreshBuiMsg:
                Refresh();
                break;
        }
    }

    private static string GetSectionName(MiningShopSectionPrototype section)
    {
        var localized = Loc.GetString(section.LocId);
        if (!string.IsNullOrEmpty(localized))
            return localized;

        return section.Name ?? section.ID;
    }
}

