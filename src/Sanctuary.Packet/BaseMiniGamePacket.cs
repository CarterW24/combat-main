using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// INSTANCE WIP (Frostfang Fury): BaseMiniGamePacket (op 39) — the minigame lifecycle family. Ported from the
// team's `minigame` branch (github Open-Source-Free-Realms/Sanctuary, branch minigame). Wire format:
// [short op39][byte subOpCode][int StateId][int GroupId][int GameId]. NOTE the sub-opcode is a BYTE here
// (unlike op41's short sub-opcode). Known subs (branch): 5=StartGame (C2S — pressing GO!/start on a minigame
// panel), 17=GameStart (S2C ack), End, Leave, Payload, Info(=MiniGameInfoPacket, bigger body).
public class BaseMiniGamePacket
{
    public const short OpCode = 39;

    private byte SubOpCode;

    public int StateId;
    public int GroupId;
    public int GameId;

    public BaseMiniGamePacket(byte subOpCode, int stateId, int groupId, int gameId)
    {
        SubOpCode = subOpCode;

        StateId = stateId;
        GroupId = groupId;
        GameId = gameId;
    }

    public virtual void Write(PacketWriter writer)
    {
        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(StateId);
        writer.Write(GroupId);
        writer.Write(GameId);
    }
}
