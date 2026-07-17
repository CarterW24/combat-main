using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// BaseEncounterPacket (op 41) sub-opcode 126 (S2C, header-only). IDA 2026-07-15
// (BaseClient::sub_90D830): fires Lua RespawnWindow:Hide — closes the knockout countdown window and
// re-enables keyboard/movement. Sent after the server accepts the sub-122 Resume (revive) request.
// (Sub 126 has no packet-name string in the client; named here by behavior.)
public class EncounterHideRespawnWindowPacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 126;

    public EncounterHideRespawnWindowPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [op 41][sub 126][header ints]

        return writer.Buffer;
    }
}
