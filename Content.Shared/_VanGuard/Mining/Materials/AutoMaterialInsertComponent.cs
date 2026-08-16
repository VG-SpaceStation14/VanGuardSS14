using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._VanGuard.Mining.Materials;

/// <summary>
/// When placed on a machine (e.g. an ore processor), using an item carrying the configured
/// tag with storage (e.g. an ore bag) inserts all of the stored contents into the machine.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AutoMaterialInsertComponent : Component
{
    /// <summary>
    /// Tag that the used item must have for its contents to be auto-inserted.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<TagPrototype> Tag;
}
