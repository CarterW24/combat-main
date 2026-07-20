using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// BaseEncounterPacket (op 41) sub-opcode 132. IDA RE-VERIFIED 2026-07-17 — the handler names are
// SWAPPED vs what the bool does: case 132 -> BaseClient::SetInWorldCombat, which writes
// **m_bIsFighting** (+0x80) — the flag BaseClient::DisplayRespawn passes to Lua
// RespawnWindow:DisplayRespawn as isOverworld. TRUE here = the knockout window renders the PAID
// two-button overworld panel; FALSE = the in-encounter 10s countdown. This packet is the ONLY
// writer of that flag in the whole client (byte-scan confirmed). Live sends neither 132 nor 133
// inside encounters.
public class EncounterOverworldCombatPacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 132;

    public bool IsFighting;

    public EncounterOverworldCombatPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(IsFighting);

        return writer.Buffer;
    }
}