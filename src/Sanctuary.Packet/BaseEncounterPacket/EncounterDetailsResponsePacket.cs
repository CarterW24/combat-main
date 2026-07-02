using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// INSTANCE WIP (Frostfang Fury): BaseEncounterPacket (op 41) sub-opcode 114 = "EncounterDetailsResponsePacket"
// â€” the S2C adventure OFFER POPUP (title / difficulty / description / prizes + GO! button).
//
// Wire format reverse-engineered from the client's Unserialize functions (IDA, 2026-06-24), top-down:
//   EncounterDetailsResponsePacket::Unserialize (sub_AA32D0):
//     BaseEncounter header (sub_8D6690 = op/sub + 2 ints)  [handled by BaseEncounterPacket.Write]
//     EncounterDetailsCommon                                (sub_A29120)
//     byte  flag
//     int32 Unknown
//     Set<StoreBundleId> = prizes-at-packet-level           (sub_9B0700; int32 count + ids)
//   EncounterDetailsCommon (sub_A29120):
//     int32 Unknown Â· int32 Unknown2
//     collection (sub_A27610; int32 count + elems)          [GAP_ member]
//     List<EncounterTeamData> (sub_A24660; int32 count + elems)
//     int32 Unknown3 Â· int32 TeleportEffectId
//     byte Unknown5 Â· byte Unknown6 Â· byte Tutorial
//     int32 Unknown8 Â· int32 RespawnTime
//     MiniGameInfo                                          (sub_9BDD70)
//     byte UNK0 Â· byte UNK1
//   MiniGameInfo (sub_9BDD70):
//     int32 NameId(title) Â· int32 IconId Â· int32 DescriptionId Â· int32 Difficulty Â· int32 ProfileType Â·
//     int32 Type Â· byte MembersOnly Â·
//     RewardBundleBase Ã—3 (reward / member / preview)       (sub_8E7930)
//     ObjectiveData[] (sub_9BC380; int32 count + elems) Â·
//     byte Ã—5 (U8..U12) Â· string U13 Â· int32 U14 Â· byte U15 Â· int32 PreselectedGameId Â·
//     byte Ã—4 (U16..U19) Â· int32 U20
//   RewardBundleBase (sub_8E7930):
//     byte Unknown Â· int32 Ã—9 (U2..U10) Â· int32 Ã—2 pairA Â· int32 Ã—2 pairB Â·
//     int32 U13 Â· int32 U14 Â· int32 entryCount Â· entryCountÃ—{int32 type + polymorphic entry} Â· int32 U15
//     (empty bundle = 69 fixed bytes, entryCount 0)
//
// This first cut sends the popup with the visible fields (NameId/Difficulty/DescriptionId/IconId) populated and
// every collection EMPTY (no teams/objectives/prizes/reward-entries) â€” enough to make the panel render. Prizes
// + objectives get layered in once the panel is confirmed.
public class EncounterDetailsResponsePacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 114;

    // --- the visible popup content (MiniGameInfo) ---
    public int NameId;            // title (locale string id)
    public int IconId = -1;       // dungeon emblem icon (-1 = none/default, matches the client ctor default)
    public int DescriptionId;     // description (locale string id)
    public int Difficulty;        // difficulty rating
    public int ProfileType;
    public int MiniGameType;
    public bool MembersOnly;

    // --- a couple of common fields worth exposing ---
    public int TeleportEffectId;
    public int RespawnTime;
    public bool Tutorial;

    public EncounterDetailsResponsePacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [op 41][sub 114][int Unknown][int Unknown2]

        // ===== EncounterDetailsCommon (sub_A29120) =====
        writer.Write(0);                 // Unknown
        writer.Write(0);                 // Unknown2
        writer.Write(0);                 // GAP_ collection count = 0 (empty)
        writer.Write(0);                 // EncounterTeamData list count = 0 (empty)
        writer.Write(0);                 // Unknown3
        writer.Write(TeleportEffectId);  // TeleportEffectId
        writer.Write(true);              // Unknown5 (byte) â€” ctor default 1; passed into the offer display
        writer.Write(false);             // Unknown6 (byte)
        writer.Write(Tutorial);          // Tutorial (byte)
        writer.Write(0);                 // Unknown8
        writer.Write(RespawnTime);       // RespawnTime

        // ----- MiniGameInfo (sub_9BDD70) -----
        writer.Write(NameId);            // title
        writer.Write(IconId);            // icon
        writer.Write(DescriptionId);     // description
        writer.Write(Difficulty);        // difficulty
        writer.Write(ProfileType);       // ProfileType
        writer.Write(MiniGameType);      // Type
        writer.Write(MembersOnly);       // MembersOnly (byte)
        WriteEmptyRewardBundle(writer);  // m_RewardBundleBase
        WriteEmptyRewardBundle(writer);  // m_RewardBundleBase_Member
        WriteEmptyRewardBundle(writer);  // m_RewardBundleBase_Preview
        writer.Write(0);                 // ObjectiveData array count = 0 (empty)
        writer.Write(true);              // U8  (ctor default 1)
        writer.Write(true);              // U9  (ctor default 1)
        writer.Write(true);              // U10 (ctor default 1)
        writer.Write(true);              // U11 (ctor default 1)
        writer.Write(true);              // U12 (ctor default 1)
        writer.Write((string?)null);     // U13 string (writes int32 0)
        writer.Write(1);                 // U14 (ctor default 1)
        writer.Write(true);              // U15 (ctor default 1)
        writer.Write(0);                 // PreselectedGameId
        writer.Write(false);             // U16
        writer.Write(false);             // U17
        writer.Write(false);             // U18
        writer.Write(false);             // U19
        writer.Write(0);                 // U20
        // ----- end MiniGameInfo -----

        writer.Write(false);             // EncounterDetailsCommon UNK0 (byte)
        writer.Write(true);              // EncounterDetailsCommon UNK1 (byte) â€” â˜… REQUIRED: client case 114
                                         // gates the whole popup on this (if(!UNK1) -> do nothing). ctor default 1.
        // ===== end EncounterDetailsCommon =====

        writer.Write(false);             // packet flag (byte)
        writer.Write(0);                 // packet Unknown (int32)
        writer.Write(0);                 // Set<StoreBundleId> count = 0 (no prizes yet)

        return writer.Buffer;
    }

    // RewardBundleBase with no entries â€” 69 fixed bytes (see header). All-zero is a valid empty bundle.
    private static void WriteEmptyRewardBundle(PacketWriter writer)
    {
        writer.Write(false);                              // byte Unknown
        for (var i = 0; i < 9; i++) writer.Write(0);      // int32 U2..U10
        writer.Write(0); writer.Write(0);                 // int32 pairA (x,y)
        writer.Write(0); writer.Write(0);                 // int32 pairB (x,y)
        writer.Write(0);                                  // int32 U13
        writer.Write(0);                                  // int32 U14
        writer.Write(0);                                  // int32 entryCount = 0
        writer.Write(0);                                  // int32 U15
    }
}
