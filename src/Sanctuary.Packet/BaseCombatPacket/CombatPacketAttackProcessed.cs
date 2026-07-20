using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// IDA-verified 2026-07-17 (client CombatProcessor 0xA2BA40): this handler resets the melee
// action-bar timer, then builds its OWN floating-number event — so op32/7 numbers do NOT go
// through HitPointModification. IsCriticalHit here is what renders "%d!!" for these hits;
// SuppressHitReaction=true skips the target's hit-react anim (code-anim 8) + camera shake.
public class CombatPacketAttackProcessed : BaseCombatPacket, ISerializablePacket
{
    public new const short OpCode = 7;

    public ulong AttackerGuid;

    public ulong TargetGuid;

    public int Damage;

    public int MaxHealth;

    public int CompositeEffectId;

    public bool IsCriticalHit;
    public bool SuppressHitReaction;

    public int Int4;

    public int CurrentHealth;

    public CombatPacketAttackProcessed() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        // The wire carries THREE guids (client unserializer reads 3 qwords); live duplicates the
        // attacker in the first two slots.
        writer.Write(AttackerGuid);
        writer.Write(AttackerGuid);
        writer.Write(TargetGuid);

        writer.Write(Damage);
        writer.Write(MaxHealth);
        writer.Write(CompositeEffectId);

        writer.Write(IsCriticalHit);
        writer.Write(SuppressHitReaction);

        writer.Write(Int4);
        writer.Write(CurrentHealth);

        return writer.Buffer;
    }
}
