using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Discord;

[Serializable, NetSerializable]
public sealed class RequestDiscordLinkMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer) { }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer) { }
}

[Serializable, NetSerializable]
public sealed class DiscordLinkCodeMessage : NetMessage
{
    public string Code = string.Empty;

    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer) 
        => Code = buffer.ReadString();

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer) 
        => buffer.Write(Code);
}

[Serializable, NetSerializable]
public sealed class DiscordLinkStateMessage : NetMessage
{
    public bool IsLinked;
    public int Tier;
    public string TierName = string.Empty;
    public string TimeLeft = string.Empty;

    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        IsLinked = buffer.ReadBoolean();
        Tier = buffer.ReadInt32();
        TierName = buffer.ReadString();
        TimeLeft = buffer.ReadString();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(IsLinked);
        buffer.Write(Tier);
        buffer.Write(TierName);
        buffer.Write(TimeLeft);
    }
}
