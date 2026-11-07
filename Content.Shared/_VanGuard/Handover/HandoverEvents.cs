using Content.Shared.Alert;

namespace Content.Shared._VanGuard.Handover;

/// <summary>
/// Raised server-side when the receiver clicks the handover alert.
/// The server resolves the current OfferId from the receiver component.
/// </summary>
public sealed partial class AcceptHandoverEvent : BaseAlertEvent;
