using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// BaseCombatPacket (op 32) sub-opcode 7 = "AttackProcessed" — the per-hit combat feedback packet.
//
// SEMANTICS PROVEN 2026-07-02 from the client struct reader (sub_A2A910) + handler
// (CombatProcessor::sub_A2BA40) + live 2014 samples (Packet-Protocol_Dump):
//
//   wire: [op16][sub16] ulong ulong ulong int int int bool bool int int   (50 bytes total)
//
//   The FIRST TWO ulongs land in the SAME struct field (+16, second overwrites first) — a duplicated
//   ATTACKER guid; the real server always sends them equal. The THIRD ulong (+24) is the TARGET.
//     ATTACKER (guid1=guid2): plays the attack-contact event; if it's the local player, resets the
//                             action-bar melee cooldown timer.
//     TARGET   (guid3):       floating damage number (-Damage), health bar (CurrentHealth/MaxHealth,
//                             falls back to client-tracked HP - Damage), recoil, hit composite effect.
//   Live sample series (bandit biting a 400-max-HP player): Damage=25, Max=400, Current 400→379→356→333.
//
// Getting attacker/target swapped makes the VICTIM swing and go on cooldown while the attacker takes
// the damage — exactly the bug seen live when this was mapped the other way around.
public class CombatPacketAttackProcessed : ISerializablePacket
{
    public const short OpCode = 32;
    public const short SubOpCode = 7;

    /// <summary>Who swings (written twice on the wire; both copies land in one client field).</summary>
    public ulong AttackerGuid;

    /// <summary>Who takes the number / bar / recoil / hit FX.</summary>
    public ulong TargetGuid;

    /// <summary>Damage dealt — the client renders the floating number as -Damage.</summary>
    public int Damage;

    /// <summary>Target's max HP (health-bar denominator).</summary>
    public int MaxHealth;

    /// <summary>Hit composite effect id played on the target.</summary>
    public int CompositeEffectId;

    public bool Bool1;
    public bool Bool2;

    public int Int4;

    /// <summary>Target's CURRENT HP after this hit (bar position; live samples count down per hit).</summary>
    public int CurrentHealth;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(AttackerGuid);
        writer.Write(AttackerGuid); // duplicated on the wire — client stores both into one field
        writer.Write(TargetGuid);

        writer.Write(Damage);
        writer.Write(MaxHealth);
        writer.Write(CompositeEffectId);

        writer.Write(Bool1);
        writer.Write(Bool2);

        writer.Write(Int4);
        writer.Write(CurrentHealth);

        return writer.Buffer;
    }
}
