using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Entities;

public class Pet : Npc
{
    public Player Owner { get; init; }
    public Resources.Definitions.PetDefinition Definition { get; init; }

    public int PetId { get; set; }

    public new string Name { get; set; } = string.Empty;

    public System.Numerics.Vector4 LastSentPosition { get; set; } = System.Numerics.Vector4.Zero;

    public System.Numerics.Vector4 OwnerLastPosition { get; set; } = System.Numerics.Vector4.Zero;

    public Pet(IZone zone, Player owner, PetDefinition definition) : base(zone)
    {
        Owner = owner;
        Definition = definition;
    }

    public override void TeleportToZone(IZone zone, Vector4 position, Quaternion rotation)
    {
        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisibleNpcs([this]);

        OnRemoveVisiblePlayers(VisiblePlayers.Values);

        ZoneTile.Entities.Remove(Guid, out _);

        Zone.TryRemoveNpc(Guid);

        zone.TryAddPet(this);

        Visible = false;

        Zone = zone;

        ZoneTile = ZoneTile.Empty;

        UpdatePosition(position, rotation);
    }

    public override PlayerUpdatePacketAddNpc GetAddNpcPacket()
    {
        var packet = base.GetAddNpcPacket();

        return packet;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
