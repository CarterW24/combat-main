using System.Collections.Generic;
using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class InGamePurchaseMerchantListPacket : PacketBaseInGamePurchase, ISerializablePacket
{
    public new const short OpCode = 42;

    public ulong MerchantGuid;
    public List<int> BundleIds = [];

    public InGamePurchaseMerchantListPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(MerchantGuid);
        writer.Write(BundleIds);

        return writer.Buffer;
    }
}
