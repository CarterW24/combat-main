using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class EncounterShowRespawnWindowPacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 125;

    public int RespawnTimeMs = 10000;

    public int ReviveHereCostRaw;

    public EncounterShowRespawnWindowPacket(int encounterId = 0, int instanceId = 0,
        int respawnTimeMs = 10000, int reviveHereCostRaw = 0) : base(OpCode)
    {
        Unknown = encounterId;
        Unknown2 = instanceId;
        RespawnTimeMs = respawnTimeMs;
        ReviveHereCostRaw = reviveHereCostRaw;
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(RespawnTimeMs);
        writer.Write(ReviveHereCostRaw);

        return writer.Buffer;
    }
}
