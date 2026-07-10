using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// COMBAT WIP (reconstructed from client binary, IDA sub_A2ED30):
//   sub-opcode = 3 (CONFIRMED for the v1.910 client family).
//   Field ORDER below = constructor member-init order (best evidence; NOT yet confirmed to equal
//   serialize order). We verify it live via the "!cast" chat test command -> watch the client.
// Server -> client: begins an ability cast (cast bar + animation + composite effect). The success
// counterpart to AbilityPacketFailed (replaces the 3079 stub once resolution is implemented).
public class AbilityPacketStartCasting : BaseAbilityPacket, ISerializablePacket
{
    public new const short OpCode = 3;

    public ulong Unknown;            // m_llUnknown   (default = invalid GUID)
    public ulong Unknown2;           // m_llUnknown2  (default = invalid GUID)
    public int CompositeEffectId;    // m_nCompositeEffectId
    public int Animation = -1;       // m_nAnimation  (default -1)
    public int AbilityId;            // m_nAbilityId
    public float ActionTime;         // m_fActionTime (cast duration, seconds)
    public bool HasActionProgress;   // m_bHasActionProgress

    public AbilityPacketStartCasting() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        base.Write(writer);          // [BaseAbilityPacket.OpCode=36][SubOpCode=3]

        // PROVISIONAL ORDER — being verified live via the !cast command.
        writer.Write(Unknown);
        writer.Write(Unknown2);
        writer.Write(CompositeEffectId);
        writer.Write(Animation);
        writer.Write(AbilityId);
        writer.Write(ActionTime);
        writer.Write(HasActionProgress);

        return writer.Buffer;
    }
}
