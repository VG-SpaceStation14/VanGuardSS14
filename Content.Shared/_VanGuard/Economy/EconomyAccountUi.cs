using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Economy;

[Serializable, NetSerializable]
public enum EconomyAccountUiKey : byte
{
    Key,
}

/// <summary>
///     Sent by the wallet window when the owner asks to withdraw physical credits.
/// </summary>
[Serializable, NetSerializable]
public sealed class EconomyWithdrawMessage(int amount, string? accountIdOverride) : BoundUserInterfaceMessage
{
    public readonly int Amount = amount;
    public readonly string? AccountIdOverride = accountIdOverride;
}

/// <summary>
///     Sent by the wallet window when the owner manually selects another account id.
/// </summary>
[Serializable, NetSerializable]
public sealed class EconomySelectAccountMessage(string? accountIdOverride) : BoundUserInterfaceMessage
{
    public readonly string? AccountIdOverride = accountIdOverride;
}

[Serializable, NetSerializable]
public sealed class EconomyAccountBoundUiState(string? accountId, int balance, List<EconomyUiTransaction>? transactions = null) : BoundUserInterfaceState
{
    public readonly string? AccountId = accountId;
    public readonly int Balance = balance;
    public readonly List<EconomyUiTransaction> Transactions = transactions ?? new();
}

/// <summary>
///     A single account transaction shown in the wallet history, newest first.
/// </summary>
[Serializable, NetSerializable]
public sealed record EconomyUiTransaction(TimeSpan Time, int Delta, string Reason, string? ReasonData);

