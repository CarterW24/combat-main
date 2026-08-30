using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CombatPacketSingleAttackTarget : BaseCombatPacket, IDeserializable<CombatPacketSingleAttackTarget>
{
    public new const short OpCode = 3;

    public ulong TargetGuid;

    public CombatPacketSingleAttackTarget() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out CombatPacketSingleAttackTarget value)
    {
        value = new CombatPacketSingleAttackTarget();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.TargetGuid))
            return false;

        return true;
    }
}
