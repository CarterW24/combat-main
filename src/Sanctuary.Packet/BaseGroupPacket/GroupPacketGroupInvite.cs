using System;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

// PARTY op40/sub1 GroupInvite (C2S). Wire format CAPTURED live 2026-07-11 (97 bytes):
//   [short 40][short 1] header
//   48 bytes ZERO block — inviter/group/target guids + status, all zero (the client doesn't know
//     them at invite time; it only has the typed/clicked name)
//   [int32 len][name bytes] — the target player's name (length-prefixed, the actionable field)
//   28 trailing bytes — mostly zero, one per-packet-varying dword = uninitialized stack noise
// The client re-sends this ~6x/sec while the invite UI is up (like FreeInteractionNpc), so the
// handler debounces. Only the target NAME is meaningful to the server.
public class GroupPacketGroupInvite : BaseGroupPacket, IDeserializable<GroupPacketGroupInvite>, ISerializablePacket
{
    public new const short OpCode = 1;

    /// <summary>C2S only: the all-zero guid/status block the CLIENT sends before the target name
    /// (the C2S layout differs from the S2C popup layout — see Serialize).</summary>
    private const int ZeroBlockSize = 48;

    public string? TargetName;

    /// <summary>S2C: the inviter's NAME (a NameData — the "Group with &lt;name&gt;" popup label).</summary>
    public NameData? InviterName;

    /// <summary>S2C: the inviter's guid.</summary>
    public ulong InviterGuid;

    public GroupPacketGroupInvite() : base(OpCode)
    {
    }

    // ★ S2C GroupInvite — WIRE FORMAT CRACKED via Ghidra + Frida (2026-07-11). The invitee's client
    // reads (deserializer FUN_00912090 -> inviter reader FUN_008fca60 -> NameData reader FUN_008e7290):
    //   header(4) | guid1(8) | NameData1 | guid2(8) | NameData2 | int(4) | 5×int32(20)
    // NameData1 (right after guid1) IS the inviter's NAME shown in the "Group with <name>" popup — and
    // it's a full NameData (3 int32 name-ids + FirstName + LastName), NOT a plain string. Writing a
    // plain string here made the client read an empty name -> "Group with ?". guid1 = inviter guid;
    // guid2/NameData2/the 6 ints aren't needed for the popup (0/empty).
    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);                        // [short 40][short 1]
        writer.Write(InviterGuid);            // guid1 (8) — the inviter
        (InviterName ?? new NameData()).Serialize(writer); // NameData1 — THE INVITER NAME (popup label)
        writer.Write((ulong)0);               // guid2 (8) — group/target guid (unused for popup)
        new NameData().Serialize(writer);     // NameData2 (empty)
        writer.Write(0);                      // int
        for (var i = 0; i < 5; i++)           // 5 × int32
            writer.Write(0);

        return writer.Buffer;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out GroupPacketGroupInvite value)
    {
        value = new GroupPacketGroupInvite();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        // Skip the 48-byte zero block (client-unknown guids).
        for (var i = 0; i < ZeroBlockSize; i++)
        {
            if (!reader.TryRead(out byte _))
                return false;
        }

        if (!reader.TryRead(out value.TargetName))
            return false;

        // Trailing bytes (noise) are intentionally not consumed/validated.
        return true;
    }
}
