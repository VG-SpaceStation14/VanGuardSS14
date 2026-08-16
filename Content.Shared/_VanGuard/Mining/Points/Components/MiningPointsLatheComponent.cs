using Robust.Shared.GameStates;

namespace Content.Shared._VanGuard.Mining.Points.Components;

/// <summary>
/// Marks a lathe (e.g. an ore processor) as a source of mining points:
/// each time a recipe with <c>miningPoints</c> is produced, the lathe gains points.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MiningPointsLatheComponent : Component;
