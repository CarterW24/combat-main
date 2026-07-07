using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class ClearInteractionMerchantSetId : BaseCommandPacket, IDeserializable<ClearInteractionMerchantSetId>
{
    public new const short OpCode = 43;

    public ClearInteractionMerchantSetId() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClearInteractionMerchantSetId value)
    {
        value = new ClearInteractionMerchantSetId();

        var reader = new PacketReader(data);

        return value.TryRead(ref reader);
    }
}
