using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

using Sanctuary.Core.IO;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Chat;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.Game.Entities;

public sealed class Player : ClientPcData, IEntity
{
    private readonly UdpConnection _connection;
    private readonly IResourceManager _resourceManager;

    public bool Visible { get; set; }

    /// <summary>True once the login-only zone-in burst (Welcome screen etc.) has been sent this
    /// session. The overworld's OnClientIsReady runs on EVERY zone-in — including the return from a
    /// combat instance — and re-sending PacketLoadWelcomeScreen there re-opens the client's Welcome
    /// popup (Main.wndWelcomeHandler) ON TOP of the encounter's victory screen (live bug 2026-07-04).</summary>
    public bool LoginBurstSent { get; set; }

    /// <summary>LOOT WHEEL: the prize the victory wheel was told to land on (set when the encounter
    /// sends MiniGameLootWheelSetItemToLandOn; consumed by the C2S LootWheelOnRotationStopped handler,
    /// which grants it). Null = no spin pending. A null prize with PendingWheelCoins &gt; 0 = the
    /// COINS slice.</summary>
    public Sanctuary.Packet.RewardEntry? PendingWheelPrize { get; set; }
    public int PendingWheelCoins { get; set; }

    public IZone Zone { get; set; }
    public ZoneTile ZoneTile { get; private set; } = ZoneTile.Empty;
    public ConcurrentDictionary<ulong, Npc> VisibleNpcs { get; } = [];
    public ConcurrentDictionary<ulong, Player> VisiblePlayers { get; } = [];

    private int ZoneAreaId { get; set; }

    public int ChatBubbleForegroundColor { get; set; }
    public int ChatBubbleBackgroundColor { get; set; }
    public int ChatBubbleSize { get; set; }

    public ClientPcProfile ActiveProfile => Profiles.Single(x => x.Id == ActiveProfileId);

    public Mount? Mount { get; set; }

    public List<FriendData> Friends { get; set; } = [];
    public List<IgnoreData> Ignores { get; set; } = [];

    public ConcurrentDictionary<ChatChannel, bool> ChatChannelStatus { get; set; } = [];

    public int StationCash { get; set; }
    public List<CoinStoreTransactionRecord> CoinStoreTransactions { get; set; } = [];

    public Vector4 StartingZonePosition { get; set; }
    public Quaternion StartingZoneRotation { get; set; }

    public Player(BaseZone zone, UdpConnection connection, IResourceManager resourceManager)
    {
        Zone = zone;

        _connection = connection;
        _resourceManager = resourceManager;
    }

    #region Connection

    public void Send(ISerializablePacket packet)
    {
        var data = packet.Serialize();

        _connection.Send(UdpChannel.Reliable1, data);
    }

    public void SendToVisible(ISerializablePacket packet, bool sendToSelf = false)
    {
        var visiblePlayers = VisiblePlayers.ToFrozenDictionary();

        foreach (var visiblePlayer in visiblePlayers)
            visiblePlayer.Value.Send(packet);

        if (sendToSelf)
            Send(packet);
    }

    public void SendTunneled(ISerializablePacket packet)
    {
        var packetTunneled = new PacketTunneledClientPacket
        {
            Payload = packet.Serialize()
        };

        Send(packetTunneled);
    }

    [Obsolete]
    public void SendTunneled(byte[] buffer)
    {
        var packetTunneled = new PacketTunneledClientPacket
        {
            Payload = buffer
        };

        Send(packetTunneled);
    }

    public void SendTunneledToVisible(ISerializablePacket packet, bool sendToSelf = false)
    {
        var visiblePlayers = VisiblePlayers.ToFrozenDictionary();

        foreach (var visiblePlayer in visiblePlayers)
            visiblePlayer.Value.SendTunneled(packet);

        if (sendToSelf)
            SendTunneled(packet);
    }

    #endregion

    #region Update

    public void UpdateEveryTick()
    {
    }

    public void UpdateEverySecond()
    {
    }

    public void UpdatePosition(Vector4 position, Quaternion rotation)
    {
        Position = position;
        Rotation = rotation;

        if (Visible)
        {
            UpdateZoneTile();

            UpdateZoneArea();
        }
    }

    private void UpdateZoneTile()
    {
        var newZoneTile = Zone.GetTileFromPosition(Position);

        if (newZoneTile == ZoneTile)
            return;

        Zone.UpdateEntityZoneTile(this, ZoneTile, newZoneTile);

        ZoneTile = newZoneTile;
    }

