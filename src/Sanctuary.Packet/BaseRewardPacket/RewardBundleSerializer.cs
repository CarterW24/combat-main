using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class RewardBundleItem
{
    public int IconId;

    public int NameId;

    public int Count = 1;
}

public static class RewardBundleSerializer
{
    public static void Write(PacketWriter writer, int coins, int experience, IReadOnlyList<RewardBundleItem>? items = null)
    {
        writer.Write(false);
        writer.Write(coins);
        writer.Write(experience);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0f);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        int count = items?.Count ?? 0;
        writer.Write(count);
        if (items is not null)
        {
            foreach (var item in items)
            {
                writer.Write(1);
                writer.Write((byte)0);
                writer.Write(item.IconId);
                writer.Write(0);
                writer.Write(item.NameId);
                writer.Write(item.Count);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write((byte)0);
            }
        }

        writer.Write(0);
    }
}
