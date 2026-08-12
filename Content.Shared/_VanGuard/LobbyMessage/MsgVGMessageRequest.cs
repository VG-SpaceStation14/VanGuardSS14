using System;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.LobbyMessage;

/// <summary>
///     Client → server request asking the server to resend the current lobby message.
///     A network event (not a raw NetMessage) so both sides handle it like any other
///     gameplay event and no manual net-message registration is required.
/// </summary>
[Serializable, NetSerializable]
public sealed class MsgVGMessageRequest : EntityEventArgs
{
}
