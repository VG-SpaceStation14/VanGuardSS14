using System.Diagnostics.CodeAnalysis;
using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._VanGuard.Radio;

/// <summary>
///     Resolves the job icon that should be shown next to a radio speaker's name.
/// </summary>
/// <remarks>
///     Radio messages are plain text, so the icon is described with a markup tag that the client renders.
///     The markup is produced here to keep the radio formatting code free of inventory lookups.
/// </remarks>
public sealed partial class RadioJobIconSystem : EntitySystem
{
    private static readonly ProtoId<JobIconPrototype> NoIdIcon = "JobIconNoId";

    [Dependency] private InventorySystem _inventory = default!;

    /// <summary>
    ///     Builds the markup for the speaker's job icon.
    /// </summary>
    /// <returns>
    ///     A self-closing markup tag, or an empty string when no valid icon can be resolved.
    /// </returns>
    public string GetIconMarkup(EntityUid speaker)
    {
        var icon = NoIdIcon;

        if (TryGetIdCard(speaker, out var idCard) && IsValidIcon(idCard.JobIcon))
            icon = idCard.JobIcon;

        if (!ProtoMan.TryIndex(icon, out _))
        {
            icon = NoIdIcon;
            if (!ProtoMan.TryIndex(icon, out _))
                return string.Empty;
        }

        return $"[jobicon=\"{FormattedMessage.EscapeStringParameter(icon.Id)}\"/]";
    }

    private static bool IsValidIcon(ProtoId<JobIconPrototype> icon)
    {
        return !string.IsNullOrWhiteSpace(icon.Id);
    }

    private bool TryGetIdCard(EntityUid speaker, [NotNullWhen(true)] out IdCardComponent? idCard)
    {
        idCard = null;

        if (!_inventory.HasSlot(speaker, "id"))
            return false;

        if (!_inventory.TryGetSlotEntity(speaker, "id", out var item) || item == null)
            return false;

        if (TryComp<PdaComponent>(item, out var pda))
            item = pda.ContainedId;

        if (item == null)
            return false;

        return TryComp<IdCardComponent>(item, out idCard);
    }
}
