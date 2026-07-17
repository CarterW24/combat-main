using System;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public class GroupPacketGroupInvite : BaseGroupPacket, IDeserializable<GroupPacketGroupInvite>, ISerializablePacket
{
    public new const short OpCode = 1;

    private const int ZeroBlockSize = 48;

    public string? TargetName;

    public NameData? InviterName;

    public ulong InviterGuid;

    public GroupPacketGroupInvite() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);
        writer.Write(InviterGuid);
        (InviterName ?? new NameData()).Serialize(writer);
        writer.Write((ulong)0);
        new NameData().Serialize(writer);
        writer.Write(0);
        for (var i = 0; i < 5; i++)
            writer.Write(0);

        return writer.Buffer;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out GroupPacketGroupInvite value)
    {
        value = new GroupPacketGroupInvite();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        for (var i = 0; i < ZeroBlockSize; i++)
        {
            if (!reader.TryRead(out byte _))
                return false;
        }

        if (!reader.TryRead(out value.TargetName))
            return false;

        return true;
    }
}
