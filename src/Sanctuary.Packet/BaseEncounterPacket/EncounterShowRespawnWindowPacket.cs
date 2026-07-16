using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// RESPAWN WINDOW (op41 sub125, S2C). Shows the client's native combat respawn window — the revive
// countdown + "Revive" button that this build never shows on its own at 0 HP. Client responds to the
// Revive click with EncounterParticipantResumePacket (sub122).
//
// WIRE FORMAT (RE'd from FreeRealms.exe, dispatcher FUN_0090d830 case 0x7d):
//   [int16 41][int16 125][int Unknown][int Unknown2]   <- base header (FUN_008d6690)
//   [int A][int B]                                      <- 2 more ints (FUN_008d6730)
// The client passes these to RespawnWindow:DisplayRespawn (FUN_00904300), where the respawn TIME (ms) is
// shown as a countdown (time/1000 seconds). My earlier header-only packet failed the body read, so the
// window was skipped. A/B carry the respawn time (+ a context/flag); we send the time in both slots' worth
// of intent — RespawnTimeMs is the countdown, the second int is a context id (0 = default).
public class EncounterShowRespawnWindowPacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 125;

    // First body int. (Sent as 10000 when the window first worked; not visibly the coin field.)
    public int RespawnTimeMs = 10000;

    // Second body int — the "Revive here" COIN COST. In-game with 0 here the window read
    // "Revive here: 0 coins", so this int drives that number. The client formats it as value/1000 (RE'd
    // DisplayRespawn FUN_00904300: "param_2 / 1000"), so 100000 -> "100 coins". CALIBRATE if the shown
    // number is off.
    public int ReviveHereCostRaw;

    public EncounterShowRespawnWindowPacket(int encounterId = 0, int instanceId = 0,
        int respawnTimeMs = 10000, int reviveHereCostRaw = 0) : base(OpCode)
    {
        Unknown = encounterId;
        Unknown2 = instanceId;
        RespawnTimeMs = respawnTimeMs;
        ReviveHereCostRaw = reviveHereCostRaw;
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // op41 + sub125 + Unknown + Unknown2

        writer.Write(RespawnTimeMs);
        writer.Write(ReviveHereCostRaw);

        return writer.Buffer;
    }
}
