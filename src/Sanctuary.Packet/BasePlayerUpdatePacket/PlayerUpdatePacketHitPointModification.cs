using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// COMBAT WIP: BasePlayerUpdatePacket (op 35) sub-opcode 35 = "HitPointModification" — the floating
// combat damage/heal number shown over an entity.
//
// WIRE FORMAT CONFIRMED from IDA (client UnserializePacket sub_8D6C50) + the 2014-04-01 capture:
//   ulong Guid   (m_llGuid)   SOURCE / attacker      ← (was mislabeled "target"; real order proven 2026-07-03)
//   ulong Guid2  (m_llGuid2)  VICTIM                 ← (was mislabeled "source")
//   bool  ShowFloatingText (m_bUnknown)  gates creation of the floating delta event, and only when
//         the LOCAL player is attacker or victim (BaseClient sub_8D3020: `if (a4 && guid==player||guid2==player)`)
//   int   Unknown2 (m_nUnknown2)  MAX hp    (health-bar denominator)
//   int   Unknown3 (m_nUnknown3)  CURRENT hp after the hit (bar position)
//   int   Unknown4 (m_nUnknown4)  DELTA = -damage  ← the floating number (was wrongly put in Unknown2)
//   bool  IsCriticalHit (m_bUnknown5)  IDA-verified 2026-07-17: PlayerHitpointDeltaEvent exec (0xAE2350)
//         renders negative deltas as "%d!!" when set (plain "%d" otherwise) + styles the floating text;
//         heals are always "+%d". NOT the leading bool (old wrong guess).
// Real NPC->player sample: Guid=NPC, Guid2=player, i2=7828(max), i3=7823(cur-after), i4=-5(delta).
// A short packet trips m_bReachedEnd and the client rejects it (the previous bug).
// NOTE: this packet does NOT reset the action-bar melee timer, so it's the correct vehicle for the
// PLAYER's own hits — AttackProcessed(attacker=player) would trip the [1] cooldown.
public class PlayerUpdatePacketHitPointModification : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 35;

    public ulong Guid;
    public ulong Guid2;

    public bool ShowFloatingText;

    public int Unknown2;
    public int Unknown3;
    public int Unknown4;

    public bool IsCriticalHit;

    public PlayerUpdatePacketHitPointModification() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [op 35][sub 35]

        writer.Write(Guid);
        writer.Write(Guid2);

        writer.Write(ShowFloatingText);

        writer.Write(Unknown2);
        writer.Write(Unknown3);
        writer.Write(Unknown4);

        writer.Write(IsCriticalHit);

        return writer.Buffer;
    }
}
