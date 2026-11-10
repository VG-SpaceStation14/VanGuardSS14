using Content.Shared._VanGuard.Economy;
using Robust.Shared.GameStates;

namespace Content.Server._VanGuard.Economy.Components;

/// <summary>
///     Marks a PDA cartridge as the bank application. Operations are resolved
///     against the acting player's mind account; the cartridge stores no balance.
/// </summary>
[RegisterComponent]
public sealed partial class BankCartridgeComponent : Component
{
    [ViewVariables]
    public EntityUid? LoaderUid;

    [ViewVariables]
    public EntityUid? ViewerUid;

    [ViewVariables]
    public string TransferQuery = string.Empty;

    [ViewVariables]
    public List<BankTransferRecipient> TransferResults = new();

    [ViewVariables]
    public BankTransferSearchStatus TransferSearchStatus = BankTransferSearchStatus.Idle;

    [ViewVariables]
    public BankTransferStatus LastTransferStatus = BankTransferStatus.None;

    [ViewVariables]
    public int LastTransferRequestId;

    [ViewVariables]
    public int LastTransferAmount;

    [ViewVariables]
    public TimeSpan NextSearchTime;
}
