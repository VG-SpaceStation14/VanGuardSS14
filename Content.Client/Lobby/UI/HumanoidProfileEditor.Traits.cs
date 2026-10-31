using System.Linq;
using Content.Client.Lobby.UI.Roles;
using Content.Client.Stylesheets;
using Content.Shared.Traits;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{

    /// <summary>
    /// Refreshes traits selector
    /// </summary>
    public void RefreshTraits()
    {
        TraitsList.RemoveAllChildren();

        var traits = _prototypeManager.EnumeratePrototypes<TraitPrototype>().OrderBy(t => Loc.GetString(t.Name)).ToList();
        // TabContainer.SetTabTitle(3, Loc.GetString("humanoid-profile-editor-traits-tab")); // Corvax-TTS-Edit

        if (traits.Count < 1)
        {
            TraitsList.AddChild(new Label
            {
                Text = Loc.GetString("humanoid-profile-editor-no-traits"),
                FontColorOverride = Color.Gray,
            });
            return;
        }

        // Setup model
        Dictionary<string, List<string>> traitGroups = new();
        List<string> defaultTraits = new();
        traitGroups.Add(TraitCategoryPrototype.Default, defaultTraits);

        foreach (var trait in traits)
        {
            if (trait.Category == null)
            {
                defaultTraits.Add(trait.ID);
                continue;
            }

            if (!_prototypeManager.HasIndex(trait.Category))
                continue;

            var group = traitGroups.GetOrNew(trait.Category);
            group.Add(trait.ID);
        }

        // Create UI view from model
        foreach (var (categoryId, categoryTraits) in traitGroups)
        {
            TraitCategoryPrototype? category = null;

            if (categoryId != TraitCategoryPrototype.Default)
            {
                category = _prototypeManager.Index<TraitCategoryPrototype>(categoryId);
                // Label
                TraitsList.AddChild(new Label
                {
                    Text = Loc.GetString(category.Name),
                    Margin = new Thickness(0, 10, 0, 0),
                    StyleClasses = { StyleClass.LabelHeading },
                });
            }

            // VG-Tweak Start: keep the trait next to its selector so availability can be explained per trait.
            List<(TraitPrototype Trait, TraitPreferenceSelector Selector)> selectors = new();
            // VG-Tweak End
            var selectionCount = 0;

            foreach (var traitProto in categoryTraits)
            {
                var trait = _prototypeManager.Index<TraitPrototype>(traitProto);
                var selector = new TraitPreferenceSelector(trait);

                selector.Preference = Profile?.TraitPreferences.Contains(trait.ID) == true;
                if (selector.Preference)
                    selectionCount += trait.Cost;

                selector.PreferenceChanged += preference =>
                {
                    if (preference)
                    {
                        Profile = Profile?.WithTraitPreference(trait.ID, _prototypeManager);
                    }
                    else
                    {
                        Profile = Profile?.WithoutTraitPreference(trait.ID, _prototypeManager);
                    }

                    SetDirty();
                    RefreshTraits(); // If too many traits are selected, they will be reset to the real value.
                };
                // VG-Tweak Start: store both the trait and its selector.
                selectors.Add((trait, selector));
                // VG-Tweak End
            }

            // Selection counter
            if (category is { MaxTraitPoints: >= 0 })
            {
                TraitsList.AddChild(new Label
                {
                    Text = Loc.GetString("humanoid-profile-editor-trait-count-hint", ("current", selectionCount), ("max", category.MaxTraitPoints)),
                    FontColorOverride = Color.Gray
                });
            }

            // VG-Tweak Start: explain why a trait can not be picked right now (conflict, species, point limit).
            foreach (var (trait, selector) in selectors)
            {
                var selected = Profile?.TraitPreferences.Contains(trait.ID) == true;

                if (!selected)
                {
                    var reason = GetTraitUnavailableReason(trait, category, selectionCount);
                    if (reason != null)
                        selector.SetAvailability(false, reason);
                }

                if (!selected &&
                    category is { MaxTraitPoints: >= 0 } &&
                    selector.Cost + selectionCount > category.MaxTraitPoints)
                {
                    selector.Checkbox.Label.FontColorOverride = Color.Red;
                }

                TraitsList.AddChild(selector);
            }
            // VG-Tweak End
        }
    }

    /// <summary>
    ///     VG-Tweak: returns a localized reason why the given trait can not be selected, or null if it can.
    /// </summary>
    private string? GetTraitUnavailableReason(TraitPrototype trait,
        TraitCategoryPrototype? category,
        int selectionCount)
    {
        var profile = Profile;
        if (profile == null)
            return null;

        if (!profile.IsTraitAllowedForSpecies(trait))
            return Loc.GetString("vg-trait-reason-species");

        var conflicting = FindConflictingTrait(trait);
        if (conflicting != null)
            return Loc.GetString("vg-trait-reason-conflict", ("trait", Loc.GetString(conflicting.Name)));

        if (category is { MaxTraitPoints: >= 0 } &&
            trait.Cost != 0 &&
            selectionCount + trait.Cost > category.MaxTraitPoints)
        {
            return Loc.GetString("vg-trait-reason-points");
        }

        return null;
    }

    /// <summary>
    ///     VG-Tweak: finds an already selected trait that conflicts with the given one.
    /// </summary>
    private TraitPrototype? FindConflictingTrait(TraitPrototype trait)
    {
        var profile = Profile;
        if (profile == null)
            return null;

        foreach (var selectedId in profile.TraitPreferences)
        {
            if (selectedId == trait.ID || !_prototypeManager.TryIndex(selectedId, out var selected))
                continue;

            if (selected.Conflicts.Contains(trait.ID) || trait.Conflicts.Contains(selected.ID))
                return selected;
        }

        return null;
    }

}
