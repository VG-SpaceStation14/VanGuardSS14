using Robust.Shared.Serialization;

namespace Content.Server._VanGuard.Economy.Components;

/// <summary>
///     A personal bank account held by a mind. Accounts are created lazily the
///     first time a humanoid mind is registered and their id is written onto the
///     owner's ID card so it acts as a bank card.
/// </summary>
[RegisterComponent]
public sealed partial class EconomyAccountComponent : Component
{
    /// <summary>
    ///     12-digit account number used to address the account.
    /// </summary>
    [DataField]
    public string AccountId = string.Empty;

    /// <summary>
    ///     Name shown in logs and admin tools.
    /// </summary>
    [DataField]
    public string OwnerName = string.Empty;

    /// <summary>
    ///     Current account balance.
    /// </summary>
    [DataField]
    public int Balance;

    /// <summary>
    ///     The station this account currently belongs to.
    /// </summary>
    [DataField]
    public EntityUid? OwningStation;

    /// <summary>
    ///     The job whose salary is being paid into this account.
    /// </summary>
    [DataField]
    public string? JobId;

    /// <summary>
    ///     Maximum number of transactions kept in <see cref="History"/>.
    /// </summary>
    [DataField]
    public int MaxHistory = 64;

    /// <summary>
    ///     Recent transactions, newest last.
    /// </summary>
    [DataField]
    public List<EconomyTransaction> History = new();

    /// <summary>
    ///     Whether the sign-on payroll bonus was already paid for this round.
    /// </summary>
    [DataField]
    public bool StartingPayrollReceived;
}

[Serializable]
public sealed record EconomyTransaction(
    TimeSpan Time,
    int Delta,
    int ResultBalance,
    string Reason,
    string? ReasonData,
    NetEntity? Counterparty);
