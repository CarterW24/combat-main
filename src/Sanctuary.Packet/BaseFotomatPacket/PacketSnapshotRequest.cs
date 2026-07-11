using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Fotomat op156/sub4 SnapshotRequest. TESTED S2C 2026-07-11 (!snapself): like sub1/sub2, it only triggers
// the client's DISPLAY render (FUN_00bd4930) — it does NOT fire ScreenshotManager::CaptureTCGPortraitToBuffer
// (FUN_00b4e9f0) and does NOT upload. So none of the four op156 sub-opcodes (1/2/3/4), sent server->client,
// make this client build capture+upload its portrait; that path is dormant (see the harvester note on
// PacketPortraitDataRequestHandler.BuildImageData). Format best-guess sibling (guid + provider).
public class PacketSnapshotRequest : BaseFotomatPacket, ISerializablePacket
{
    public new const short OpCode = 4;

    public ulong Guid;

    public string? Provider;

    public PacketSnapshotRequest() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        Write(writer);          // [short 156][short 4]
        writer.Write(Guid);
        writer.Write(Provider);
        return writer.Buffer;
    }
}
