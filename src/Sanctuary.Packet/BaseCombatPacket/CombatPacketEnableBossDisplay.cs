using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// BOSS PLATE (Frostfang Fury): BaseCombatPacket (op 32) sub-opcode 9 = "EnableBossDisplay".
// RE'd 2026-07-02 (CombatProcessor::OnRoutePacket case 9 -> sub_A2B910): payload after the op/sub
// header is [ulong Guid][bool Enable]. Enable=true sets actor flag 0x80 and calls
// ClientProxiedCharacterManager::AddBoss(guid) — the red boss NAME + overhead boss health display
// seen on "Frostfang Alpha" / "Taka the Shadow" in the reference video. Enable=false -> RemoveBoss.
public class CombatPacketEnableBossDisplay : ISerializablePacket
{
    public const short OpCode = 32;
    public const short SubOpCode = 9;

    public ulong Guid;
    public bool Enable = true;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(Guid);
        writer.Write(Enable);

        return writer.Buffer;
    }
}
