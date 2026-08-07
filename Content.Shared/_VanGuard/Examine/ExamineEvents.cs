using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._VanGuard.Examine;

[Serializable, NetSerializable]
public sealed class ExaminableCharacterInfoMessage : EntityEventArgs
{
    public NetEntity EntityUid { get; }
    public FormattedMessage Message { get; }

    public ExaminableCharacterInfoMessage(NetEntity entityUid, FormattedMessage message)
    {
        EntityUid = entityUid;
        Message = message;
    }
}