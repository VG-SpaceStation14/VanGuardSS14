using System.Linq;
using System.Numerics;
using Content.Client.Lobby.UI.Loadouts;
using Content.Client.Lobby.UI.Roles;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Guidebook;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Client.ResourceManagement;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{

    /// <summary>
    /// Temporary override of their selected job, used to preview roles.
    /// </summary>
    public JobPrototype? JobOverride;

    // One at a time.
    private LoadoutWindow? _loadoutWindow;

    private List<(string, RequirementsSelector)> _jobPriorities = new();

    private readonly Dictionary<string, BoxContainer> _jobCategories;

    // VG-Tweak Start
    private Dictionary<string, OptionButton> _jobPriorityButtons = new();
    private Dictionary<string, TextureButton> _jobLoadoutButtons = new();
    private Dictionary<string, TextureRect> _jobPriorityIndicators = new();

    private const string AntagIconPath = "/Textures/_VanGuard/Interface/Antagonists/antag_icons.rsi";
    // VG-Tweak End

    /// <summary>
    /// Updates selected job priorities to the profile's.
    /// </summary>
    private void UpdateJobPriorities()
    {
        foreach (var (jobId, prioritySelector) in _jobPriorities)
        {
            var priority = Profile?.JobPriorities.GetValueOrDefault(jobId, JobPriority.Never) ?? JobPriority.Never;
            prioritySelector.Select((int)priority);
        }
    }

    /// <summary>
    /// Refresh all loadouts.
    /// </summary>
    public void RefreshLoadouts()
    {
        _loadoutWindow?.Dispose();
    }

    private void OpenLoadout(JobPrototype? jobProto, RoleLoadout roleLoadout, RoleLoadoutPrototype roleLoadoutProto)
    {
        _loadoutWindow?.Dispose();
        _loadoutWindow = null;
        var collection = IoCManager.Instance;

        if (collection == null || _playerManager.LocalSession == null || Profile == null)
            return;

        JobOverride = jobProto;
        var session = _playerManager.LocalSession;

        _loadoutWindow = new LoadoutWindow(Profile, roleLoadout, roleLoadoutProto, _playerManager.LocalSession, collection)
        {
            Title = Loc.GetString("loadout-window-title-loadout", ("job", $"{jobProto?.LocalizedName}")),
        };

        // Refresh the buttons etc.
        _loadoutWindow.RefreshLoadouts(roleLoadout, session, collection);
        _loadoutWindow.OpenCenteredLeft();

        _loadoutWindow.OnNameChanged += name =>
        {
            roleLoadout.EntityName = name;
            Profile = Profile.WithLoadout(roleLoadout);
            SetDirty();
        };

        _loadoutWindow.OnLoadoutPressed += (loadoutGroup, loadoutProto) =>
        {
            roleLoadout.AddLoadout(loadoutGroup, loadoutProto, _prototypeManager);
            _loadoutWindow.RefreshLoadouts(roleLoadout, session, collection);
            Profile = Profile?.WithLoadout(roleLoadout);
            ReloadPreview();
        };

        _loadoutWindow.OnLoadoutUnpressed += (loadoutGroup, loadoutProto) =>
        {
            roleLoadout.RemoveLoadout(loadoutGroup, loadoutProto, _prototypeManager);
            _loadoutWindow.RefreshLoadouts(roleLoadout, session, collection);
            Profile = Profile?.WithLoadout(roleLoadout);
            ReloadPreview();
        };

        JobOverride = jobProto;
        ReloadPreview();

        _loadoutWindow.OnClose += () =>
        {
            JobOverride = null;
            ReloadPreview();
        };

        if (Profile is null)
            return;

        UpdateJobPriorities();
    }

    // VG-Tweak Start
    public void RefreshJobs()
    {
        LeftJobsColumn.RemoveAllChildren();
        RightJobsColumn.RemoveAllChildren();
        _jobPriorityButtons.Clear();
        _jobLoadoutButtons.Clear();
        _jobPriorityIndicators.Clear();

        var departments = new List<DepartmentPrototype>();
        foreach (var department in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (department.EditorHidden) continue;
            departments.Add(department);
        }
        departments.Sort(DepartmentUIComparer.Instance);

        if (departments.Count == 0) return;

        var priorityItems = new[]
        {
            (Loc.GetString("humanoid-profile-editor-job-priority-never-button"), JobPriority.Never),
            (Loc.GetString("humanoid-profile-editor-job-priority-low-button"), JobPriority.Low),
            (Loc.GetString("humanoid-profile-editor-job-priority-medium-button"), JobPriority.Medium),
            (Loc.GetString("humanoid-profile-editor-job-priority-high-button"), JobPriority.High),
        };

        bool addToLeft = true;
        var resourceCache = IoCManager.Resolve<IResourceCache>();
        var dotTexture = resourceCache.GetResource<TextureResource>("/Textures/Interface/VerbIcons/dot.svg.192dpi.png");

        foreach (var department in departments)
        {
            var jobs = department.Roles
                .Select(jobId => _prototypeManager.Index<JobPrototype>(jobId))
                .Where(job => job.SetPreference)
                .ToArray();

            if (JobUIComparer.TryCreate(_prototypeManager, null, out var comparer))
                Array.Sort(jobs, comparer);

            if (jobs.Length == 0) continue;

            var departmentColor = department.Color;

            var departmentPanel = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = departmentColor.WithAlpha(0.15f),
                    BorderColor = departmentColor,
                    BorderThickness = new Thickness(1)
                },
                Margin = new Thickness(0, 0, 0, 10)
            };

            var categoryContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Margin = new Thickness(8)
            };

            var darkenedColor = new Color(
                departmentColor.R * 0.8f,
                departmentColor.G * 0.8f,
                departmentColor.B * 0.8f,
                departmentColor.A
            );
            var header = new PanelContainer
            {
            PanelOverride = new StyleBoxFlat { BackgroundColor = darkenedColor },
                Children =
                {
                    new Label
                    {
                        Text = Loc.GetString(department.Name),
                        Margin = new Thickness(8, 4, 8, 4),
                        FontColorOverride = Color.White,
                        HorizontalAlignment = HAlignment.Center
                    }
                }
            };
            categoryContainer.AddChild(header);

            foreach (var job in jobs)
            {
                var row = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    SeparationOverride = 6,
                    Margin = new Thickness(3, 3, 3, 0)
                };

                // Job icon
                var icon = new TextureRect
                {
                    TextureScale = new Vector2(2, 2),
                    VerticalAlignment = VAlignment.Center,
                    SetSize = new Vector2(16, 16)
                };
                if (_prototypeManager.TryIndex(job.Icon, out var iconProto))
                    icon.Texture = _sprite.Frame0(iconProto.Icon);
                row.AddChild(icon);

                // Job name
                var nameLabel = new Label
                {
                    Text = job.LocalizedName,
                    VerticalAlignment = VAlignment.Center,
                    HorizontalExpand = true,
                    MouseFilter = MouseFilterMode.Stop
                };
                if (!string.IsNullOrWhiteSpace(job.LocalizedDescription))
                    nameLabel.ToolTip = job.LocalizedDescription;
                else
                    nameLabel.ToolTip = Loc.GetString("humanoid-profile-editor-no-description");
                row.AddChild(nameLabel);

                // Color priority indicator (dot)
                var priorityIndicator = new TextureRect
                {
                    Texture = dotTexture,
                    TextureScale = new Vector2(0.75f, 0.75f),
                    VerticalAlignment = VAlignment.Center,
                    HorizontalAlignment = HAlignment.Left,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                var currentPriority = Profile?.JobPriorities.GetValueOrDefault(job.ID, JobPriority.Never) ?? JobPriority.Never;
                UpdatePriorityIndicatorColor(priorityIndicator, currentPriority);
                row.AddChild(priorityIndicator);
                _jobPriorityIndicators[job.ID] = priorityIndicator;

                // Priority dropdown
                var priorityDropdown = new OptionButton
                {
                    MinWidth = 120,
                    VerticalAlignment = VAlignment.Center
                };
                foreach (var (text, prio) in priorityItems)
                    priorityDropdown.AddItem(text, (int)prio);

                priorityDropdown.SelectId((int)currentPriority);

                bool isAllowed = _requirements.IsAllowed(job, Profile, out var reason);
                if (!isAllowed)
                {
                    priorityDropdown.Disabled = true;
                    priorityDropdown.ToolTip = reason?.ToString() ?? Loc.GetString("generic-requirements-not-met");
                    priorityDropdown.SelectId((int)JobPriority.Never);
                    UpdatePriorityIndicatorColor(priorityIndicator, JobPriority.Never);
                }

                priorityDropdown.OnItemSelected += args =>
                {
                    var newPriority = (JobPriority)args.Id;
                    priorityDropdown.SelectId(args.Id);
                    UpdatePriorityIndicatorColor(priorityIndicator, newPriority);

                    if (newPriority == JobPriority.High)
                    {
                        foreach (var (otherJobId, otherDropdown) in _jobPriorityButtons)
                        {
                            if (otherJobId == job.ID) continue;
                            if ((JobPriority)otherDropdown.SelectedId == JobPriority.High)
                            {
                                otherDropdown.SelectId((int)JobPriority.Medium);
                                UpdatePriorityIndicatorColor(_jobPriorityIndicators[otherJobId], JobPriority.Medium);
                                Profile = Profile?.WithJobPriority(otherJobId, JobPriority.Medium);
                            }
                        }
                    }

                    Profile = Profile?.WithJobPriority(job.ID, newPriority);
                    SetDirty();

                    if (newPriority == JobPriority.High ||
                        (currentPriority == JobPriority.High && newPriority != JobPriority.High))
                        ReloadPreview();
                    else
                        ReloadProfilePreview();
                };

                row.AddChild(priorityDropdown);
                _jobPriorityButtons[job.ID] = priorityDropdown;

                // Loadout button
                var loadoutButton = new TextureButton
                {
                    SetSize = new Vector2(24, 24),
                    VerticalAlignment = VAlignment.Center,
                    ToolTip = Loc.GetString("lobby-character-preview-panel-character-setup-button")
                };
                if (resourceCache.TryGetResource<TextureResource>("/Textures/Interface/VerbIcons/settings.svg.192dpi.png", out var gearTexture))
                    loadoutButton.TextureNormal = gearTexture;

                var loadoutProtoId = LoadoutSystem.GetJobPrototype(job.ID);
                bool hasLoadout = _prototypeManager.HasIndex<RoleLoadoutPrototype>(loadoutProtoId);
                loadoutButton.Disabled = !hasLoadout || !isAllowed || Profile == null;

                if (hasLoadout && Profile != null)
                {
                    loadoutButton.OnPressed += _ =>
                    {
                        RoleLoadout? loadout = null;
                        Profile?.Loadouts.TryGetValue(loadoutProtoId, out loadout);
                        loadout = loadout?.Clone();
                        if (loadout == null)
                        {
                            loadout = new RoleLoadout(loadoutProtoId);
                            loadout.SetDefault(Profile, _playerManager.LocalSession, _prototypeManager);
                        }

                        OpenLoadout(job, loadout, _prototypeManager.Index<RoleLoadoutPrototype>(loadoutProtoId));
                    };
                }

                row.AddChild(loadoutButton);
                _jobLoadoutButtons[job.ID] = loadoutButton;

                categoryContainer.AddChild(row);
            }

            departmentPanel.AddChild(categoryContainer);

            if (addToLeft)
                LeftJobsColumn.AddChild(departmentPanel);
            else
                RightJobsColumn.AddChild(departmentPanel);

            addToLeft = !addToLeft;
        }
    }

    private void UpdatePriorityIndicatorColor(TextureRect indicator, JobPriority priority)
    {
        Color color = priority switch
        {
            JobPriority.High => Color.LimeGreen,
            JobPriority.Medium => Color.Gold,
            JobPriority.Low => Color.Orange,
            JobPriority.Never => Color.Gray,
            _ => Color.White
        };
        indicator.ModulateSelfOverride = color;
    }
    // VG-Tweak End

    // VG-Tweak Start
    public void RefreshAntags()
    {
        AntagGrid.RemoveAllChildren();

        var antags = _prototypeManager.EnumeratePrototypes<AntagPrototype>()
            .Where(a => a.SetPreference)
            .OrderBy(a => Loc.GetString(a.Name))
            .ToList();

        if (antags.Count == 0)
        {
            AntagGrid.AddChild(new Label
            {
                Text = Loc.GetString("humanoid-profile-editor-no-antags"),
                FontColorOverride = Color.Gray,
                HorizontalAlignment = HAlignment.Center
            });
            return;
        }

        const int columns = 6;
        BoxContainer? row = null;
        var count = 0;
        var cache = IoCManager.Resolve<IResourceCache>();

        foreach (var antag in antags)
        {
            if (count % columns == 0)
            {
                row = new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    SeparationOverride = 20,
                    HorizontalAlignment = HAlignment.Center
                };
                AntagGrid.AddChild(row);
            }

            bool isAvailable = _requirements.IsAllowed(antag,
                (HumanoidCharacterProfile?)_preferencesManager.Preferences?.SelectedCharacter, out var reason);
            bool isEnabled = Profile?.AntagPreferences.Contains(antag.ID) == true;

            string state;
            if (!isAvailable)
                state = $"{antag.ID}-disable";
            else if (isEnabled)
                state = $"{antag.ID}-on";
            else
                state = $"{antag.ID}-off";

            var container = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                SeparationOverride = 6,
                HorizontalAlignment = HAlignment.Center,
                MinWidth = 150
            };

            var buttonParent = new Control
            {
                SetSize = new Vector2(100, 100),
                HorizontalAlignment = HAlignment.Center,
            };

            var button = new TextureButton
            {
                HorizontalAlignment = HAlignment.Stretch,
                VerticalAlignment = VAlignment.Stretch,
                ToolTip = !isAvailable ? (reason?.ToString() ?? "") : Loc.GetString(antag.Objective),
                Disabled = !isAvailable
            };

            if (cache.TryGetResource<RSIResource>(new ResPath(AntagIconPath), out var rsi))
            {
                RSI.State? rsiState = null;
                if (!rsi.RSI.TryGetState(state, out rsiState))
                    rsi.RSI.TryGetState($"{antag.ID}-off", out rsiState);
                if (rsiState != null)
                    button.TextureNormal = rsiState.Frame0;
            }

            button.OnPressed += _ =>
            {
                if (!isAvailable) return;
                bool newState = !(Profile?.AntagPreferences.Contains(antag.ID) == true);
                Profile = Profile?.WithAntagPreference(antag.ID, newState);
                SetDirty();
                RefreshAntags();
            };

            buttonParent.AddChild(button);

            if (antag.Guides is { Count: > 0 })
            {
                var guideButton = new TextureButton
                {
                    SetSize = new Vector2(32, 32),
                    HorizontalAlignment = HAlignment.Right,
                    VerticalAlignment = VAlignment.Bottom,
                    TextureNormal = cache.GetResource<TextureResource>("/Textures/Interface/VerbIcons/information.svg.192dpi.png"),
                    ToolTip = Loc.GetString("humanoid-profile-editor-guidebook-button-tooltip"),
                };

                guideButton.OnPressed += _ =>
                {
                    var guides = antag.Guides.ToList();
                    OnOpenGuidebook?.Invoke(guides);
                };

                buttonParent.AddChild(guideButton);
            }

            container.AddChild(buttonParent);

            var label = new RichTextLabel
            {
                HorizontalAlignment = HAlignment.Center,
                MaxWidth = 175
            };
            var msg = new FormattedMessage();
            msg.AddMarkup($"[font size=11][color={(isAvailable ? "white" : "gray")}]{Loc.GetString(antag.Name)}[/color][/font]");
            label.SetMessage(msg);

            container.AddChild(label);
            row!.AddChild(container);
            count++;
        }
    }
    // VG-Tweak End
}