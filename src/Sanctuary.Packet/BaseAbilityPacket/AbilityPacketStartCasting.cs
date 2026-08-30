using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class AbilityPacketStartCasting : BaseAbilityPacket, ISerializablePacket
{
    public new const short OpCode = 3;

    public ulong CasterGuid;
    public ulong TargetGuid;
    public int CompositeEffectId;
    public int Animation = -1;
    public int AbilityId;
    public float ActionTime;
    public bool HasActionProgress;

    public AbilityPacketStartCasting() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        base.Write(writer);

        writer.Write(CasterGuid);
        writer.Write(TargetGuid);
        writer.Write(CompositeEffectId);
        writer.Write(Animation);
        writer.Write(AbilityId);
        writer.Write(ActionTime);
        writer.Write(HasActionProgress);

        return writer.Buffer;
    }
}
