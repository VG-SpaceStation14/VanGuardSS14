using System;
using System.Numerics;
using Content.Shared.Research.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._VanGuard.Research.UI;

/// <summary>
/// A single technology displayed as a node on the research tree.
/// Clicking selects it and shows the info panel; holding the button on a
/// researchable technology fills it green and researches it on completion.
/// It consumes mouse input so dragging the tree can only start from empty space.
/// </summary>
public sealed class ResearchTechnologyNode : ContainerButton
{
    public const float NodeWidth = 160;
    public const float NodeHeight = 170;

    /// <summary>Below this zoom the node collapses to an icon-only chip.</summary>
    private const float TextLodZoom = 0.8f;

    /// <summary>How long (seconds) the button must be held to research the tech.</summary>
    public const float FillDuration = 0.5f;

    /// <summary>Duration of the quick hint fill shown when researching from the info panel.</summary>
    public const float QuickFillDuration = 0.175f;

    /// <summary>How long (seconds) a chain research fill takes (yellow, researches the whole branch).</summary>
    public const float ChainFillDuration = 1.5f;

    /// <summary>Grace period before a hold fill becomes visible, so simple
    /// clicks that just select a technology never flash the fill.</summary>
    public const float FillDelay = 0.15f;

    /// <summary>The technology prototype this node represents.</summary>
    public TechnologyPrototype Proto { get; }

    /// <summary>Fired when the player clicks this node.</summary>
    public event Action<ResearchTechnologyNode>? NodePressed;

    /// <summary>Fired when the hold-to-research fill completes.</summary>
    public event Action<ResearchTechnologyNode>? NodeResearched;

    /// <summary>Fired when the yellow chain-research fill completes.</summary>
    public event Action<ResearchTechnologyNode>? NodeChainResearched;

    /// <summary>Fired when the mouse button is released, cancelling a hold fill.</summary>
    public event Action<ResearchTechnologyNode>? NodeReleased;

    /// <summary>Whether this node can be researched by holding (green fill).</summary>
    public bool Researchable => _researchable;

    /// <summary>Whether holding this node researches its whole branch (yellow fill).</summary>
    public bool ChainResearchable => _chainResearchable;

    private static readonly Color SingleFillColor = new(47, 174, 86, 150);

    /// <summary>Yellow fill used for chain research (node hold and branch button).</summary>
    public static readonly Color ChainFillColor = new(255, 213, 79, 150);

    private readonly Color _stateColor;
    private readonly float _zoom;
    private readonly bool _researchable;
    private readonly bool _chainResearchable;

    private bool _filling;
    private float _fillProgress;
    private float _fillDuration = FillDuration;
    private float _fillDelayRemaining;
    private bool _triggerOnComplete = true;
    private FillKind _fillKind;
    private Color _fillColor = SingleFillColor;

    private readonly FillOverlay _fillOverlay;

    private enum FillKind
    {
        None,
        Single,
        Chain,
    }

