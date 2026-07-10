using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Triggers the full-screen job level-up celebration UI (levelup_&lt;job&gt;.gfx / "JobLevelUp" client event).
/// OpCode 38 (ClientUpdate) / SubOpCode 15 - handled client-side by FUN_009392c0 case 0xf: it reads the
/// [38][15] header then a single length-prefixed payload (FUN_008bfc00), deserializes that payload into a
/// profile object (FUN_00921460) and reads the job's level/icon/name from it to drive the UI. The payload is
/// a serialized <see cref="Sanctuary.Packet.Common.ClientPcProfile"/> - the same blob ActivateProfile (38/21)
/// carries. There is no gate: a fully-consumed payload fires the UI unconditionally.
/// </summary>
public class ClientUpdatePacketJobLevelUp : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 15;

    /// <summary>Serialized <see cref="Sanctuary.Packet.Common.ClientPcProfile"/> of the job that levelled up.</summary>
    public byte[] Payload = Array.Empty<byte>();

    public ClientUpdatePacketJobLevelUp() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.WritePayload(Payload);

        return writer.Buffer;
    }
}