    public void TeleportToZone(IZone zone, Vector4 position, Quaternion rotation)
    {
        // Preserve the original hardcoded values for existing (deep-mines test) callers.
        TeleportToZone(zone, position, rotation, "sky_deep_mines.xml", 214);
    }

    // INSTANCE (Frostfang Fury): overload with explicit sky/geometry so real zone transfers (e.g. the
    // sg_random_encounter_clearing arena) can use the destination world's own sky (null) instead of the
    // deep-mines test values. This is the PROPER server-side zone handoff — tiles/visibility rebuilt,
    // OverrideUpdateRadius=true (the client's case-31 handler feeds this to ActorManager::SetOverrideUpdateRadius;
    // without it NPCs in the new world get distance-culled -> the "invisible wolves" bug).
    public void TeleportToZone(IZone zone, Vector4 position, Quaternion rotation, string? sky, int geometryId)
    {
        if (Zone == zone)
            return;

        if (Zone is StartingZone)
        {
            StartingZonePosition = Position;
            StartingZoneRotation = Rotation;
        }

        if (Mount is not null)
            Mount.TeleportToZone(zone, position, rotation);

        // Alert/Remove visible entities
        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisiblePlayers([this]);

        OnRemoveVisibleNpcs(VisibleNpcs.Values);
        OnRemoveVisiblePlayers(VisiblePlayers.Values);

        ZoneTile.Entities.Remove(Guid, out _);

        Zone.TryRemovePlayer(Guid);

        // Add to new zone/zonetile

        zone.TryAddPlayer(this);

        // Teleport to new zone

        Visible = false;

        Zone = zone;

        ZoneTile = ZoneTile.Empty;

        UpdatePosition(position, rotation);

        var packetClientBeginZoning = new PacketClientBeginZoning
        {
            Name = Zone.Name,
            Position = position,
            Rotation = rotation,
            Sky = sky,
            Id = Zone.Id,
            GeometryId = geometryId,
            OverrideUpdateRadius = true
        };

        SendTunneled(packetClientBeginZoning);
    }

    private void UpdateZoneArea()
    {
        if (Zone is not StartingZone startingZone)
            return;

        var zoneAreaId = startingZone.GetZoneAreaId(Position);

        if (ZoneAreaId == zoneAreaId)
            return;

        ZoneAreaId = zoneAreaId;

        var packetPOIChangeMessage = new PacketPOIChangeMessage
        {
            ZoneId = zoneAreaId
        };

        SendTunneled(packetPOIChangeMessage);
    }

    public void UpdateCharacterStats(params CharacterStat[] characterStats)
    {
        var clientUpdatePacketUpdateStat = new ClientUpdatePacketUpdateStat
        {
            Guid = Guid
        };

        clientUpdatePacketUpdateStat.Stats.AddRange(characterStats);

        SendTunneled(clientUpdatePacketUpdateStat);

        foreach (var characterStat in characterStats)
        {
            Stats[characterStat.Id] = characterStat;

            if (characterStat.Id == CharacterStatId.MaxMovementSpeed)
            {
                var playerUpdatePacketExpectedSpeed = new PlayerUpdatePacketExpectedSpeed
                {
                    Guid = Guid,
                    ExpectedSpeed = characterStat.Float
                };

                SendTunneledToVisible(playerUpdatePacketExpectedSpeed);
            }
        }
    }

    #endregion

    #region Events

    public void OnAddVisibleNpcs(params IEnumerable<Npc> npcs)
    {
        foreach (var npc in npcs)
        {
            var playerUpdatePacketAddNpc = npc.GetAddNpcPacket();

            SendTunneled(playerUpdatePacketAddNpc);
        }

        /* var playerUpdatePacketNpcRelevance = new PlayerUpdatePacketNpcRelevance();

        foreach (var npc in npcs)
        {
            if (npc.CursorId == 0)
                continue;

            playerUpdatePacketNpcRelevance.Entries.Add(new PlayerUpdatePacketNpcRelevance.Entry
            {
                Guid = npc.Guid,
                HasCursor = true,
                CursorId = npc.CursorId,
                Unknown2 = false
            });
        }

        if (playerUpdatePacketNpcRelevance.Entries.Count > 0)
            SendTunneled(playerUpdatePacketNpcRelevance);

        var playerUpdatePacketAddNotifications = new PlayerUpdatePacketAddNotifications();

        foreach (var npc in npcs)
        {
            if (npc.Notification is null)
                continue;

            playerUpdatePacketAddNotifications.Notifications.Add(npc.Notification);

            SendTunneled(playerUpdatePacketAddNotifications);
        }

        foreach (var npc in npcs)
            VisibleNpcs.TryAdd(npc.Guid, npc); */
    }

