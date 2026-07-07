using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Best-effort field layout - not verified against a live client capture.
/// </summary>
public class PlayerUpdatePacketRemoveNotifications : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 11;

    public List<ulong> Guids = new List<ulong>();

    public PlayerUpdatePacketRemoveNotifications() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guids);

        return writer.Buffer;
    }
}
