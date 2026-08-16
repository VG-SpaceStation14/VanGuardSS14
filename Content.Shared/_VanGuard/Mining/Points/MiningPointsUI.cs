using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Mining.Points;

/// <summary>
/// Sent from the lathe UI to transfer the lathe's mining points onto the user's ID card.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheClaimMiningPointsMessage : BoundUserInterfaceMessage;
