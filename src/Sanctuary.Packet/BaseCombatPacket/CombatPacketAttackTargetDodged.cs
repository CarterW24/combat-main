using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// op32/6 AttackTargetDodged — the client shows the "Dodge" text over the target and plays the attacker's swing.
// Wire: [op16][sub16] attacker target (20 bytes; attacker written once, unlike AttackProcessed). No damage.
// Note: our client build never renders this (its text is gated on a missing hit-type entry), so the dodge
// mechanic uses op32/5 Missed instead — kept here for reference. Reader sub_A2A820, handler sub_A2B580.
public class CombatPacketAttackTargetDodged : ISerializablePacket
{
    public const short OpCode = 32;
    public const short SubOpCode = 6;

    public ulong AttackerGuid;   // who swung and got evaded
    public ulong TargetGuid;     // who dodged

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(AttackerGuid);
        writer.Write(TargetGuid);

        return writer.Buffer;
    }
}