    public ResearchTechnologyNode(TechnologyPrototype proto, SpriteSystem sprite, Color stateColor, bool selected, float zoom, bool researchable, bool chainResearchable)
    {
        Proto = proto;
        _stateColor = stateColor;
        _zoom = zoom;
        _researchable = researchable;
        _chainResearchable = chainResearchable;

        MouseFilter = MouseFilterMode.Stop;

        // Cap the icon so oversized machine/object sprites (e.g. the artifact
        // crusher) can never push the name and tier out of the card.
        var icon = sprite.Frame0(proto.Icon);
        const float maxIconPx = 64f;
        var iconScale = zoom * MathF.Min(2f,
            MathF.Min(maxIconPx / MathF.Max(icon.Size.X, 1f), maxIconPx / MathF.Max(icon.Size.Y, 1f)));

        // Content area inside the node, scaled by the zoom factor. The tier
        // band keeps a fixed pixel height: label text is not scaled by the
        // zoom, so scaling the band made "Уровень N" spill over the border.
        var showText = zoom >= TextLodZoom;
        var nodeWidth = NodeWidth * zoom;
        var nodeHeight = NodeHeight * zoom;
        var contentWidth = nodeWidth - 8f * zoom;
        var iconHeight = 72f * zoom;
        var tierHeight = 20f;

        // Zoomed-out nodes collapse to a compact icon-only chip.
        if (!showText)
            nodeHeight = iconHeight + 12f * zoom;

        var contentHeight = nodeHeight - 12f * zoom;
        var nameHeight = Math.Max(0f, contentHeight - iconHeight - tierHeight);

        SetSize = new Vector2(nodeWidth, nodeHeight);

        ApplyState(selected);

        var content = new LayoutContainer();
        AddChild(content);

        // Dark background band behind the icon (idle look). The fill overlay
        // is added on top of it but below the icon sprite and the texts, so
        // the fill also covers the icon area while researching.
        var iconBg = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = new Color(16, 16, 20, 255) },
        };
        LayoutContainer.SetPosition(iconBg, new Vector2(0, 0));
        iconBg.SetSize = new Vector2(contentWidth, iconHeight);
        content.AddChild(iconBg);

        // Green fill growing from the bottom of the card. The node's own Draw
        // paints it over the full card (margins and border included) so no
        // strips stay unfilled; this overlay re-covers the icon band, whose
        // opaque background would otherwise hide the fill.
        _fillOverlay = new FillOverlay();
        LayoutContainer.SetPosition(_fillOverlay, new Vector2(0, 0));
        _fillOverlay.SetSize = new Vector2(contentWidth, contentHeight);
        content.AddChild(_fillOverlay);

        // Icon sprite, centered over the dark band (and the fill).
        var iconSize = new Vector2(icon.Size.X * iconScale, icon.Size.Y * iconScale);
        var iconControl = new TextureRect
        {
            Texture = icon,
            TextureScale = new Vector2(iconScale, iconScale),
        };
        LayoutContainer.SetPosition(iconControl, new Vector2(
            MathF.Max(0f, (contentWidth - iconSize.X) / 2f),
            MathF.Max(0f, (iconHeight - iconSize.Y) / 2f)));
        content.AddChild(iconControl);

        if (showText)
        {
            // Name in a clipped band below the icon: it can never push the tier line.
            var namePanel = new PanelContainer
            {
                RectClipContent = true,
                Children =
                {
                    new RichTextLabel
                    {
                        Text = Loc.GetString(proto.Name),
                        MaxWidth = contentWidth - 8f * zoom,
                        HorizontalExpand = true,
                        VerticalAlignment = VAlignment.Top,
                        StyleClasses = { "LabelSubText" },
                    },
                },
            };
            LayoutContainer.SetPosition(namePanel, new Vector2(0, iconHeight));
            namePanel.SetSize = new Vector2(contentWidth, nameHeight);
            content.AddChild(namePanel);

            // Tier line pinned to the bottom edge of the card, centered. The
            // band has a fixed pixel height so the label text never reaches
            // the bottom border (label fonts are not scaled by the zoom).
            var tierLabel = new Label
            {
                Text = Loc.GetString("research-console-tier-info-small", ("tier", proto.Tier)),
                HorizontalAlignment = HAlignment.Center,
                VAlign = Label.VAlignMode.Center,
                StyleClasses = { "LabelSubText" },
            };
            LayoutContainer.SetPosition(tierLabel, new Vector2(0, iconHeight + nameHeight));
            tierLabel.SetSize = new Vector2(contentWidth, tierHeight);
            content.AddChild(tierLabel);
        }

        // Press selects the tech; holding a researchable one fills it green and
        // holding a locked-but-affordable one fills it yellow (whole branch).
        OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            NodePressed?.Invoke(this);

            if (_researchable)
                StartFill(FillKind.Single);
            else if (_chainResearchable)
                StartFill(FillKind.Chain);
        };

        // Releasing the button cancels the fill.
        OnKeyBindUp += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            _filling = false;
            _fillProgress = 0f;
            NodeReleased?.Invoke(this);
        };
    }

    /// <summary>
    /// Starts the hold-to-research fill: green for a single technology, yellow
    /// for a chain research of the whole branch up to this technology.
    /// </summary>
    private void StartFill(FillKind kind)
    {
        _filling = true;
        _fillProgress = 0f;
        _fillDelayRemaining = FillDelay;
        _fillKind = kind;
        _triggerOnComplete = true;

        switch (kind)
        {
            case FillKind.Chain:
                _fillDuration = ChainFillDuration;
                _fillColor = ChainFillColor;
                break;
            default:
                _fillDuration = FillDuration;
                _fillColor = SingleFillColor;
                break;
        }

        _fillOverlay.Color = _fillColor;
    }

    /// <summary>
    /// Resumes a hold-to-research fill on a rebuilt node (the tree is rebuilt
    /// whenever the server pushes a new console state while the player holds).
    /// </summary>
    public void ResumeHoldFill(float remaining, bool chain)
    {
        _filling = true;
        _fillKind = chain ? FillKind.Chain : FillKind.Single;
        _fillDuration = chain ? ChainFillDuration : FillDuration;
        _triggerOnComplete = true;
        _fillColor = chain ? ChainFillColor : SingleFillColor;
        _fillOverlay.Color = _fillColor;

        // The remaining time covers the grace period first, then the fill.
        _fillDelayRemaining = MathF.Max(0f, remaining - _fillDuration);
        _fillProgress = Math.Clamp(1f - MathF.Min(remaining, _fillDuration) / _fillDuration, 0f, 1f);
    }

    /// <summary>
    /// Highlights the node border when the technology becomes selected.
    /// </summary>
    public void SetSelected(bool selected)
    {
        ApplyState(selected);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Keep the fill overlay in sync with the fill progress.
        _fillOverlay.Progress = _fillProgress;

        if (!_filling)
            return;

        // Small grace period so quick clicks that just select a technology
        // never flash the fill.
        if (_fillDelayRemaining > 0f)
        {
            _fillDelayRemaining = MathF.Max(0f, _fillDelayRemaining - args.DeltaSeconds);
            return;
        }

        _fillProgress += args.DeltaSeconds / _fillDuration;
        if (_fillProgress >= 1f)
        {
            _fillProgress = 1f;
            _filling = false;

            if (_triggerOnComplete)
            {
                if (_fillKind == FillKind.Chain)
                    NodeChainResearched?.Invoke(this);
                else
                    NodeResearched?.Invoke(this);

                // The node is rebuilt right after (optimistic unlock / wave),
                // but if the research can't start the fill must not stay at 100%.
                _fillProgress = 0f;
            }
            else
            {
                // Hint fill: after completing, drop the overlay so the node
                // does not stay green if the server rejects the research.
                _fillProgress = 0f;
            }
        }
    }

    /// <summary>
    /// Plays the green fill animation without researching anything. Used as a
    /// hint when the player researches via the info panel button: it shows
    /// that holding the mouse on the node fills it too.
    /// </summary>
    public void PlayQuickFill(float duration)
    {
        _filling = true;
        _fillProgress = 0f;
        _fillDelayRemaining = 0f;
        _fillDuration = MathF.Max(0.01f, duration);
        _triggerOnComplete = false;
        _fillKind = FillKind.None;
        _fillColor = SingleFillColor;
        _fillOverlay.Color = _fillColor;
    }

    /// <summary>
    /// Draws the green research fill over the full card (margins and border
    /// included), so the fill always matches the card exactly. The fill
    /// overlay child re-covers the opaque icon band on top of this.
    /// </summary>
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_fillProgress <= 0f)
            return;

        var fillHeight = PixelSize.Y * _fillProgress;
        var fillBox = new UIBox2(0, PixelSize.Y - fillHeight, PixelSize.X, PixelSize.Y);
        handle.DrawRect(fillBox, _fillColor);
    }

    /// <summary>
    /// Draws the green research fill over the card background and the icon
    /// band, but below the icon sprite and the texts.
    /// </summary>
    private sealed class FillOverlay : Control
    {
        public float Progress;
        public Color Color = SingleFillColor;

        protected override void Draw(DrawingHandleScreen handle)
        {
            if (Progress <= 0f)
                return;

            var fillHeight = PixelSize.Y * MathF.Min(1f, Progress);
            var fillBox = new UIBox2(0, PixelSize.Y - fillHeight, PixelSize.X, PixelSize.Y);
            handle.DrawRect(fillBox, Color);
        }
    }

    private void ApplyState(bool selected)
    {
        StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = new Color(24, 24, 28, 255),
            BorderColor = selected ? Color.FromHex("#FFD54F") : _stateColor,
            BorderThickness = new Thickness((selected ? 3 : 2) * _zoom),
            ContentMarginLeftOverride = 4 * _zoom,
            ContentMarginTopOverride = 6 * _zoom,
            ContentMarginRightOverride = 4 * _zoom,
            ContentMarginBottomOverride = 6 * _zoom,
        };
    }
}