using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// NPC DISPOSITION CONTROL (op35 sub 28). RE'd 2026-07-03 (IDA BaseClient::HandlePlayerUpdatePacket
// case 28): [ulong Guid][int Disposition] -> ProxiedCharacter.m_nDisposition (+0x178) directly.
//
// WHY THIS PACKET MATTERS (the red-name mechanism, finally complete):
//   * The nameplate COLOR RESOLVER (ProxiedCharacter::sub_966460) runs when NameColor==0 and colors the
//     overhead name by DISPOSITION: 0 HOSTILE -> Display.NameColorHostileNpc 0xFFFF0000 (RED);
//     1 neutral / 2 ally / other -> 0xFF6699CC (the bluish default). A nonzero static NameColor
//     suppresses the resolver entirely.
//   * The AddNpc packet's own Disposition int is IGNORED for the character: the AddNpc apply sets
//     m_nDisposition from the CLIENT-GLOBAL arena flag (BaseClient+0x958, set by details Unknown3==6):
//     arena -> 0 hostile, else -> 2 ally. The ProxiedCharacter ctor default is also 2 (ally) — which is
//     why every NPC we spawn renders the bluish name.
//   * THIS packet is the per-NPC override: send Disposition=0 after AddNpc to make a hostile (RED name
//     via the resolver, when the NPC has no static NameColor).
public class PlayerUpdatePacketUpdateDisposition : ISerializablePacket
{
    public const short OpCode = 35;
    public const short SubOpCode = 28;

    public ulong Guid;
    public int Disposition;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(Guid);
        writer.Write(Disposition);

        return writer.Buffer;
    }
}
