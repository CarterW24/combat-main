using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// COMBAT WIP: BaseEncounterPacket (op 41) sub-opcode 133 = "EncounterPacketIsFighting".
// IDA (ClientCommandProcessor::sub_AA36C0 case 133) -> BaseClient::SetIsFighting(m_bInWorldCombat).
// Opens (with its sibling sub 132 EncounterOverworldCombatPacket -> SetInWorldCombat) the client-side
// gate in BaseClient::sub_8BB0B0 that otherwise suppresses floating combat text (damage numbers, MISS!).
public class EncounterPacketIsFighting : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 133;

    public bool InWorldCombat;

    public EncounterPacketIsFighting() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [op 41][sub 133]

        writer.Write(InWorldCombat);

        return writer.Buffer;
    }
}
