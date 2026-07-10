using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// COMBAT WIP: sends pre-captured raw packet bytes verbatim (the bytes already include the op/sub header).
// Used to replay real server->client captures (e.g. AbilityPacketSetDefinition) while we decode their
// structure. (See docs/STATUS.md + captures/.)
public class RawPacket : ISerializablePacket
{
    private readonly byte[] _bytes;

    public RawPacket(byte[] bytes)
    {
        _bytes = bytes;
    }

    public byte[] Serialize() => _bytes;
}
