using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// ★ THE REAL GOALS-WINDOW FEED (RE'd 2026-07-03). The in-game top-right "Goals" tracker is
// Main.wndObjectives (minigame.lua ObjectiveWindow), bound to the C++ data source
// "BaseClient.ObjectiveHelper" (ObjectiveHelperDataSource, created in the UIProcessor ctor
// @0xA98020). It is NOT fed by op45 (minigame goal state) or the MiniGameGoals data source —
// those drive the LOBBY/ready panes (wndMinigameStatusObjPane) and the goal-complete announces.
//
// The ObjectiveHelper rows come exclusively from this BaseUiPacket (op47) family, dispatched in
// BaseClient::OnTunneledClientPacket2 case 47 -> UIProcessor::sub_A91BF0:
//   sub 1 = ADD/UPSERT row  -> sub_CB81E0: row key = ObjectiveId, text = StringProvider(NameId)
//           (server-known string id caveat), then fires the DS row-changed event -> Lua
//           BaseClient_ObjectiveHelper_OnDataChanged -> ObjectiveWindow:AddOrUpdateObjective ->
//           the window SHOWS ITSELF on its first row. MembersOnly + non-member client swaps the
//           text to string 9195 and icon state 4 (locked).
//   sub 3 = COMPLETE/REMOVE row by id -> sub_CB7F20.
//   sub 5 = CLEAR all rows -> sub_A89B60 (no payload; Lua ObjectiveWindow:Clear via OnDataUpdate).
//
// GROUND TRUTH (2014-04-01 capture): entry burst idx 28049/28069 =
//   2F00 01 [62310000=12642] [00000000] [F0960100=104176] [00] [00] [00000000] [00] [01000000]
// and completion idx 37165 = 2F00 03 [62310000]. No minigame state is required — this window
// works standalone (the Lua only gates on tutorial/pirates/disabled).
public class UiObjectiveAddPacket : BaseUiPacket, ISerializablePacket
{
    public new const byte OpCode = 1;

    public int ObjectiveId;
    public int Unknown2;
    public int NameId;          // row text (server-known string id!)
    public bool Unknown4;
    public bool MembersOnly;    // non-member client: text -> string 9195, icon state 4 (locked)
    public int Unknown6;
    public bool Unknown7;
    public int Unknown8 = 1;    // real capture always sends 1

    public UiObjectiveAddPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ObjectiveId);
        writer.Write(Unknown2);
        writer.Write(NameId);
        writer.Write(Unknown4);
        writer.Write(MembersOnly);
        writer.Write(Unknown6);
        writer.Write(Unknown7);
        writer.Write(Unknown8);

        return writer.Buffer;
    }
}

/// <summary>Sub 3 — complete/remove a Goals-window row by objective id.</summary>
public class UiObjectiveCompletePacket : BaseUiPacket, ISerializablePacket
{
    public new const byte OpCode = 3;

    public int ObjectiveId;

    public UiObjectiveCompletePacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ObjectiveId);

        return writer.Buffer;
    }
}

/// <summary>Sub 5 — clear every Goals-window row (no payload).</summary>
public class UiObjectiveClearPacket : BaseUiPacket, ISerializablePacket
{
    public new const byte OpCode = 5;

    public UiObjectiveClearPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        return writer.Buffer;
    }
}
