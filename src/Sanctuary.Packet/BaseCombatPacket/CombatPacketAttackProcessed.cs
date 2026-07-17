using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CombatPacketAttackProcessed : ISerializablePacket
{
    public const short OpCode = 32;
    public const short SubOpCode = 7;

    public ulong AttackerGuid;

    public ulong TargetGuid;

    public int Damage;

    public int MaxHealth;

    public int CompositeEffectId;

    public bool Bool1;
    public bool Bool2;

    public int Int4;

    public int CurrentHealth;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(AttackerGuid);
        writer.Write(AttackerGuid);
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
