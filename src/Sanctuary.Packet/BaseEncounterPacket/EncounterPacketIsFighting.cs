using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// COMBAT WIP: BaseEncounterPacket (op 41) sub-opcode 133 = "EncounterPacketIsFighting".
// IDA (ClientCommandProcessor::sub_AA36C0 case 133) -> BaseClient::SetIsFighting.
// RE-VERIFIED 2026-07-17: despite its name, SetIsFighting writes **m_bInCombatArea** (the GameDock
// combat indicator / floating-text gate), NOT m_bIsFighting — the knockout-window mode flag is
// written ONLY by sibling sub 132 (SetInWorldCombat -> m_bIsFighting). The two setter names are
// swapped relative to the fields they write.
// Opens (with sub 132) the client-side gate in BaseClient::sub_8BB0B0 that otherwise suppresses
// floating combat text (damage numbers, MISS!).
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
