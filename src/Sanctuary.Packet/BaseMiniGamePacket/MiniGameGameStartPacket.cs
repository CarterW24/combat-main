using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// S2C ack for a client MiniGameStartGame (sub 5) request — tells the client the game is starting.
// Ported verbatim from the team's `minigame` branch (their MiniGameStartGamePacketHandler replies
// with this, echoing StateId/GroupId/GameId).
public class MiniGameGameStartPacket : BaseMiniGamePacket, ISerializablePacket
{
    public new const byte OpCode = 17;

    public MiniGameGameStartPacket(int stateId, int groupId, int gameId) : base(OpCode, stateId, groupId, gameId)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        return writer.Buffer;
    }
}
