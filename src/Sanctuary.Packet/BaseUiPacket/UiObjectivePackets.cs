using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class UiObjectiveAddPacket : BaseUiPacket, ISerializablePacket
{
    public new const byte OpCode = 1;

    public int ObjectiveId;
    public int Unknown2;
    public int NameId;
    public bool Unknown4;
    public bool MembersOnly;
    public int Unknown6;
    public bool Unknown7;
    public int Unknown8 = 1;    // real capture always sends 1

    public UiObjectiveAddPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ObjectiveId);
        writer.Write(Unknown2);
        writer.Write(NameId);
        writer.Write(Unknown4);
        writer.Write(MembersOnly);
        writer.Write(Unknown6);
        writer.Write(Unknown7);
        writer.Write(Unknown8);

        return writer.Buffer;
    }
}

public class UiObjectiveCompletePacket : BaseUiPacket, ISerializablePacket
{
    public new const byte OpCode = 3;

    public int ObjectiveId;

    public UiObjectiveCompletePacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ObjectiveId);

        return writer.Buffer;
    }
}

public class UiObjectiveClearPacket : BaseUiPacket, ISerializablePacket
{
    public new const byte OpCode = 5;

    public UiObjectiveClearPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        return writer.Buffer;
    }
}
