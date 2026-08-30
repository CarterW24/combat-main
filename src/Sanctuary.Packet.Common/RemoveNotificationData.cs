using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class RemoveNotificationData : ISerializableType
{
    public ulong Guid;

    public int ThoughtBubbleIconId;

    public int CompositeEffectId;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Guid);

        writer.Write(ThoughtBubbleIconId);

        writer.Write(CompositeEffectId);
    }
}
