using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// PARTY op40/sub1 GroupInvite (C2S). Wire format CAPTURED live 2026-07-11 (97 bytes):
//   [short 40][short 1] header
//   48 bytes ZERO block — inviter/group/target guids + status, all zero (the client doesn't know
//     them at invite time; it only has the typed/clicked name)
//   [int32 len][name bytes] — the target player's name (length-prefixed, the actionable field)
//   28 trailing bytes — mostly zero, one per-packet-varying dword = uninitialized stack noise
// The client re-sends this ~6x/sec while the invite UI is up (like FreeInteractionNpc), so the
// handler debounces. Only the target NAME is meaningful to the server.
public class GroupPacketGroupInvite : BaseGroupPacket, IDeserializable<GroupPacketGroupInvite>
{
    public new const short OpCode = 1;

    /// <summary>Size of the all-zero guid/status block between the header and the name.</summary>
    private const int ZeroBlockSize = 48;

    public string? TargetName;

    public GroupPacketGroupInvite() : base(OpCode)
    {
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
