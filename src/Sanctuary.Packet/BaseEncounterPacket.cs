using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class BaseEncounterPacket
{
    public const short OpCode = 41;

    private short SubOpCode;

    public int Unknown;
    public int Unknown2;

    public BaseEncounterPacket(short subOpCode)
    {
        SubOpCode = subOpCode;
    }

    public void Write(PacketWriter writer)
    {
        writer.Write(OpCode);
        writer.Write(SubOpCode);

        // The client's header reader (BaseEncounterPacket::sub_8D6690) expects these two ints — without
        // them every encounter packet is 8 bytes short and the client rejects it. (Likely an encounter
        // id/instance; 0 is fine for global combat-state toggles.)
        writer.Write(Unknown);
        writer.Write(Unknown2);
    }
}