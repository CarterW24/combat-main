using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Sent by client when the player clicks to auto-attack a target (OpCode 32, SubOpCode 1).
public class CombatPacketAutoAttackTarget : BaseCombatPacket, IDeserializable<CombatPacketAutoAttackTarget>
{
    public new const short OpCode = 1;

    // GUID of the target entity to attack.
    public ulong TargetGuid;

    public CombatPacketAutoAttackTarget() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out CombatPacketAutoAttackTarget value)
    {
        value = new CombatPacketAutoAttackTarget();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.TargetGuid))
            return false;

        return true;
    }
}
