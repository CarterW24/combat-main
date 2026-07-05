using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// GOALS TRACKER (Frostfang Fury) — the client's top-right "Goals (N)" HUD panel.
// RE'd from the client 2026-07-02 (full writeup: drafts/objective-goals-packets.md):
//
//   BaseObjectivePacket family = opcode 45 (client tunnel), header [int16 45][byte subOpCode]
//   dispatch: BaseClient::OnTunneledClientPacket2 case 45 -> ClientMiniGameManager::sub_9BE130.
//   A MiniGameState must exist client-side (our GO! flow creates one via the offer popup + GameStart).
//
//   sub 5 = ADD/UPSERT a goal: payload = ObjectiveData (below). Status 2 announces
//           «New Objective: "%s"» and shows the entry in the Goals panel.
//   sub 1 = ACTIVATE: [int32 ObjectiveId][int32 Total] -> sets ObjectiveTotal, status=2 (announce).
//   sub 2 = UPDATE:   [int32 ObjectiveId][int32 Count] -> sets ObjectiveCount (progress tick).
//
//   ObjectiveData (ObjectiveData::sub_8FD770; field semantics from MiniGameGoalState ctor @0xC42870):
//     int32 ObjectiveId · int32 NameId · int32 DescriptionId · byte Unknown4 ·
//     RewardBundleBase (69-byte empty ok) · int32 Status · int32 Count · int32 Total ·
//     int32 Unknown8 · byte MemberOnly · int32 Unknown10
//
//   CAVEAT: NameId resolves via the SERVER-FED string table (same as the popup title). Unknown ids
//   render blank; ADMIN clients fall back to «<OBJECTIVE %d>».
public static class ObjectivePacketWriter
{
    public const short OpCode = 45;

    private static void WriteHeader(PacketWriter writer, byte subOpCode)
    {
        writer.Write(OpCode);
        writer.Write(subOpCode);
    }

    // RewardBundleBase with no entries — 69 fixed bytes (same shape EncounterDetailsResponsePacket uses).
    private static void WriteEmptyRewardBundle(PacketWriter writer)
    {
        writer.Write(false);                              // byte Unknown
        for (var i = 0; i < 9; i++) writer.Write(0);      // int32 U2..U10
        writer.Write(0); writer.Write(0);                 // int32 pairA
        writer.Write(0); writer.Write(0);                 // int32 pairB
        writer.Write(0);                                  // int32 U13
        writer.Write(0);                                  // int32 U14
        writer.Write(0);                                  // int32 entryCount = 0
        writer.Write(0);                                  // int32 U15
    }
}

/// <summary>Sub 5 — add/announce a goal. DEPRECATED / DO NOT USE: GROUND TRUTH (2026-07-03, 04-01
/// capture) shows the real server NEVER sends sub5. Goals are DEFINED inline in the launch details
/// packet's ObjectiveData[] and ACTIVATED by id (sub1). Sending sub5 for an id the MiniGameState doesn't
/// already know is dropped -> no panel. Kept only for reference; define goals via EncounterObjective.</summary>
public class ObjectiveAddPacket : ISerializablePacket
{
    public const byte SubOpCode = 5;

    public int ObjectiveId;
    public int NameId;          // goal text (server-known string id!)
    public int DescriptionId;
    public int Status = 2;      // 2 = newly active -> "New Objective" announce
    public int Count;
    public int Total;
    public bool MemberOnly;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(ObjectivePacketWriter.OpCode);
        writer.Write(SubOpCode);

        writer.Write(ObjectiveId);
        writer.Write(NameId);
        writer.Write(DescriptionId);
        writer.Write(false);            // byte Unknown4
        // empty RewardBundleBase (69 bytes)
        writer.Write(false);
        for (var i = 0; i < 9; i++) writer.Write(0);
        writer.Write(0); writer.Write(0);
        writer.Write(0); writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        // ---
        writer.Write(Status);
        writer.Write(Count);
        writer.Write(Total);
        writer.Write(0);                // int32 Unknown8
        writer.Write(MemberOnly);       // byte
        writer.Write(0);                // int32 Unknown10

        return writer.Buffer;
    }
}

/// <summary>Sub 1 — (re)activate a goal: sets its Total and fires the "New Objective" announce.</summary>
public class ObjectiveActivatePacket : ISerializablePacket
{
    public const byte SubOpCode = 1;

    public int ObjectiveId;
    public int Total;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(ObjectivePacketWriter.OpCode);
        writer.Write(SubOpCode);
        writer.Write(ObjectiveId);
        writer.Write(Total);

        return writer.Buffer;
    }
}

/// <summary>Sub 3 — COMPLETE a goal: fires the green-check "Goal Complete!" announce for the id.
/// GROUND TRUTH (04-01 capture idx 37163, the Frostfang win moment):
///   2D00 03 [62310000 = 12642] [00000000] [88130000 = 5000]
/// sent together with the op47/sub3 row-complete (TaskComplete) and the reward banners.</summary>
public class ObjectiveCompletePacket : ISerializablePacket
{
    public const byte SubOpCode = 3;

    public int ObjectiveId;
    public int Unknown;          // 0 on the live wire
    public int Unknown2 = 5000;  // 5000 on the live wire (announce display ms?)

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(ObjectivePacketWriter.OpCode);
        writer.Write(SubOpCode);
        writer.Write(ObjectiveId);
        writer.Write(Unknown);
        writer.Write(Unknown2);

        return writer.Buffer;
    }
}

/// <summary>Sub 2 — progress tick: sets the goal's current Count (e.g. wolves scared 3/12).
/// NOTE: the live Frostfang encounter never sends this — its goal (12642) is Total=1 and completes
/// in one shot at the win (sub 3). Kept for other encounter types.</summary>
public class ObjectiveUpdatePacket : ISerializablePacket
{
    public const byte SubOpCode = 2;

    public int ObjectiveId;
    public int Count;

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(ObjectivePacketWriter.OpCode);
        writer.Write(SubOpCode);
        writer.Write(ObjectiveId);
        writer.Write(Count);

        return writer.Buffer;
    }
}
