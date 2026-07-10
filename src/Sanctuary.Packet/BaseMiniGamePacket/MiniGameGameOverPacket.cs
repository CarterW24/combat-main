using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// GAME OVER / RESULT (op39 sub 18). Tells the client the minigame/encounter ENDED and whether the
// player WON — this is what flips the end-of-game card between the win presentation and "TRY AGAIN!".
// RE'd 2026-07-03 (IDA): client dispatch ClientMiniGameManager::sub_9BFEA0 case 18 ->
// MiniGameGameOverPacket reader sub_9B48B0 -> handler sub_9AD480 ("MiniGame:OnGameOver"), which stores
// the fields on the matching MiniGameState (byte +99 = Won, ints +112/+116/+120) before the end screen
// (Lua MiniGameStateManager:endGame -> swf ShowEndScreen) reads the state.
//
// Wire: [short 39][byte 18] + BaseMiniGamePacket body [int StateId][int GroupId][int GameId] +
//       [bool Won][int Unknown1][int Unknown2][int Unknown3].
// Client ctor defaults (case-18 ctor call): StateId -1, Won 0, Unknown1 0, Unknown2 0, Unknown3 1 —
// we mirror those defaults; the 3 ints' semantics are un-RE'd (likely score/time/rank slots).
// StateId -1 = broadcast (matches every state, same as the knockout packet's base).
//
// LIVE FINDING that motivated this (2026-07-03 recording): completing the Frostfang encounter WITHOUT
// sending GameOver showed the FAILURE card ("TRY AGAIN! Frostfang Growler!") on exit — the state's Won
// byte stays 0 unless this packet sets it.
public class MiniGameGameOverPacket : BaseMiniGamePacket, ISerializablePacket
{
    public new const byte OpCode = 18;

    public bool Won;
    public int Unknown1;      // client default 0
    public int Unknown2;      // client default 0
    public int Unknown3 = 1;  // client default 1

    public MiniGameGameOverPacket(bool won, int stateId = -1, int groupId = -1, int gameId = -1)
        : base(OpCode, stateId, groupId, gameId)
    {
        Won = won;
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Won);
        writer.Write(Unknown1);
        writer.Write(Unknown2);
        writer.Write(Unknown3);

        return writer.Buffer;
    }
}
