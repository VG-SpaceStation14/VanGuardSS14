using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using System.Collections.Generic;

namespace Content.Shared._VanGuard.Mining.OreBags;

/// <summary>
/// Marks an ore bag that can filter which ores its magnet picks up.
/// The list of ignored ores is configured through the smart ore bag window.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SmartOreBagComponent : Component
{
    /// <summary>
    /// Ore prototypes that the magnet should skip when collecting.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntProtoId> IgnoredOres = new();
}

/// <summary>
/// Sent from the server to open the filter window for a smart ore bag.
/// </summary>
[Serializable, NetSerializable]
public sealed class OpenSmartOreBagWindowMessage(NetEntity entity, List<EntProtoId> ignoredOres) : EntityEventArgs
{
    public readonly NetEntity Entity = entity;
    public readonly List<EntProtoId> IgnoredOres = ignoredOres;
}

/// <summary>
/// Sent from the client to apply the new ignore list to a smart ore bag.
/// </summary>
[Serializable, NetSerializable]
public sealed class SmartOreBagUpdateMessage(NetEntity entity, List<EntProtoId> ignoredOres) : EntityEventArgs
{
    public readonly NetEntity Entity = entity;
    public readonly List<EntProtoId> IgnoredOres = ignoredOres;
}
