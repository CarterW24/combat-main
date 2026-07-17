using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class CommandPacketFreeInteractionNpc : BaseCommandPacket, IDeserializable<CommandPacketFreeInteractionNpc>
{
    public new const short OpCode = 20;

    public CommandPacketFreeInteractionNpc() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out CommandPacketFreeInteractionNpc value)
    {
        value = new CommandPacketFreeInteractionNpc();

        var reader = new PacketReader(data);

        return value.TryRead(ref reader);
    }
}
