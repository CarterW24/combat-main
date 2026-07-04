using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Entities;

public class Npc : IEntity
{
    public ulong Guid { get; init; }

    public Vector4 Position { get; private set; }
    public Quaternion Rotation { get; private set; }

    public bool Visible { get; set; }

    public IZone Zone { get; set; }
    public ZoneTile ZoneTile { get; protected set; } = ZoneTile.Empty;
    public ConcurrentDictionary<ulong, Npc> VisibleNpcs { get; } = [];
    public ConcurrentDictionary<ulong, Player> VisiblePlayers { get; } = [];

    public int NameId { get; set; }
    public string? Name { get; set; }

    /// <summary>Nameplate text color (AddNpc.NameColor; 0 = client default). Bosses in the reference
    /// video render RED names — first candidate mechanism alongside op32/sub9 EnableBossDisplay.</summary>
    public int NameColor { get; set; }
    public int SubTextNameId { get; set; }
    /// <summary>HIDE the overhead nameplate. LIVE-PROVEN 2026-07-03 (builds 12 vs 13): true hides,
    /// false shows — upstream's name is correct; the IDA "m_bShowNamePlate" annotation is wrong.</summary>
    public bool HideNamePlate { get; set; }
    public int NameplateImageId { get; set; }
    public float VerticalOffset { get; set; }

    /// <summary>Overhead name text SCALE. RE'd 2026-07-03: ProxiedCharacter::Process @0x973200 does
    /// `if (m_fNameScale != 0) Display_EliteNameScale = m_fNameScale` — so this AddNpc field directly
    /// sets the name text size. 0 = client default (~normal); &gt;1 = bigger letters (the video's boss).</summary>
    public float NameScale { get; set; }

    public int ModelId { get; set; }
    public int TerrainObjectId { get; set; }

    public string? TextureAlias { get; set; }
    public string? TintAlias { get; set; }
    public int TintId { get; set; }

    public float Scale { get; set; }

    /// <summary>
    /// 0 - Hostile
    /// 1 - Neutral
    /// 2 - Ally
    /// </summary>
    public int Disposition { get; set; } = 1;

    // COMBAT WIP: server-side health so abilities can damage/kill this NPC.
    // MaxHealth == 0 means "not damageable" (no health bar). See docs/STATUS.md.
    public int MaxHealth { get; set; }
    public int Health { get; set; }

    /// <summary>Render a nameplate health bar (maps to AddNpc.Unknown41).</summary>
    public bool ShowHealthBar { get; set; }

    public bool IsHostile => Disposition == 0;
    public bool IsDamageable => MaxHealth > 0;
    public bool IsAlive => !IsDamageable || Health > 0;

    /// <summary>Apply damage; returns true if this hit killed the NPC.</summary>
    public bool ApplyDamage(int amount)
    {
        if (!IsAlive)
            return false;

        Health -= amount;

        if (Health <= 0)
        {
            Health = 0;
            return true;
        }

        return false;
    }

    public int Animation { get; set; } = 1;

    // Locomotion animation group ids. -1 = "use the model's own clips" — the live 2014 server sends
    // -1 on EVERY NPC (370/370 AddNpc packets in the 2014-03-25 capture). 0 or a guessed id replaces
    // the model's run clip with an invalid one and the actor slides un-animated.
    public int WalkAnimId { get; set; } = -1;
    public int RunAnimId { get; set; } = -1;
    public int StandAnimId { get; set; } = -1;

    public int CompositeEffectId { get; set; }

    public int InteractRange { get; set; } = 100;
    public bool IsInteractable { get; set; } = true;

    // MOVEMENT (client OnPlayerUpdatePosition @0x90DE90, RE'd 2026-07-02): the client applies op125
    // position updates ONLY when the actor's MovementType is 1 (CONTROLLER: ClientMovementManager
    // interpolates to the sent position at ExpectedSpeed) or 2 (PHYSICS: network-player style with
    // gravity/fall states). Type 0 = static scenery — updates are parsed then silently DROPPED
    // (that was the "wolves frozen at spawn in the treetops" bug).
    public int MovementType { get; set; }

    /// <summary>Movement speed baked into AddNpc (feeds the client's ExpectedSpeed for this actor —
    /// at 0 a CONTROLLER/PHYSICS actor has no speed to move with).</summary>
    public float Speed { get; set; }

    /// <summary>Rider gate: OnPlayerUpdatePosition ignores actors whose rider != the invalid-guid
    /// sentinel (0xFFFFFFFFFFFFFFFF). Send the sentinel for AI NPCs ("no rider").</summary>
    public ulong RiderGuid { get; set; }

    // AddNpc bool #38. GROUND TRUTH (2014-03-25 capture): set to 1 on every red-name attackable camp
    // hostile (nameId 440711/440712, disp 0, nameColor FFFF0000) and 0 on every friendly — the
    // "render as enemy" status flag that goes with the red name.
    public bool EnemyStatus { get; set; }

    public int AreaDefinitionId { get; set; }

    public int ImageSetId { get; set; }

    public byte CursorId { get; set; }

    // public NotificationInfo? Notification { get; set; }

