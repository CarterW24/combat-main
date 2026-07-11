using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Fotomat op156/sub1 GeneratePortraitRequest (S2C). Wire format VERIFIED 2026-07-11: guid(8) + provider
// string (sibling of sub2 PortraitDataRequest); the client accepted it and rendered for whatever guid was
// sent. Sending this makes the client RENDER its 70x70 portrait on demand (client FUN_00bd4930 fires) — a
// useful on-demand render trigger — but it does NOT upload it (the client's upload path stays dormant, same
// as sub2). Pair it with a headshot HARVESTER (see PacketPortraitDataRequestHandler.BuildImageData docs):
// server sends this to trigger the render, the harvester captures the PNG and POSTs it to WebAPI /image.
public class PacketGeneratePortraitRequest : BaseFotomatPacket, ISerializablePacket
{
    public new const short OpCode = 1;

    public ulong Guid;

    public string? Provider;

    public PacketGeneratePortraitRequest() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();
        Write(writer);          // [short 156][short 1]
        writer.Write(Guid);
        writer.Write(Provider);
        return writer.Buffer;
    }
}