    public void OnAddVisiblePlayers(params IEnumerable<Player> players)
    {
        foreach (var player in players)
        {
            var playerUpdatePacketAddPc = player.GetAddPcPacket();

            SendTunneled(playerUpdatePacketAddPc);
        }

        foreach (var player in players)
            VisiblePlayers.TryAdd(player.Guid, player);
    }

    public void OnRemoveVisibleNpcs(params IEnumerable<Npc> npcs)
    {
        foreach (var npc in npcs)
        {
            if (npc is Mount mount)
            {
                var playerUpdatePacketRemovePlayerGracefully = new PlayerUpdatePacketRemovePlayerGracefully();

                playerUpdatePacketRemovePlayerGracefully.Guid = npc.Guid;

                playerUpdatePacketRemovePlayerGracefully.Animate = false;
                playerUpdatePacketRemovePlayerGracefully.Delay = 0;
                playerUpdatePacketRemovePlayerGracefully.EffectDelay = 0;
                playerUpdatePacketRemovePlayerGracefully.CompositeEffectId = 46;
                playerUpdatePacketRemovePlayerGracefully.Duration = 1000;

                SendTunneled(playerUpdatePacketRemovePlayerGracefully);
            }
            else if (npc.GracefulRemoval is { } graceful)
            {
                // Live-server despawn (04-01 capture): the ONE graceful-remove packet carries the whole
                // death presentation — Animate=true plays the model's own death clip client-side, the
                // composite effect (5017 poof) fires and the actor despawns after Delay ms. No separate
                // SetAnimation / PlayCompositeEffect packets are needed (the real server sends none).
                var packet = new PlayerUpdatePacketRemovePlayerGracefully();

                packet.Guid = npc.Guid;

                packet.Animate = graceful.Animate;
                packet.Delay = graceful.Delay;
                packet.EffectDelay = graceful.EffectDelay;
                packet.CompositeEffectId = graceful.EffectId;
                packet.Duration = graceful.Duration;

                SendTunneled(packet);
            }
            else
            {
                var playerUpdatePacketRemovePlayer = new PlayerUpdatePacketRemovePlayer();

                playerUpdatePacketRemovePlayer.Guid = npc.Guid;

                SendTunneled(playerUpdatePacketRemovePlayer);
            }
        }

        foreach (var npc in npcs)
            VisibleNpcs.TryRemove(npc.Guid, out _);
    }

    public void OnRemoveVisiblePlayers(params IEnumerable<Player> players)
    {
        foreach (var player in players)
        {
            var playerUpdatePacketRemovePlayer = new PlayerUpdatePacketRemovePlayer();

            playerUpdatePacketRemovePlayer.Guid = player.Guid;

            SendTunneled(playerUpdatePacketRemovePlayer);
        }

        foreach (var player in players)
            VisiblePlayers.TryRemove(player.Guid, out _);
    }

    public void OnInteract(Player player)
    {
        var commandPacketInteractionList = new CommandPacketInteractionList();

        commandPacketInteractionList.List.Guid = Guid;

        commandPacketInteractionList.List.Interactions.Add(InspectInteraction.Data);

        if (Friends.Any(x => x.Guid == player.Guid))
        {
            commandPacketInteractionList.List.Interactions.Add(RemoveFriendInteraction.Data);
        }
        else
        {
            commandPacketInteractionList.List.Interactions.Add(AddFriendInteraction.Data);
        }

        if (player.Ignores.Any(x => x.Guid == Guid))
        {
            commandPacketInteractionList.List.Interactions.Add(StopIgnoringInteraction.Data);
        }
        else
        {
            commandPacketInteractionList.List.Interactions.Add(IgnoreInteraction.Data);
        }

        player.SendTunneled(commandPacketInteractionList);
    }

    #endregion

    public int GetFlairShardCompositeEffect()
    {
        const int FlairShardSlot = 13;

        if (ActiveProfile.Items.TryGetValue(FlairShardSlot, out var profileItem))
        {
            var clientItem = Items.FirstOrDefault(x => x.Id == profileItem.Id);

            if (clientItem is not null)
            {
                if (_resourceManager.ClientItemDefinitions.TryGetValue(clientItem.Definition, out var clientItemDefinition))
                    return clientItemDefinition.CompositeEffectId;
            }
        }

        return 0;
    }

