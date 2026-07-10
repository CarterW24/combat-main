using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// ENCOUNTER STATE MACHINE (op41 sub106, from the live 2014-04-01 capture — Packet-Protocol_Dump):
// 16 bytes: [op41][sub106][int32 EncounterId][int32 InstanceId][int32 State].
// The real server steps State through the entry flow (same EncounterId/InstanceId as the details
// packet header ints): 2 (offer shown) -> 3 -> 4 (ready) -> 5 (after GO!) -> 6 (running).
// Live seq 26985(2) 27015(3) 27017(4) 27150(5, right after EncounterZoneIsReady) 27812(6, in-zone).
public class EncounterStatePacket : ISerializablePacket
{
    public const short OpCode = 41;
    public const short SubOpCode = 106;

    public int EncounterId;
    public int InstanceId;
    public int State;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(EncounterId);
        writer.Write(InstanceId);
        writer.Write(State);

        return writer.Buffer;
    }
}
