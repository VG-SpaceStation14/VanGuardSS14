using System.Linq;
using System.Numerics;
using Content.Client.Lobby.UI.Loadouts;
using Content.Client.Lobby.UI.Roles;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Client.Graphics;
using Robust.Shared.Utility;
using Robust.Client.UserInterface;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

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

    // VG-Tweak Start: antagonist pictures are kept from the VanGuard build.
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

    /// <summary>
    /// Refreshes all job selectors.
    /// </summary>
    public void RefreshJobs()
    {
        JobList.RemoveAllChildren();
        _jobCategories.Clear();
        _jobPriorities.Clear();
        var firstCategory = true;

        // Get all displayed departments
        var departments = new List<DepartmentPrototype>();
        foreach (var department in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (department.EditorHidden)
                continue;

            departments.Add(department);
        }

        departments.Sort(DepartmentUIComparer.Instance);

        var items = new[]
        {
                ("humanoid-profile-editor-job-priority-never-button", (int) JobPriority.Never),
                ("humanoid-profile-editor-job-priority-low-button", (int) JobPriority.Low),
                ("humanoid-profile-editor-job-priority-medium-button", (int) JobPriority.Medium),
                ("humanoid-profile-editor-job-priority-high-button", (int) JobPriority.High),
            };

        foreach (var department in departments)
        {
            var departmentName = Loc.GetString(department.Name);

            if (!_jobCategories.TryGetValue(department.ID, out var category))
            {
                category = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    Name = department.ID,
                    ToolTip = Loc.GetString("humanoid-profile-editor-jobs-amount-in-department-tooltip",
                        ("departmentName", departmentName))
                };

                if (firstCategory)
                {
                    firstCategory = false;
                }
                else
                {
                    category.AddChild(new Control
                    {
                        MinSize = new Vector2(0, 23),
                    });
                }

                // VG-Tweak Start: department header and panel in the VanGuard style.
                category.AddChild(new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = Color.FromHex("#10161C"),
                        BorderColor = department.Color.WithAlpha(0.85f),
                        BorderThickness = new Thickness(0, 0, 0, 2),
                        ContentMarginLeftOverride = 8,
                        ContentMarginRightOverride = 8,
                        ContentMarginTopOverride = 4,
                        ContentMarginBottomOverride = 4,
                    },
                    Children =
                        {
                            new Label
                            {
                                Text = Loc.GetString("humanoid-profile-editor-department-jobs-label",
                                    ("departmentName", departmentName)),
                                FontColorOverride = Color.White,
                            }
                        }
                });

                var departmentPanel = new PanelContainer
                {
                    HorizontalExpand = true,
                    Margin = new Thickness(0, 0, 0, 10),
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = department.Color.WithAlpha(0.07f),
                        BorderColor = department.Color.WithAlpha(0.45f),
                        BorderThickness = new Thickness(1),
                        ContentMarginLeftOverride = 6,
                        ContentMarginRightOverride = 6,
                        ContentMarginTopOverride = 6,
                        ContentMarginBottomOverride = 6,
                    }
                };
                departmentPanel.AddChild(category);

                _jobCategories[department.ID] = category;
                JobList.AddChild(departmentPanel);
                // VG-Tweak End
            }

            var jobs = department.Roles.Select(jobId => _prototypeManager.Index(jobId))
                .Where(job => job.SetPreference)
                .ToArray();

            if (JobUIComparer.TryCreate(_prototypeManager, null, out var comparer))
                Array.Sort(jobs, comparer);

            foreach (var job in jobs)
            {
                // VG-Tweak Start: compact row panel with the same RequirementsSelector as upstream.
                var jobContainer = new BoxContainer()
                {
                    Orientation = LayoutOrientation.Horizontal,
                    SeparationOverride = 4,
                };

                var selector = new RequirementsSelector()
                {
                    Margin = new Thickness(3f, 3f, 3f, 0f),
                };
                selector.OnOpenGuidebook += OnOpenGuidebook;

                var icon = new TextureRect
                {
                    TextureScale = new Vector2(2, 2),
                    VerticalAlignment = VAlignment.Center
                };
                var jobIcon = _prototypeManager.Index(job.Icon);
                icon.Texture = _sprite.Frame0(jobIcon.Icon);
                selector.Setup(items, job.LocalizedName, 200, job.LocalizedDescription, icon, job.Guides);

                if (!_requirements.IsAllowed(job, (HumanoidCharacterProfile?)_preferencesManager.Preferences?.SelectedCharacter, out var reason))
                {
                    selector.LockRequirements(reason);
                }
                else
                {
                    selector.UnlockRequirements();
                }

                selector.OnSelected += selectedPrio =>
                {
                    var selectedJobPrio = (JobPriority)selectedPrio;
                    Profile = Profile?.WithJobPriority(job.ID, selectedJobPrio);

                    foreach (var (jobId, other) in _jobPriorities)
                    {
                        // Sync other selectors with the same job in case of multiple department jobs
                        if (jobId == job.ID)
                        {
                            other.Select(selectedPrio);
                            continue;
                        }

                        if (selectedJobPrio != JobPriority.High || (JobPriority)other.Selected != JobPriority.High)
                            continue;

                        // Lower any other high priorities to medium.
                        other.Select((int)JobPriority.Medium);
                        Profile = Profile?.WithJobPriority(jobId, JobPriority.Medium);
                    }

                    // TODO: Only reload on high change (either to or from).
                    ReloadPreview();

                    UpdateJobPriorities();
                    SetDirty();
                };

                // VG-Tweak Start: gear icon instead of the upstream text button.
                var loadoutWindowBtn = new TextureButton()
                {
                    SetSize = new Vector2(24, 24),
                    HorizontalAlignment = HAlignment.Right,
                    VerticalAlignment = VAlignment.Center,
                    Margin = new Thickness(3f, 3f, 0f, 0f),
                    ToolTip = Loc.GetString("lobby-character-preview-panel-character-setup-button"),
                };

                var resourceCache = IoCManager.Resolve<IResourceCache>();
                if (resourceCache.TryGetResource<TextureResource>("/Textures/Interface/VerbIcons/settings.svg.192dpi.png", out var gearTexture))
                    loadoutWindowBtn.TextureNormal = gearTexture;
                // VG-Tweak End

                var collection = IoCManager.Instance!;
                var protoManager = collection.Resolve<IPrototypeManager>();

                // If no loadout found then disabled button
                if (!protoManager.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID), out var roleLoadoutProto))
                {
                    loadoutWindowBtn.Disabled = true;
                }
                // else
                else
                {
                    loadoutWindowBtn.OnPressed += args =>
                    {
                        RoleLoadout? loadout = null;

                        // Clone so we don't modify the underlying loadout.
                        Profile?.Loadouts.TryGetValue(LoadoutSystem.GetJobPrototype(job.ID), out loadout);
                        loadout = loadout?.Clone();

                        if (loadout == null)
                        {
                            loadout = new RoleLoadout(roleLoadoutProto.ID);
                            loadout.SetDefault(Profile, _playerManager.LocalSession, _prototypeManager);
                        }

                        OpenLoadout(job, loadout, roleLoadoutProto);
                    };
                }

                _jobPriorities.Add((job.ID, selector));
                jobContainer.AddChild(selector);
                jobContainer.AddChild(loadoutWindowBtn);

                category.AddChild(new PanelContainer
                {
                    HorizontalExpand = true,
                    Margin = new Thickness(0, 0, 0, 2),
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = Color.FromHex("#0C1116"),
                        BorderColor = Color.FromHex("#26323B"),
                        BorderThickness = new Thickness(1),
                        ContentMarginLeftOverride = 4,
                        ContentMarginRightOverride = 4,
                        ContentMarginTopOverride = 2,
                        ContentMarginBottomOverride = 2,
                    },
                    Children = { jobContainer }
                });
                // VG-Tweak End
            }
        }

        UpdateJobPriorities();
    }

    // VG-Tweak Start: upstream antag preference logic with the VanGuard antagonist pictures.
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

            // Upstream behaviour: unavailable antagonists are removed from the profile.
            if (!isAvailable && isEnabled)
            {
                Profile = Profile?.WithAntagPreference(antag.ID, false);
                SetDirty();
                isEnabled = false;
            }

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
                ToolTip = !isAvailable ? (reason?.ToString() ?? Loc.GetString("generic-requirements-not-met")) : Loc.GetString(antag.Objective),
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
