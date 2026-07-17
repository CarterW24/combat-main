using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class InstancePermission : ISerializableType
{
    public ulong Guid;

    public int Level;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Guid);
        writer.Write(Level);
    }
}
