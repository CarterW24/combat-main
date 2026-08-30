using System.Collections.Generic;

using Sanctuary.Core.IO;

using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public class PlayerUpdatePacketRemoveNotifications : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 11;

    public List<RemoveNotificationData> Entries = [];

    public PlayerUpdatePacketRemoveNotifications() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Entries);

        return writer.Buffer;
    }
}
