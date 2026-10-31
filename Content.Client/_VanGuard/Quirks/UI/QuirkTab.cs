using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._VanGuard.Quirks.UI;

/// <summary>
///     Character quirk editor: searchable, compact category panels with point budgets, add/remove buttons,
///     descriptions and explicit reasons why a quirk can not be taken right now.
///     The tab is updated in place, so adding or removing a quirk never resets the scroll position or the
///     collapsed state of the categories.
/// </summary>
public sealed class QuirkTab : BoxContainer
{
    private const string DefaultCategory = TraitCategoryPrototype.Default;

    private sealed class TraitRow
    {
        public TraitPrototype Trait = default!;
        public PanelContainer Panel = default!;
        public Label NameLabel = default!;
        public Label CostLabel = default!;
        public Label ReasonLabel = default!;
        public Button Button = default!;
        public string SearchText = string.Empty;
    }

    private sealed class CategoryView
    {
        public string Id = string.Empty;
        public TraitCategoryPrototype? Category;
        public PanelContainer Panel = default!;
        public BoxContainer Body = default!;
        public Button Toggle = default!;
        public Label Counters = default!;
        public List<TraitRow> Rows = new();
    }

    private readonly IPrototypeManager _prototypes;
    private readonly ILocalizationManager _loc;
    private readonly List<CategoryView> _categories = new();
    private readonly Dictionary<string, bool> _expanded = new();

    private HumanoidCharacterProfile? _profile;
    private Action<HumanoidCharacterProfile>? _profileChanged;
    private LineEdit? _search;
    private Label? _summary;
    private string _searchText = string.Empty;

    /// <summary>
    ///     Builds the quirk tab into the given host, or updates the already built tab in place.
    /// </summary>
    public static void BuildInto(BoxContainer host,
        HumanoidCharacterProfile? profile,
        Action<HumanoidCharacterProfile> profileChanged)
    {
        if (profile == null)
        {
            host.RemoveAllChildren();
            return;
        }

        if (host.ChildCount == 1 && host.GetChild(0) is QuirkTab existing)
        {
            existing.Update(profile, profileChanged);
            return;
        }

        host.RemoveAllChildren();
        host.AddChild(new QuirkTab(profile, profileChanged));
    }

    private QuirkTab(HumanoidCharacterProfile profile, Action<HumanoidCharacterProfile> profileChanged)
    {
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _loc = IoCManager.Resolve<ILocalizationManager>();

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;

        BuildLayout();
        Update(profile, profileChanged);
    }

    private void BuildLayout()
    {
        var header = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _search = new LineEdit
        {
            PlaceHolder = _loc.GetString("quirk-search-placeholder"),
            HorizontalExpand = true,
        };
        _search.OnTextChanged += args =>
        {
            _searchText = args.Text.Trim();
            ApplyFilter();
        };
        header.AddChild(_search);

        _summary = new Label
        {
            StyleClasses = { "LabelSubText" },
            VerticalAlignment = VAlignment.Center,
        };
        header.AddChild(_summary);

        AddChild(header);

        var groups = new Dictionary<string, List<TraitPrototype>>();
        foreach (var trait in _prototypes.EnumeratePrototypes<TraitPrototype>())
            groups.GetOrNew(trait.Category?.Id ?? DefaultCategory).Add(trait);

        foreach (var (categoryId, traits) in groups
                     .OrderBy(g => CategoryPriority(g.Key))
                     .ThenBy(g => CategoryName(g.Key), StringComparer.CurrentCultureIgnoreCase))
        {
            _prototypes.TryIndex(categoryId, out TraitCategoryPrototype? category);
            var view = BuildCategory(categoryId, category, traits);
            _categories.Add(view);
            AddChild(view.Panel);
        }
    }

