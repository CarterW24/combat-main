using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

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

    public int Unknown;

    public int CurrentHealth;

    public CombatPacketAttackProcessed() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(AttackerGuid);
        writer.Write(AttackerGuid);
        writer.Write(TargetGuid);

        writer.Write(Damage);
        writer.Write(MaxHealth);
        writer.Write(CompositeEffectId);

        writer.Write(IsCriticalHit);
        writer.Write(SuppressHitReaction);

        writer.Write(Unknown);
        writer.Write(CurrentHealth);

        return writer.Buffer;
    }
}
