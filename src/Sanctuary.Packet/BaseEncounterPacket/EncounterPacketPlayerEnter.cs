using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// ★ THE GOALS-PANEL TRIGGER (op41 sub2 = "EncounterPacketPlayerEnter"). RE'd 2026-07-03 from the client
// (IDA) after two sessions of correct-but-invisible objective packets:
//
//   ClientCommandProcessor::sub_AA36C0 case 2 -> ClientMiniGameManager::sub_9B9500 -> (if a MiniGameState
//   exists and was created via the LAUNCH details packet, GAP_4[0]=1) marks it started (GAP_4[1]=1),
//   starts the timer, and calls sub_9B6AA0 = "MiniGame:ShowMiniGameHud" -> fires the Lua UI event
//   **"HUD:showMinigame"** (showStarCounter/showStatusIcon/showActionBar). THAT is what actually renders
//   the minigame HUD incl. the top-right Goals panel.
//
// So the goals panel is gated on PLAYER-ENTER, not on the op45 objective packets: creating the
// MiniGameState (launch) is necessary but NOT sufficient — without this packet sub_9B6AA0 never fires and
// the HUD never shows, no matter how correct the ObjectiveActivate/ObjectiveData are.
//
// WIRE FORMAT (2014-04-01 capture idx 28224, 21 bytes):
//   [op41][sub2][int32 EncounterId][int32 InstanceId][ulong PlayerGuid][byte Unknown]
// Same [EncounterId][InstanceId] header ints as the details/state packets. The real server sent it twice
// (guid=0 then the player's guid); we send the player's guid. Trailing byte = 0 in the capture.
public class EncounterPacketPlayerEnter : ISerializablePacket
{
    public const short OpCode = 41;
    public const short SubOpCode = 2;

    public int EncounterId;
    public int InstanceId;
    public ulong PlayerGuid;
    public byte Unknown;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(EncounterId);
        writer.Write(InstanceId);
        writer.Write(PlayerGuid);
        writer.Write(Unknown);

        return writer.Buffer;
    }
}
