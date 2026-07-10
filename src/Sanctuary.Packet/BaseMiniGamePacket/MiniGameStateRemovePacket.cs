using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// MINIGAME EXIT (RE'd 2026-07-02 late): op39 sub-opcode 19 = server-driven "minigame state removed".
// Client handler (ClientMiniGameManager dispatch case 19 -> sub_9BF190, StateId<=0 targets the first
// MiniGameState) performs the FULL local teardown the client otherwise only does when the player
// clicks leave: hides the minigame UI (":Hide"/"QuitGameWithNoEndGameScreen"), fires
// "MiniGame:LeaveMiniGame", removes the MiniGameState, and — for combat-type games — calls the
// combat-exit routine (sub_8A8E80 -> LeaveLobbyGame). Without this the client stays "in the game"
// forever after the encounter (stuck minigame state). Pair it with PacketEncounterDataCommon
// defaults to fully release combat mode.
public class MiniGameStateRemovePacket : BaseMiniGamePacket, ISerializablePacket
{
    public new const byte OpCode = 19;

    public MiniGameStateRemovePacket(int stateId = 0, int groupId = -1, int gameId = -1)
        : base(OpCode, stateId, groupId, gameId)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        return writer.Buffer;
    }
}
