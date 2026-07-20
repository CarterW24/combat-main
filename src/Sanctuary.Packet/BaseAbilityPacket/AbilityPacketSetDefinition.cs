using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

// COMBAT WIP: BaseAbilityPacket (op36) sub-opcode 5 = AbilityPacketSetDefinition — populates a profile's
// ability toolbar.
//
// Wire format CONFIRMED from IDA (AbilitySet::SerializeForClient + Ability::sub_8E6760):
//   [op36][sub5][int ProfileId][int Count=8][Ability * 8]
// Ability (sub_8E6760): int Type; if Type!=0: (Type 1/3 -> int Unknown2, int ManaCost; Type 2 ->
//   int ItemDefinitionId) then int IconId, int NameId, int Unknown7, int Unknown8, int Unknown9,
//   int AbilityDefinitionId, int Unknown11, bool ForceDismount. Type 0 = empty slot (just the int).
// Reproducing the captured Ninja set (2 full + 6 empty) yields the exact 118-byte capture.
public class AbilityPacketSetDefinition : BaseAbilityPacket, ISerializablePacket
{
    public new const short OpCode = 5;

    public int ProfileId;

    public AbilitySet AbilitySet = new();

    public AbilityPacketSetDefinition() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ProfileId);

        AbilitySet.Serialize(writer);

        return writer.Buffer;
    }
}
