using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._VanGuard.Research.UI;

/// <summary>
/// A layout surface for the research tree. Holds the technology nodes
/// and draws a link between every technology and the technologies it
/// requires (its prerequisites).
/// </summary>
/// <remarks>
/// Nodes are positioned manually with <see cref="LayoutContainer.SetPosition"/>.
/// Links use the same L-shaped connectors as the original tree: a horizontal
/// segment at the child's row followed by a vertical segment at the
/// prerequisite's column. Nodes are drawn on top, so the parts of a link
/// hidden underneath a node are never visible.
/// </remarks>
public sealed class ResearchTreeCanvas : LayoutContainer
{
    /// <summary>
    /// Color of the lines connecting technologies to their prerequisites.
    /// Set to the current discipline's color when the tree is populated.
    /// </summary>
    public Color LinkColor { get; set; } = new(0.55f, 0.7f, 1f, 0.8f);

    /// <summary>
    /// Fired when the player scrolls the mouse wheel over the tree.
    /// The argument is +1 for zoom-in (wheel up) and -1 for zoom-out.
    /// </summary>
    public event Action<float>? ZoomRequested;

    /// <summary>
    /// Handles the mouse wheel so the tree can be zoomed in and out.
    /// </summary>
    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (args.Delta.Y != 0)
        {
            ZoomRequested?.Invoke(args.Delta.Y);
            args.Handle();
        }
    }

    /// <summary>
    /// Draws the prerequisite links between the nodes currently on the canvas.
    /// </summary>
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        // Index every node on the canvas by technology id.
        var nodes = new Dictionary<string, ResearchTechnologyNode>();
        foreach (var child in Children)
        {
            if (child is ResearchTechnologyNode node)
                nodes[node.Proto.ID] = node;
        }

        if (nodes.Count == 0)
            return;

        // Draw a link from each technology to each of its prerequisites.
        foreach (var node in nodes.Values)
        {
            foreach (var prerequisiteId in node.Proto.TechnologyPrerequisites)
            {
                if (!nodes.TryGetValue(prerequisiteId, out var prerequisite))
                    continue;

                var start = CenterOf(node);           // requiring technology
                var end = CenterOf(prerequisite);     // required technology

                if (Math.Abs(start.Y - end.Y) < 0.5f)
                {
                    // Same row: a single straight segment.
                    handle.DrawLine(start, end, LinkColor);
                }
                else
                {
                    // Different rows: horizontal at the child's row, then
                    // vertical down/up at the prerequisite's column.
                    var corner = new Vector2(end.X, start.Y);
                    handle.DrawLine(start, corner, LinkColor);
                    handle.DrawLine(corner, end, LinkColor);
                }
            }
        }
    }

    private static Vector2 CenterOf(Control control)
    {
        return control.PixelPosition + control.PixelSize / 2f;
    }
}