    public List<CharacterAttachmentData> GetAttachments()
    {
        var list = new List<CharacterAttachmentData>();

        foreach (var profileItem in ActiveProfile.Items)
        {
            var attachment = GetAttachment(profileItem.Key);

            if (attachment is null)
                continue;

            list.Add(attachment);
        }

        return list;
    }

    // COMBAT WIP: the item-definition id of the weapon currently equipped in the weapon slot (7), or 0 if
    // none. Used to drive the ability toolbar off the equipped weapon (see Combat/NinjaWeaponAbilities).
    public int GetEquippedWeaponDefinitionId()
    {
        if (!ActiveProfile.Items.TryGetValue(7, out var profileItem))
            return 0;

        var clientItem = Items.FirstOrDefault(x => x.Id == profileItem.Id);

        return clientItem?.Definition ?? 0;
    }

    public CharacterAttachmentData? GetAttachment(int slot)
    {
        if (!ActiveProfile.Items.TryGetValue(slot, out var profileItem))
            return null;

        var clientItem = Items.FirstOrDefault(x => x.Id == profileItem.Id);

        if (clientItem is null)
            return null;

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(clientItem.Definition, out var clientItemDefinition))
            return null;

        var compositeEffectId = clientItemDefinition.CompositeEffectId;

        // Update the Weapon composite effect if we have a Flair Shard equipped.
        if (slot == 7)
        {
            var flairShardcompositeEffectId = GetFlairShardCompositeEffect();

            if (flairShardcompositeEffectId > 0)
                compositeEffectId = flairShardcompositeEffectId;
        }

        return new CharacterAttachmentData
        {
            ModelName = clientItemDefinition.ModelName,
            TextureAlias = clientItemDefinition.TextureAlias,
            TintAlias = clientItemDefinition.TintAlias,
            TintId = clientItem.Tint,
            CompositeEffectId = compositeEffectId,
            Slot = clientItemDefinition.Slot
        };
    }

    public PlayerUpdatePacketAddPc GetAddPcPacket()
    {
        var packet = new PlayerUpdatePacketAddPc
        {
            Guid = Guid,

            Name = Name,

            Model = Model,

            ChatBubbleForegroundColor = ChatBubbleForegroundColor,
            ChatBubbleBackgroundColor = ChatBubbleBackgroundColor,
            ChatBubbleSize = ChatBubbleSize,

            Position = Position,
            Rotation = Rotation,

            Attachments = GetAttachments(),

            Head = Head,
            Hair = Hair,

            HairColor = HairColor,
            EyeColor = EyeColor,

            SkinTone = SkinTone,

            FacePaint = FacePaint,
            ModelCustomization = ModelCustomization,

            MaxMovementSpeed = Stats[CharacterStatId.MaxMovementSpeed],

            IsUnderage = Age < 18,
            IsMember = MembershipStatus != 0,

            // playerUpdatePacketAddPc.TemporaryAppearance = 277;

            ActiveProfileId = ActiveProfileId,

            MountQueuePosition = -1,
            MountSeat = -1,
        };

        var activeTitle = Titles.FirstOrDefault(x => x.Id == ActiveTitle);

        if (activeTitle is not null)
            packet.Title = activeTitle;

        if (Mount is not null)
        {
            packet.MountGuid = Mount.Guid;
            packet.MountSeat = Mount.Seat;
            packet.MountQueuePosition = Mount.QueuePosition;

            packet.NameVerticalOffset = Mount.Definition.NameVerticalOffset;

            Debug.WriteLine($"AddPc: {Name} {Guid} | {Mount.Guid} {Mount.Seat} {Mount.QueuePosition}");
        }

        return packet;
    }

    #region Equatable

    public bool Equals(IEntity? other)
    {
        return Guid == other?.Guid;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is Player other)
            return Equals(other);

        return false;
    }

    public override int GetHashCode()
    {
        return Guid.GetHashCode();
    }

    public static bool operator ==(Player left, Player right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Player left, Player right)
    {
        return !(left == right);
    }

    #endregion

    public void Dispose()
    {
        foreach (var visiblePlayer in VisiblePlayers)
            visiblePlayer.Value.OnRemoveVisiblePlayers([this]);

        if (Mount is not null)
        {
            Mount.ZoneTile.Entities.Remove(Mount.Guid, out _);

            Zone.TryRemoveNpc(Mount.Guid);
            Mount = null;
        }

        ZoneTile.Entities.Remove(Guid, out _);

        Zone.TryRemovePlayer(Guid);
    }
}