    public List<CharacterAttachmentData> Attachments { get; set; } = [];

    public bool Static { get; set; }

    public Npc(IZone zone)
    {
        Zone = zone;
    }

    #region Events

    public void OnInteract(Player player)
    {
    }

    public virtual void OnAddVisibleNpcs(params IEnumerable<Npc> npcs)
    {
        foreach (var npc in npcs)
            VisibleNpcs.TryAdd(npc.Guid, npc);
    }

    public virtual void OnAddVisiblePlayers(params IEnumerable<Player> players)
    {
        foreach (var player in players)
            VisiblePlayers.TryAdd(player.Guid, player);
    }

    public virtual void OnRemoveVisibleNpcs(params IEnumerable<Npc> npcs)
    {
        foreach (var npc in npcs)
            VisibleNpcs.TryRemove(npc.Guid, out _);
    }

    public virtual void OnRemoveVisiblePlayers(params IEnumerable<Player> players)
    {
        foreach (var player in players)
            VisiblePlayers.TryRemove(player.Guid, out _);
    }

    #endregion

    #region Update

    public virtual void UpdateEveryTick()
    {
    }

    public virtual void UpdateEverySecond()
    {
    }

    public void UpdatePosition(Vector4 position, Quaternion rotation)
    {
        Position = position;
        Rotation = rotation;

        if (Visible)
        {
            UpdateZoneTile();
        }
    }

    public virtual void TeleportToZone(IZone zone, Vector4 position, Quaternion rotation)
    {
    }

    protected void UpdateZoneTile()
    {
        var newZoneTile = Zone.GetTileFromPosition(Position);

        if (newZoneTile == ZoneTile)
            return;

        Zone.UpdateEntityZoneTile(this, ZoneTile, newZoneTile);

        ZoneTile = newZoneTile;
    }

    #endregion

    public virtual PlayerUpdatePacketAddNpc GetAddNpcPacket()
    {
        var packet = new PlayerUpdatePacketAddNpc
        {
            Guid = Guid,

            NameId = NameId,

            ModelId = ModelId,

            Unknown = default,

            TextureAlias = TextureAlias,
            TintAlias = TintAlias,

            TintId = TintId,

            Scale = Scale,

            Position = Position,
            Rotation = Rotation,

            Attachments = Attachments,
            HasAttachments = Attachments.Count > 0,

            Disposition = Disposition,

            Animation = Animation,

            Unknown16 = default,
            VerticalOffset = VerticalOffset,

            CompositeEffectId = CompositeEffectId,

            WieldType = default,

            Name = Name,

            HideNamePlate = HideNamePlate,

            Unknown22 = default,
            Unknown23 = default,
            Unknown24 = default,

            TerrainObjectId = TerrainObjectId,

            Speed = Speed,

            Unknown28 = default,

            InteractRange = InteractRange,

            WalkAnimId = WalkAnimId, // Walk GroupAnimId
            RunAnimId = RunAnimId, // Sprint GroupAnimId
            StandAnimId = StandAnimId, // Idle GroupAnimId

            Unknown33 = default,
            Unknown34 = default,

            SubTextNameId = SubTextNameId,

            Unknown36 = default, // AnimationEvent
            TemporaryAppearance = default,

            // playerUpdatePacketAddNpc.EffectTags = TODO

            Unknown38 = EnemyStatus,
            Unknown39 = default,
            Unknown40 = default,
            Unknown41 = ShowHealthBar, // Health bar
            Unknown42 = default,

            HasTilt = default,

            // playerUpdatePacketAddNpc.Customization = TODO

            Tilt = default,

            NameColor = NameColor,

            AreaDefinitionId = AreaDefinitionId,

            ImageSetId = ImageSetId,

            IsInteractable = IsInteractable,

            RiderGuid = RiderGuid,

            MovementType = MovementType,

            Unknown51 = default,

            Unknown52 = default,

            Unknown53 = default,

            Unknown54 = default,

            Unknown55 = default,

            Unknown56 = default,
            Unknown57 = default,
            Unknown58 = default,

            // playerUpdatePacketAddNpc.Head = TODO
            // playerUpdatePacketAddNpc.Hair = TODO
            // playerUpdatePacketAddNpc.ModelCustomization = TODO

            ReplaceTerrainObject = default,

            Unknown63 = default,
            Unknown64 = 3050,

            FlyByEffectId = default,

            ActiveProfile = default,

            Unknown67 = default,
            Unknown68 = default,

            NameScale = NameScale,

            NameplateImageId = NameplateImageId
        };

        return packet;
    }

    #region Equatable

    public bool Equals(IEntity? other)
    {
        return Guid == other?.Guid;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is Npc other)
            return Equals(other);

        return false;
    }

    public override int GetHashCode()
    {
        return Guid.GetHashCode();
    }

    public static bool operator ==(Npc left, Npc right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Npc left, Npc right)
    {
        return !(left == right);
    }

    #endregion

    public virtual void Dispose()
    {
        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisibleNpcs([this]);

        ZoneTile.Entities.Remove(Guid, out _);

        Zone.TryRemoveNpc(Guid);
    }
}