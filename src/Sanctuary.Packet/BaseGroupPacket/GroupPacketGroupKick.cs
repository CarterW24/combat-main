using System;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

// PARTY op40/sub6 GroupKick (C2S) — the "x" on ANOTHER member's portrait. Wire format CAPTURED live
// 2026-07-11 (68 bytes):
//   [short 40][short 6] header
//   ulong guid  — ZERO (the client does not send the target's guid)
//   28-byte block — zero (sender/group placeholders the client doesn't fill)
//   NameData    — the TARGET player's name (3 int32 name-ids + FirstName + LastName), starting at
//                 offset 40; the FirstName string length-prefix lands at offset 52 (same slot as the
//                 GroupInvite target name). This NAME is the only field identifying who to kick.
//   trailing zero padding — ignored.
// So the server must kick BY NAME, not by guid.
public class GroupPacketGroupKick : BaseGroupPacket, IDeserializable<GroupPacketGroupKick>
{
    public new const short OpCode = 6;

    // header(4) + guid(8) already read → skip this many bytes to reach the target NameData at offset 40.
    private const int ReservedBlockSize = 28;

    /// <summary>Target guid — present in the wire but the client sends 0; kept for completeness.</summary>
    public ulong TargetGuid;

    public string? TargetFirstName;
    public string? TargetLastName;

    public string TargetFullName =>
        string.IsNullOrEmpty(TargetLastName) ? (TargetFirstName ?? "")
        : $"{TargetFirstName} {TargetLastName}";

    public GroupPacketGroupKick() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out GroupPacketGroupKick value)
    {
        value = new GroupPacketGroupKick();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))              // [40][6]
            return false;

        if (!reader.TryRead(out value.TargetGuid))   // guid (0)
            return false;

        for (var i = 0; i < ReservedBlockSize; i++)  // skip to the NameData at offset 40
        {
            if (!reader.TryRead(out byte _))
                return false;
        }

        // NameData: 3 int32 name-ids, then FirstName + LastName (length-prefixed strings).
        for (var i = 0; i < 3; i++)
        {
            if (!reader.TryRead(out int _))
                return false;
        }

        if (!reader.TryRead(out value.TargetFirstName))
            return false;

        if (!reader.TryRead(out value.TargetLastName))
            return false;

        return true;
    }
}
