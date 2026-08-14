using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.VendingMachines;

/// <summary>
///     Sent from the server to the client that has a vending interface open so the
///     window can display the acting player's current bank balance. The balance is
///     pushed again after every successful purchase.
/// </summary>
[Serializable, NetSerializable]
public sealed class VendingMachineBalanceUpdateMessage(int balance) : BoundUserInterfaceMessage
{
    public readonly int Balance = balance;
}
