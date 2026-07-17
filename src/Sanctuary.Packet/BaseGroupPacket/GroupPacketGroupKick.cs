using System;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public class GroupPacketGroupKick : BaseGroupPacket, IDeserializable<GroupPacketGroupKick>
{
    public new const short OpCode = 6;

    private const int ReservedBlockSize = 28;

    public ulong TargetGuid;

    public string? TargetFirstName;
    public string? TargetLastName;

    public string TargetFullName =>
        string.IsNullOrEmpty(TargetLastName) ? (TargetFirstName ?? "")
        : $"{TargetFirstName} {TargetLastName}";

    public GroupPacketGroupKick() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out GroupPacketGroupKick value)
    {
        value = new GroupPacketGroupKick();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.TargetGuid))
            return false;

        for (var i = 0; i < ReservedBlockSize; i++)
        {
            if (!reader.TryRead(out byte _))
                return false;
        }

        for (var i = 0; i < 3; i++)
        {
            if (!reader.TryRead(out int _))
                return false;
        }

        if (!reader.TryRead(out value.TargetFirstName))
            return false;

        if (!reader.TryRead(out value.TargetLastName))
            return false;

        return true;
    }
}
