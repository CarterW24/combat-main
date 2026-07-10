using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Broadcasts an entity's current/max mana to visible players (OpCode 35, SubOpCode 9). Mirrors
/// <see cref="PlayerUpdatePacketUpdateHitpoints"/> for mana.
/// </summary>
public class PlayerUpdatePacketUpdateMana : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 9;

    public ulong Guid;
    public int CurrentMana;
    public int MaxMana;

    public PlayerUpdatePacketUpdateMana() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(CurrentMana);
        writer.Write(MaxMana);

        return writer.Buffer;
    }
}
