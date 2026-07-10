using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// KNOCKOUT COUNTER / LIMIT (op39 sub 23). Drives the minigame HUD's star counter AND the encounter's
// knockout-limit lose condition. RE'd 2026-07-03 (client dispatch ClientMiniGameManager::sub_9BFEA0
// case 23 -> sub_9B87A0 -> reader sub_9B4770 -> stores via sub_C42380 -> Lua "GroupHandler:SetKO").
//
// Wire: [short 39][byte 23] + BaseMiniGamePacket body [int StateId][int GroupId][int GameId] +
//       [int CurrentKnockouts][int MaxKnockouts].
// GROUND TRUTH (2014-04-01 capture idx 28043/28060/28071, sent 3x in the entry burst):
//   2700 17  FFFFFFFF FFFFFFFF FFFFFFFF  00000000  05000000
//   = base all -1 (broadcast/whole-team), Current 0, Max 5.
//
// Client SetKO (minigame HUD, COMBAT type only): reads Ui.GetPlayerKnockoutInfo() -> (KOs, KOmax) and
// shows KOmax - KOs REMAINING ("KnockoutsRemaining" string, "@1" = remaining). So Current=0/Max=5 renders
// "5" remaining. The matching objective row ("Don't get knocked out 5 times!") is a separate op47 goal.
//
// The limit (Max) appears to scale with party size on live (a solo run capped at 5); we have solo data
// only, so callers pass the count explicitly. Base ids are -1 (the counter is whole-team, not per-state).
public class MiniGameKnockOutPacket : BaseMiniGamePacket, ISerializablePacket
{
    public new const byte OpCode = 23;

    public int CurrentKnockouts;
    public int MaxKnockouts;

    public MiniGameKnockOutPacket(int currentKnockouts, int maxKnockouts,
        int stateId = -1, int groupId = -1, int gameId = -1)
        : base(OpCode, stateId, groupId, gameId)
    {
        CurrentKnockouts = currentKnockouts;
        MaxKnockouts = maxKnockouts;
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(CurrentKnockouts);
        writer.Write(MaxKnockouts);

        return writer.Buffer;
    }
}