    private CategoryView BuildCategory(string categoryId,
        TraitCategoryPrototype? category,
        IReadOnlyList<TraitPrototype> traits)
    {
        var accent = category?.AccentColor ?? Color.FromHex("#9AA5B1");

        var view = new CategoryView
        {
            Id = categoryId,
            Category = category,
        };

        view.Panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 8),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#14181D"),
                BorderColor = Color.FromHex("#2A343D"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4,
            }
        };

        var content = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        view.Panel.AddChild(content);

        // Category header: a compact row with a thin accent line, not a bright colored plate.
        var headerRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };

        view.Toggle = new Button
        {
            Text = "▾",
            MinWidth = 26,
            MinHeight = 24,
            ToolTip = _loc.GetString("quirk-category-collapse"),
        };
        headerRow.AddChild(view.Toggle);

        headerRow.AddChild(new Label
        {
            Text = CategoryName(categoryId),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        });

        view.Counters = new Label
        {
            StyleClasses = { "LabelSubText" },
            VerticalAlignment = VAlignment.Center,
        };
        headerRow.AddChild(view.Counters);

        var headerPanel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#101418"),
                BorderColor = accent.WithAlpha(0.45f),
                BorderThickness = new Thickness(0, 0, 0, 2),
                ContentMarginLeftOverride = 4,
                ContentMarginRightOverride = 4,
                ContentMarginTopOverride = 2,
                ContentMarginBottomOverride = 2,
            }
        };
        headerPanel.AddChild(headerRow);
        content.AddChild(headerPanel);

        view.Body = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true,
            Visible = _expanded.GetValueOrDefault(categoryId, true),
        };
        content.AddChild(view.Body);

        view.Toggle.OnPressed += _ =>
        {
            var expanded = !view.Body.Visible;
            view.Body.Visible = expanded;
            _expanded[categoryId] = expanded;
            view.Toggle.Text = expanded ? "▾" : "▸";
            view.Toggle.ToolTip = _loc.GetString(expanded ? "quirk-category-collapse" : "quirk-category-expand");
        };

        foreach (var trait in traits.OrderBy(t => _loc.GetString(t.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            var row = BuildTraitRow(trait);
            view.Rows.Add(row);
            view.Body.AddChild(row.Panel);
        }

        return view;
    }

    private TraitRow BuildTraitRow(TraitPrototype trait)
    {
        var row = new TraitRow
        {
            Trait = trait,
            SearchText = trait.Description == null
                ? $"{_loc.GetString(trait.Name)} {trait.ID}".ToLowerInvariant()
                : $"{_loc.GetString(trait.Name)} {_loc.GetString(trait.Description.Value)} {trait.ID}".ToLowerInvariant(),
        };

        row.Panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = RowStyle(false),
        };

        var content = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true,
        };

        var top = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };

        row.NameLabel = new Label
        {
            Text = _loc.GetString(trait.Name),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            ToolTip = trait.Description == null ? null : _loc.GetString(trait.Description),
        };
        top.AddChild(row.NameLabel);

        row.CostLabel = new Label
        {
            StyleClasses = { "LabelSubText" },
            VerticalAlignment = VAlignment.Center,
        };
        top.AddChild(row.CostLabel);

        row.Button = new Button
        {
            MinWidth = 90,
            MinHeight = 26,
            VerticalAlignment = VAlignment.Center,
        };
        row.Button.OnPressed += _ => ToggleTrait(trait);
        top.AddChild(row.Button);

        content.AddChild(top);

        if (trait.Description != null)
        {
            var description = new RichTextLabel { HorizontalExpand = true };
            description.SetMessage(FormattedMessage.FromUnformatted(_loc.GetString(trait.Description.Value)));
            content.AddChild(description);
        }

        row.ReasonLabel = new Label
        {
            StyleClasses = { "LabelSubText" },
            Visible = false,
        };
        content.AddChild(row.ReasonLabel);

        row.Panel.AddChild(content);
        return row;
    }

    private void ToggleTrait(TraitPrototype trait)
    {
        if (_profile == null || _profileChanged == null)
            return;

        var selected = _profile.TraitPreferences.Contains(trait.ID);
        var profile = selected
            ? _profile.WithoutTraitPreference(trait.ID, _prototypes)
            : _profile.WithTraitPreference(trait.ID, _prototypes);

        _profileChanged(profile);
    }

    private void Update(HumanoidCharacterProfile profile, Action<HumanoidCharacterProfile> profileChanged)
    {
        _profile = profile;
        _profileChanged = profileChanged;

        if (_summary != null)
            _summary.Text = _loc.GetString("quirk-selected-count", ("count", profile.TraitPreferences.Count));

        foreach (var category in _categories)
        {
            var points = 0;
            var selectedCount = 0;

            foreach (var row in category.Rows)
            {
                var selected = profile.TraitPreferences.Contains(row.Trait.ID);
                var reason = selected ? null : GetUnavailableReason(row.Trait, category.Category);

                if (selected)
                {
                    selectedCount++;
                    points += row.Trait.Cost;
                }

                row.Panel.PanelOverride = RowStyle(selected);
                row.NameLabel.FontColorOverride = reason != null ? Color.FromHex("#8C949C") : Color.White;

                row.CostLabel.Text = row.Trait.Cost switch
                {
                    > 0 => _loc.GetString("quirk-cost-hint", ("cost", row.Trait.Cost)),
                    < 0 => _loc.GetString("quirk-refund-hint", ("cost", -row.Trait.Cost)),
                    _ => string.Empty,
                };

                row.Button.Text = _loc.GetString(selected ? "quirk-button-remove" : "quirk-button-add");
                row.Button.Disabled = !selected && reason != null;
                row.Button.ToolTip = reason;
                row.ReasonLabel.Text = reason ?? string.Empty;
                row.ReasonLabel.Visible = reason != null;
            }

            var counters = category.Category is { MaxTraitPoints: >= 0 }
                ? _loc.GetString("quirk-category-points",
                    ("spent", points),
                    ("max", category.Category.MaxTraitPoints))
                : string.Empty;

            var selectedText = _loc.GetString("quirk-category-selected", ("count", selectedCount));
            category.Counters.Text = counters.Length == 0 ? selectedText : $"{counters} · {selectedText}";
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var search = _searchText.ToLowerInvariant();

        foreach (var category in _categories)
        {
            var anyVisible = false;
            foreach (var row in category.Rows)
            {
                var visible = search.Length == 0 || row.SearchText.Contains(search);
                row.Panel.Visible = visible;
                anyVisible |= visible;
            }

            category.Panel.Visible = anyVisible;
        }
    }

    private string? GetUnavailableReason(TraitPrototype trait, TraitCategoryPrototype? category)
    {
        if (_profile == null)
            return null;

        if (!_profile.IsTraitAllowedForSpecies(trait))
            return _loc.GetString("quirk-reason-species");

        if (_profile.HasConflictingTrait(trait, _prototypes))
        {
            var conflicting = FindConflictingTrait(trait);
            return conflicting != null
                ? _loc.GetString("quirk-reason-conflict", ("trait", _loc.GetString(conflicting.Name)))
                : _loc.GetString("quirk-reason-conflict-generic");
        }

        if (category is { MaxTraitPoints: >= 0 } && trait.Cost != 0)
        {
            var points = 0;
            foreach (var selectedId in _profile.TraitPreferences)
            {
                if (_prototypes.TryIndex(selectedId, out TraitPrototype? selected) && selected.Category == category.ID)
                    points += selected.Cost;
            }

            if (points + trait.Cost > category.MaxTraitPoints)
                return _loc.GetString("quirk-reason-points");
        }

        return null;
    }

    private TraitPrototype? FindConflictingTrait(TraitPrototype trait)
    {
        if (_profile == null)
            return null;

        foreach (var selectedId in _profile.TraitPreferences)
        {
            if (selectedId == trait.ID ||
                !_prototypes.TryIndex(selectedId, out TraitPrototype? selected))
            {
                continue;
            }

            if (selected.Conflicts.Contains(trait.ID) || trait.Conflicts.Contains(selected.ID))
                return selected;
        }

        return null;
    }

    private static StyleBoxFlat RowStyle(bool selected)
    {
        return selected
            ? new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#151B21"),
                BorderColor = Color.FromHex("#C9A227").WithAlpha(0.7f),
                BorderThickness = new Thickness(2, 0, 0, 0),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 4,
                ContentMarginTopOverride = 3,
                ContentMarginBottomOverride = 3,
            }
            : new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#0C1116"),
                BorderColor = Color.FromHex("#212A31"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 4,
                ContentMarginTopOverride = 3,
                ContentMarginBottomOverride = 3,
            };
    }

    private int CategoryPriority(string categoryId)
    {
        if (categoryId == DefaultCategory)
            return int.MaxValue;

        return _prototypes.TryIndex(categoryId, out TraitCategoryPrototype? category)
            ? category.Priority
            : int.MaxValue - 1;
    }

    private string CategoryName(string categoryId) =>
        categoryId == DefaultCategory ||
        !_prototypes.TryIndex(categoryId, out TraitCategoryPrototype? category)
            ? _loc.GetString("quirk-category-default")
            : _loc.GetString(category.Name);
}
