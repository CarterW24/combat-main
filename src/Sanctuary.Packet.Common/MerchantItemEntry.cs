using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class MerchantItemEntry : ISerializableType
{
    public int ItemId;
    public int Qty = -1;
    public int Cost;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(ItemId);
        writer.Write(Qty);
        writer.Write(Cost);
    }
}
