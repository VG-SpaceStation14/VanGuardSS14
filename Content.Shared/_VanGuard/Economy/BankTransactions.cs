using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Economy;

/// <summary>
///     Shared operation limits. The server enforces them; the client uses the
///     same values only to keep request buttons disabled for amounts that would
///     be rejected.
/// </summary>
public static class BankTransactionLimits
{
    public const int MaxTransaction = 1_000_000;
}

/// <summary>
///     Shared account-number formatting. Only the last four digits are ever
///     exposed by the bank UI.
/// </summary>
public static class BankAccountFormat
{
    public static string Mask(string accountId)
    {
        return accountId.Length <= 4 ? accountId : $"•••• •••• •••• {accountId[^4..]}";
    }
}

[Serializable, NetSerializable]
public enum BankOperation : byte
{
    Deposit,
    Withdraw,
    DepositAll,
}

[Serializable, NetSerializable]
public enum BankTransactionStatus : byte
{
    Success,
    InvalidAmount,
    InsufficientFunds,
    NoAccount,
    NoCash,
    CashTooLarge,
    InvalidRequest,
    DuplicateRequest,
    WrongPin,
    NotAuthorized,
    NoCard,
    CardMismatch,
    Loading,
}

[Serializable, NetSerializable]
public sealed record BankTransactionRequest(int RequestId, BankOperation Operation, int Amount);

/// <summary>
///     A bank operation requested by an ATM.
/// </summary>
[Serializable, NetSerializable]
public sealed class BankAtmTransactionMessage(BankTransactionRequest request) : BoundUserInterfaceMessage
{
    public readonly BankTransactionRequest Request = request;
}

[Serializable, NetSerializable]
public sealed class BankAccountUiState(
    string? accountId,
    string ownerName,
    int balance,
    List<BankUiTransaction>? transactions = null,
    BankTransactionStatus lastStatus = BankTransactionStatus.InvalidRequest,
    int lastRequestId = 0,
    BankOperation lastOperation = BankOperation.Deposit,
    int lastAmount = 0,
    bool hasCard = false,
    bool authorized = true,
    string cardOwnerName = "",
    int cash = 0) : BoundUserInterfaceState
{
    public readonly string? AccountId = accountId;
    public readonly string OwnerName = ownerName;
    public readonly int Balance = balance;
    public readonly List<BankUiTransaction> Transactions = transactions ?? new();
    public readonly BankTransactionStatus LastStatus = lastStatus;
    public readonly int LastRequestId = lastRequestId;
    public readonly BankOperation LastOperation = lastOperation;
    public readonly int LastAmount = lastAmount;
    public readonly bool HasCard = hasCard;
    public readonly bool Authorized = authorized;
    public readonly string CardOwnerName = cardOwnerName;
    public readonly int Cash = cash;
}

[Serializable, NetSerializable]
public sealed record BankUiTransaction(
    TimeSpan Time,
    int Delta,
    string Reason,
    string? ReasonData,
    string? CounterpartyName = null,
    string? CounterpartyAccount = null,
    long? TransferId = null);

/// <summary>
///     A recipient that can be selected in the bank cartridge's transfer tab.
///     Contains no balance, PIN, full account number or account id.
/// </summary>
[Serializable, NetSerializable]
public sealed class BankTransferRecipient(
    NetEntity accountHolder,
    string name,
    string? jobTitle,
    string maskedAccount)
{
    /// <summary>
    ///     Network entity of the recipient's mind. Used only as a stable handle
    ///     for the transfer request; the server re-resolves and re-validates it.
    /// </summary>
    public readonly NetEntity AccountHolder = accountHolder;
    public readonly string Name = name;
    public readonly string? JobTitle = jobTitle;
    public readonly string MaskedAccount = maskedAccount;
}

[Serializable, NetSerializable]
public enum BankTransferSearchStatus : byte
{
    Idle,
    Results,
    EmptyQuery,
    NoResults,
    NoAccount,
    InvalidQuery,
    RateLimited,
    Error,
}

[Serializable, NetSerializable]
public enum BankTransferStatus : byte
{
    None,
    Success,
    InvalidAmount,
    InsufficientFunds,
    NoAccount,
    RecipientNotFound,
    RecipientNoAccount,
    RecipientNotOnStation,
    SelfTransfer,
    DuplicateRequest,
    InvalidRequest,
    TransferFailed,
}

[Serializable, NetSerializable]
public sealed class BankTransferUiState(
    BankTransferSearchStatus searchStatus,
    string query,
    List<BankTransferRecipient>? results,
    BankTransferStatus lastTransferStatus,
    int lastTransferRequestId,
    int lastTransferAmount)
{
    public readonly BankTransferSearchStatus SearchStatus = searchStatus;
    public readonly string Query = query;
    public readonly List<BankTransferRecipient> Results = results is null ? new() : new(results);
    public readonly BankTransferStatus LastTransferStatus = lastTransferStatus;
    public readonly int LastTransferRequestId = lastTransferRequestId;
    public readonly int LastTransferAmount = lastTransferAmount;
}

/// <summary>
///     Combined cartridge state: the main account page plus the transfer tab.
/// </summary>
[Serializable, NetSerializable]
public sealed class BankCartridgeUiState(
    BankAccountUiState account,
    BankTransferUiState transfer) : BoundUserInterfaceState
{
    public readonly BankAccountUiState Account = account;
    public readonly BankTransferUiState Transfer = transfer;
}

[Serializable, NetSerializable]
public sealed class BankCartridgeSearchMessage(string query) : CartridgeMessageEvent
{
    public readonly string Query = query;
}

[Serializable, NetSerializable]
public sealed class BankCartridgeTransferMessage(
    NetEntity recipient,
    int amount,
    int requestId) : CartridgeMessageEvent
{
    public readonly NetEntity Recipient = recipient;
    public readonly int Amount = amount;
    public readonly int RequestId = requestId;
}

[Serializable, NetSerializable]
public sealed class BankAtmAuthMessage(string pin) : BoundUserInterfaceMessage
{
    public readonly string Pin = pin;
}

[Serializable, NetSerializable]
public sealed class BankAtmEjectMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class BankAtmEndSessionMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public enum BankAtmUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum BankAtmVisualLayers : byte
{
    Base,
    Unshaded,
}
