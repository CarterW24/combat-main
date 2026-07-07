using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Server -> client: force-closes the currently open NPC dialog (quest offer popup,
/// dialog camera zoom, etc.). Traced from the client's BaseCommandPacket dispatcher
/// (FUN_00aa2560, case 4 -> handler FUN_00aa0870): the handler reads only the 4-byte
/// header (short OpCode + short SubOpCode) and requires the buffer to be exactly
/// consumed, then calls the dialog teardown (FUN_008a7ce0 with force=true). Any extra
/// payload bytes make the client silently ignore the packet, so this must stay empty.
/// </summary>
public class CommandPacketEndDialog : BaseCommandPacket, ISerializablePacket
{
    public new const short OpCode = 4;

    public CommandPacketEndDialog() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        base.Write(writer);

        return writer.Buffer;
    }
}
