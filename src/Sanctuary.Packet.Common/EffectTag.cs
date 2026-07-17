using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class EffectTag : ISerializableType
{
    public int InstanceId;

    public int EffectId;
    public int TypeId;

    public int Unknown4;
    public int Unknown5;

    public float Magnitude;

    public int Duration;

    public bool Unknown8;

    public ulong Guid;

    public int ElapsedMs;
    public int ElapsedMs2;

    public int Unknown12 = 1;

    public int CompositeEffectId;

    public int Unknown14a;
    public int Unknown14b;
    public int Unknown15;

    public int Unknown16 = 3;

    public bool Unknown17;
    public bool Unknown18;
    public bool Unknown19 = true;

    public int IconId;
    public byte TailByte;
    public int NameId;
    public int AbilityId;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(InstanceId);

        writer.Write(EffectId);
        writer.Write(TypeId);

        writer.Write(Unknown4);
        writer.Write(Unknown5);

        writer.Write(Magnitude);

        writer.Write(Duration);

        writer.Write(Unknown8);

        writer.Write(Guid);

        writer.Write(ElapsedMs);
        writer.Write(ElapsedMs2);

        writer.Write(Unknown12);

        writer.Write(CompositeEffectId);

        writer.Write(Unknown14a);
        writer.Write(Unknown14b);
        writer.Write(Unknown15);

        writer.Write(Unknown16);

        writer.Write(Unknown17);
        writer.Write(Unknown18);
        writer.Write(Unknown19);

        writer.Write(IconId);
        writer.Write(TailByte);
        writer.Write(NameId);
        writer.Write(AbilityId);
    }
}
