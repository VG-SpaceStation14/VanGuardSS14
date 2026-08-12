using System.Linq;
using System.Numerics;
using Content.Shared._VanGuard.Language;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private string _languagesSearch = string.Empty;

    public void RefreshLanguages()
    {
        if (Profile == null)
            return;

        var species = _prototypeManager.Index(Profile.Species);
        UpdateLanguagesCount(Profile.Languages.Count, species.MaxLanguages);

        var available = new List<LanguagePrototype>();
        available.AddRange(_prototypeManager.EnumeratePrototypes<LanguagePrototype>()
            .Where(x => x.Roundstart));

        foreach (var unique in species.UniqueLanguages)
        {
            if (_prototypeManager.TryIndex(unique, out var proto) && !available.Contains(proto))
                available.Add(proto);
        }

        available.Sort((a, b) =>
        {
            var priority = b.Priority.CompareTo(a.Priority);
            return priority != 0 ? priority : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        // Species-default languages are granted automatically at spawn and cannot be removed.
        var locked = species.DefaultLanguages.ToHashSet();
        var max = species.MaxLanguages;

        LanguagesList.RemoveAllChildren();

        foreach (var proto in available)
        {
            if (_languagesSearch.Length > 0 && !proto.Name.Contains(_languagesSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            var isSelected = Profile.Languages.Contains(proto.ID);
            var isLocked = locked.Contains(proto.ID);
            var atLimit = !isSelected && !isLocked && Profile.Languages.Count >= max;

            LanguagesList.AddChild(BuildLanguageCard(proto, isSelected, isLocked, atLimit));
        }

        if (LanguagesList.ChildCount == 0)
        {
            LanguagesList.AddChild(new Label
            {
                Text = Loc.GetString("humanoid-profile-editor-no-languages"),
                FontColorOverride = Color.Gray,
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }
    }

    private PanelContainer BuildLanguageCard(LanguagePrototype proto, bool isSelected, bool isLocked, bool atLimit)
    {
        var card = new PanelContainer
        {
            StyleClasses = { "PanelInsetDark" },
            Margin = new Thickness(2),
        };

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 4,
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };

        if (proto.Icon is SpriteSpecifier.Rsi rsi)
        {
            var spriteSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SpriteSystem>();
            header.AddChild(new TextureRect
            {
                Texture = spriteSystem.Frame0(rsi),
                TextureScale = new Vector2(2, 2),
                VerticalAlignment = VAlignment.Center,
                MinSize = new Vector2(32, 32),
            });
        }

        var name = new Label
        {
            Text = proto.Name,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        if (proto.UiColor.HasValue)
            name.FontColorOverride = proto.UiColor.Value;
        header.AddChild(name);

        var button = new Button
        {
            Text = isLocked
                ? Loc.GetString("language-lobby-native")
                : isSelected
                    ? Loc.GetString("language-lobby-remove-button")
                    : Loc.GetString("language-lobby-add-button"),
            Disabled = isLocked || atLimit,
            MinWidth = 96,
            HorizontalAlignment = HAlignment.Right,
        };
        header.AddChild(button);
        content.AddChild(header);

        var description = new RichTextLabel
        {
            Text = proto.Description,
            HorizontalExpand = true,
        };
        content.AddChild(description);

        card.AddChild(content);

        var languageId = proto.ID;
        button.OnPressed += _ =>
        {
            if (isSelected)
                Profile = Profile?.WithoutLanguage(languageId);
            else
                Profile = Profile?.WithLanguage(languageId);
            SetDirty();
            RefreshLanguages();
        };

        return card;
    }

    private void UpdateLanguagesCount(int current, int max)
    {
        LanguagesCountLabel.Text = Loc.GetString("humanoid-profile-editor-languages-count",
            ("current", current), ("max", max));
    }
}

