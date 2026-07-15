using System.Collections.Generic;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

// COMBAT WIP: BaseClientUpdatePacket (op 38) sub-opcode 15 (from server-files/more packets.xlsx).
// Server -> client: gives a profile its ability set. Payload = an AbilityExperienceSet: a run of
// AbilityExperience entries terminated by an entry whose Id (AbilityExperience.Unknown) == 0.
// Wire format CONFIRMED from AbilityExperienceSet::SerializeForClient (IDA decompile, 2026-06-20):
//   repeat { int Id(!=0); bool IsActivateable; int NameId; int DescriptionId; int IconId;
//            int Experience; int Rank; int RankExperience; int RankMaxExperience; int RequiredLevel }
//   then int 0 (terminator).
// (AbilityExperience.Serialize already matches this exactly.) Theory: sending this on job-swap makes the
// client request each ability's full definition (RequestAbilityDefinition, sub-op 12).
// ASSUMPTION being verified live: no profile-id/count prefix before the set (the reader starts at the set).
public class ClientUpdatePacketAddProfileAbilitySetApl : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 15;

    public List<AbilityExperience> AbilityExperiences = new();

    public ClientUpdatePacketAddProfileAbilitySetApl() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [BaseClientUpdatePacket.OpCode=38][SubOpCode=15]

        foreach (var abilityExperience in AbilityExperiences)
        {
            abilityExperience.Serialize(writer);

            if (abilityExperience.Present == 0)
                return writer.Buffer; // entry was the terminator
        }

        // explicit terminator (Id == 0)
        new AbilityExperience { Present = 0 }.Serialize(writer);

        return writer.Buffer;
    }
}
