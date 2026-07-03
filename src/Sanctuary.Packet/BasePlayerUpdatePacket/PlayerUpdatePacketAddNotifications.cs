using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// OVERHEAD BADGE / NOTIFICATION (op35 sub10, RE'd 2026-07-02 from client 2014-03-13 + verified
// byte-exact against 5 live packets in logs/2014-03-25.pcap):
//   BaseClient::HandlePlayerUpdatePacket case 10 -> PlayerUpdatePacketAddNotifications
//   (reader sub_9213E0 -> NotificationInfo::sub_8DB310). For each entry the client attaches an
//   OverHeadBitmapElement ABOVE the character's head (offset 0,-0.9,0 — the minigame crossed-swords
//   badge over the Frostfang Growler in the reference video) + optional minimap indicator.
//   ImageId indexes Resources/NotificationImages.txt (layered art):
//     24  = tint-circle + circle + crossed swords (icon 1345) — the combat-encounter badge
//     117 = same + outer ring;  162 = bare crossed swords
//   NameId/SubTextId: string id shown with the indicator; SubTextId doubles as the
//   UiColorDefinitionManager tint id for the APPLY_TINT layer (unknown id -> white).
//   Live entries consistently end with Unknown10 = 1.
public class PlayerUpdatePacketAddNotifications : ISerializablePacket
{
    public const short OpCode = 35;
    public const short SubOpCode = 10;

    public List<NotificationData> Notifications = new();

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);
        writer.Write(SubOpCode);

        writer.Write(Notifications.Count);

        foreach (var notification in Notifications)
            notification.Serialize(writer);

        return writer.Buffer;
    }
}

public class NotificationData
{
    public ulong Guid;

    /// <summary>true = combat minimap indicator only (short wire form, no overhead icon fields).</summary>
    public bool IsCombat;

    public int Type = 1;

    /// <summary>If &gt; 0, stored on the client character (quest/encounter association id).</summary>
    public int Unknown3 = 1;

    /// <summary>NotificationImages.txt id (24 = crossed-swords combat-encounter badge).</summary>
    public int ImageId;

    public int DescriptionId;

    /// <summary>String id shown with the indicator — the live server sends the NPC's NameId here.</summary>
    public int NameId;

    /// <summary>Subtext string id / UiColorDefinition tint id for the badge's tintable layer.</summary>
    public int SubTextId;

    /// <summary>Set = suppress the overhead icon (minimap indicator only).</summary>
    public bool HideOverheadIcon;

    public int CompositeEffectId;

    public byte Unknown10 = 1; // constant 1 across all live samples

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Guid);
        writer.Write(IsCombat);
        writer.Write(Type);

        if (!IsCombat)
        {
            writer.Write(Unknown3);
            writer.Write(ImageId);
            writer.Write(DescriptionId);
            writer.Write(NameId);
            writer.Write(SubTextId);
            writer.Write(HideOverheadIcon);
            writer.Write(CompositeEffectId);
        }

        writer.Write(Unknown10);
    }
}
