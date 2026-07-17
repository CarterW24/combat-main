using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public static class ObjectivePacketWriter
{
    public const short OpCode = 45;

    private static void WriteHeader(PacketWriter writer, byte subOpCode)
    {
        writer.Write(OpCode);
        writer.Write(subOpCode);
    }

    private static void WriteEmptyRewardBundle(PacketWriter writer)
    {
        writer.Write(false);
        for (var i = 0; i < 9; i++) writer.Write(0);
        writer.Write(0); writer.Write(0);
        writer.Write(0); writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
    }
}

public class ObjectiveAddPacket : ISerializablePacket
{
    public const byte SubOpCode = 5;

    public int ObjectiveId;
    public int NameId;
    public int DescriptionId;
    public int Status = 2;
    public int Count;
    public int Total;
    public bool MemberOnly;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(ObjectivePacketWriter.OpCode);
        writer.Write(SubOpCode);

        writer.Write(ObjectiveId);
        writer.Write(NameId);
        writer.Write(DescriptionId);
        writer.Write(false);
        writer.Write(false);
        for (var i = 0; i < 9; i++) writer.Write(0);
        writer.Write(0); writer.Write(0);
        writer.Write(0); writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(Status);
        writer.Write(Count);
        writer.Write(Total);
        writer.Write(0);
        writer.Write(MemberOnly);
        writer.Write(0);

        return writer.Buffer;
    }
}

public class ObjectiveActivatePacket : ISerializablePacket
{
    public const byte SubOpCode = 1;

    public int ObjectiveId;
    public int Total;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(ObjectivePacketWriter.OpCode);
        writer.Write(SubOpCode);
        writer.Write(ObjectiveId);
        writer.Write(Total);

        return writer.Buffer;
    }
}

public class ObjectiveCompletePacket : ISerializablePacket
{
    public const byte SubOpCode = 3;

    public int ObjectiveId;
    public int Unknown;          // 0 on the live wire
    public int Unknown2 = 5000;  // 5000 on the live wire (announce display ms?)

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(ObjectivePacketWriter.OpCode);
        writer.Write(SubOpCode);
        writer.Write(ObjectiveId);
        writer.Write(Unknown);
        writer.Write(Unknown2);

        return writer.Buffer;
    }
}

public class ObjectiveUpdatePacket : ISerializablePacket
{
    public const byte SubOpCode = 2;

    public int ObjectiveId;
    public int Count;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(ObjectivePacketWriter.OpCode);
        writer.Write(SubOpCode);
        writer.Write(ObjectiveId);
        writer.Write(Count);

        return writer.Buffer;
    }
}
