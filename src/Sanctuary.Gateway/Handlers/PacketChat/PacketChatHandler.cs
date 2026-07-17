using System;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;
using Sanctuary.Gateway.Commands;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketChatHandler
{
    private static ILogger _logger = null!;
    private static ILogger _chatLogger = null!;
    private static IZoneManager _zoneManager = null!;
    private static bool _commandRouterInitialized = false;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketChatHandler));
        _chatLogger = loggerFactory.CreateLogger("Chat");

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();

        if (!_commandRouterInitialized)
        {
            CommandRouter.Initialize(serviceProvider);
            _commandRouterInitialized = true;
        }

        var adminLogger = loggerFactory.CreateLogger("Admin");

        ChatCommandRegistry.Initialize(_zoneManager, _dbContextFactory, adminLogger);
    }

    private static void SendMuteNotice(GatewayConnection connection)
    {
        DateTimeOffset? mutedUntil = connection.Player.MutedUntil;

        var packet = new PacketChat
        {
            Channel = ChatChannel.System,
            FromName = connection.Player.Name,
            ToName = connection.Player.Name,
            Message = $"You are muted until {mutedUntil:u} and cannot send chat messages."
        };

        connection.Player.SendTunneled(packet);
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PacketChat.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketChat));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketChat), packet);
        _logger.LogInformation("Chat message from {Player}: {Message}", connection.Player.Name, packet.Message);

        if (CommandRouter.TryHandle(connection, packet.Message))
        {
            _logger.LogInformation("Command was handled by CommandRouter");
            return true;
        }

        if (packet.Message is { } chatMsg && chatMsg.StartsWith("!cast"))
        {
            HandleCastTest(connection, chatMsg);
            return true;
        }

        if (packet.Message is { } animMsg && animMsg.StartsWith("!anim"))
        {
            HandleAnimTest(connection, animMsg);
            return true;
        }

        if (packet.Message is { } fightMsg && fightMsg.StartsWith("!fight"))
        {
            var parts2 = fightMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var on = !(parts2.Length > 1 && parts2[1] == "0");
            connection.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = on });
            connection.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = on });
            _logger.LogInformation("!fight -> InWorldCombat + IsFighting = {on}", on);
            return true;
        }

        if (packet.Message is { } atkMsg && atkMsg.StartsWith("!atk"))
        {
            HandleAtkTest(connection, atkMsg);
            return true;
        }

        if (packet.Message is { } dmgMsg && dmgMsg.StartsWith("!dmg"))
        {
            HandleDmgTest(connection, dmgMsg);
            return true;
        }

        if (packet.Message is { } hpMsg && hpMsg.StartsWith("!hp"))
        {
            HandleHpTest(connection, hpMsg);
            return true;
        }

        if (packet.Message is { } chatMsg2 && chatMsg2.StartsWith("!slot"))
        {
            HandleSlotTest(connection, chatMsg2);
            return true;
        }

        if (packet.Message is { } giveMsg && giveMsg.StartsWith("!give"))
        {
            HandleGiveJobWeapons(connection);
            return true;
        }

        if (packet.Message is { } iconMsg && iconMsg.StartsWith("!ticon"))
        {
            HandleIconProbe(connection, iconMsg);
            return true;
        }

        if (packet.Message is { } homeMsg && (homeMsg.StartsWith("!home") || homeMsg.StartsWith("!spawn")))
        {
            HandleHome(connection);
            return true;
        }

        if (packet.Message is { } packMsg && packMsg.StartsWith("!pack"))
        {
            var parts = packMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = parts.Length > 1 && int.TryParse(parts[1], out var n) ? Math.Clamp(n, 1, 25) : 1;
            for (var i = 0; i < count; i++)
                BaseMiniGamePacketHandler.OpenMysteryPack(connection);
            return true;
        }

        if (packet.Message is { } ncMsg && ncMsg.StartsWith("!namecolor"))
        {
            var ncParts = ncMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var argb = unchecked((int)0xFFA020F0);
            if (ncParts.Length > 1 && uint.TryParse(ncParts[1].TrimStart('#'),
                    System.Globalization.NumberStyles.HexNumber, null, out var hex))
                argb = unchecked((int)hex);

            if (connection.Player.Zone is StartingZone ncZone)
            {
                ncZone.SpawnNameColorTestDummy(connection.Player, argb);
                _logger.LogInformation("!namecolor -> spawned test dummy with NameColor 0x{argb:X8}.", argb);
            }
            else
                _logger.LogWarning("!namecolor -> only works in the starting zone.");
            return true;
        }

        if (packet.Message is { } pufxMsg && pufxMsg.StartsWith("!pufx"))
        {
            var fxParts = pufxMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int? animId = fxParts.Length >= 4 && int.TryParse(fxParts[3], out var a) ? a : null;
            if (fxParts.Length >= 3 && int.TryParse(fxParts[2], out var fxId) &&
                FrostfangArenaZone.TrySetPowerupFx(fxParts[1].ToLowerInvariant(), fxId, animId))
                _logger.LogInformation("!pufx -> {kind} use-FX now composite {fx}, anim {anim}.",
                    fxParts[1], fxId, animId?.ToString() ?? "(unchanged)");
            else
                _logger.LogWarning("!pufx usage: !pufx <flame|quake|shield> <fxId> [animId]");
            return true;
        }

        // "!puspawn [flame|quake|shield|energy]" — a single named pickup just ahead, or the ring of
        // four with no arg. Real walk-over collection either way.
        if (packet.Message is { } puSpawnMsg && puSpawnMsg.StartsWith("!puspawn"))
        {
            if (connection.Player.Zone is BaseZone puZone)
            {
                var spawnParts = puSpawnMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var kind = spawnParts.Length > 1 ? spawnParts[1].ToLowerInvariant() : null;
                HeldPowerupProbe.SpawnPickups(puZone, connection.Player, _resourceManager, kind);
                _logger.LogInformation("!puspawn -> dropped {what}.", kind ?? "the 4 pickup models");
            }
            return true;
        }

        if (packet.Message is { } koMsg && koMsg.StartsWith("!ko"))
        {
            connection.Player.Knockout();
            _logger.LogInformation("!ko -> forced player knockout.");
            return true;
        }

        if (packet.Message is { } stMsg && stMsg.StartsWith("!status"))
        {
            HandleStatusTest(connection, stMsg);
            return true;
        }

        if (packet.Message is { } puMsg && puMsg.StartsWith("!pu"))
        {
            var puParts = puMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (puParts.Length != 2)
                _logger.LogWarning("!pu usage: !pu <flame|quake|shield|energy>  (also: !puspawn, !pufx)");
            else if (connection.Player.Zone is FrostfangArenaZone puArena
                ? puArena.GrantPowerup(connection.Player, puParts[1].ToLowerInvariant())
                : HeldPowerupProbe.Grant(connection.Player, puParts[1].ToLowerInvariant(), _resourceManager))
                _logger.LogInformation("!pu -> granted {kind} powerup.", puParts[1]);
            else
                _logger.LogWarning("!pu usage: !pu <flame|quake|shield|energy>");
            return true;
        }

        if (packet.Message is { } offerMsg && offerMsg.StartsWith("!offer"))
        {
            connection.SendTunneled(new EncounterDetailsResponsePacket
            {
                NameId = 93276,
                DescriptionId = 104171,
                Difficulty = 1,
                IconId = 1345,
            });
            _logger.LogInformation("!offer -> EncounterDetailsResponsePacket (Frostfang) sent.");
            return true;
        }

        if (packet.Message is { } readyMsg && readyMsg.StartsWith("!ready"))
        {
            connection.SendTunneled(new EncounterZoneIsReadyPacket());
            _logger.LogInformation("!ready -> EncounterZoneIsReadyPacket (sub107) sent.");
            return true;
        }

        if (packet.Message is { } arenaMsg && arenaMsg.StartsWith("!arena"))
        {
            var pos = connection.Player.Position;

            if (arenaMsg.Contains("set"))
            {
                Sanctuary.Game.Zones.FrostfangArenaZone.SpawnOverride = pos;
                _logger.LogInformation("!arena set -> arena spawn pinned at ({x}, {y}, {z}).", pos.X, pos.Y, pos.Z);
            }
            else
            {
                _logger.LogInformation("!arena -> player at ({x}, {y}, {z}) in zone {zone}.",
                    pos.X, pos.Y, pos.Z, connection.Player.Zone.Name);
            }
            return true;
        }

        if (packet.Message is { } ffMsg && ffMsg.StartsWith("!frostfang"))
        {
            HandleFrostfangTest(connection, ffMsg);
            return true;
        }

        if (packet.Message == null)
        {
            _logger.LogWarning("Received {name} packet with null message. ( {packet} )", nameof(PacketChat), packet);
            return false;
        }
        
        if (packet.Message.StartsWith("!admin"))
        {
            ChatCommandRegistry.HandleCommand(connection, packet.Message);
            return true;
        }

        if (connection.Player.IsMuted())
        {
            SendMuteNotice(connection);
            return true;
        }

        packet.FromGuid = connection.Player.Guid;
        packet.FromName = connection.Player.Name;

        switch (packet.Channel)
        {
            case ChatChannel.Tell:
                {
                    if (_zoneManager.TryGetPlayer(packet.ToName.FullName, out var toPlayer))
                    {
                        _chatLogger.LogInformation("Tell|From: \"{FromName}\" ({FromGuid}), To: \"{ToName}\" ({ToGuid}), Msg: \"{Message}\"",
                            packet.FromName,
                            packet.FromGuid,
                            packet.ToName,
                            toPlayer.Guid,
                            packet.Message
                        );

                        if (!toPlayer.Ignores.Any(x => x.Guid == connection.Player.Guid))
                            toPlayer.SendTunneled(packet);

                        var tellEchoPacket = new TellEchoPacket();

                        tellEchoPacket.Name = packet.ToName;
                        tellEchoPacket.Message = packet.Message;

                        connection.Player.SendTunneled(tellEchoPacket);
                    }
                }
                break;

            case ChatChannel.WorldShout:
                {
                    _chatLogger.LogInformation("WorldShout|From: \"{FromName}\" ({FromGuid}), Msg: \"{Message}\"",
                        packet.FromName,
                        packet.FromGuid,
                        packet.Message
                    );

                    foreach (var zonePlayer in connection.Player.Zone.Players)
                    {
                        if (zonePlayer.Ignores.Any(x => x.Guid == connection.Player.Guid))
                            continue;

                        zonePlayer.SendTunneled(packet);
                    }
                }
                break;

            case ChatChannel.WorldTrade:
            case ChatChannel.WorldLfg:
            case ChatChannel.WorldArea:
            case ChatChannel.WorldMembersOnly:
                {
                    _chatLogger.LogInformation("{Channel}|Area: {AreaNameId}, From: \"{FromName}\" ({FromGuid}), Msg: \"{Message}\"",
                        packet.Channel,
                        packet.AreaNameId,
                        packet.FromName,
                        packet.FromGuid,
                        packet.Message
                    );

                    connection.Player.SendTunneled(packet);

                    foreach (var visiblePlayer in connection.Player.VisiblePlayers)
                    {
                        if (visiblePlayer.Value.ChatChannelStatus.TryGetValue(packet.Channel, out var channelStatus) && !channelStatus)
                            continue;

                        if (visiblePlayer.Value.Ignores.Any(x => x.Guid == connection.Player.Guid))
                            continue;

                        visiblePlayer.Value.SendTunneled(packet);
                    }
                }
                break;

            default:
                {
                    _chatLogger.LogInformation("{Channel}|From: \"{FromName}\" ({FromGuid}), Msg: \"{Message}\"",
                        packet.Channel,
                        packet.FromName,
                        packet.FromGuid,
                        packet.Message
                    );

                    connection.Player.SendTunneled(packet);

                    foreach (var visiblePlayer in connection.Player.VisiblePlayers)
                    {
                        if (visiblePlayer.Value.Ignores.Any(x => x.Guid == connection.Player.Guid))
                            continue;

                        visiblePlayer.Value.SendTunneled(packet);
                    }
                }
                break;
        }

        return true;
    }

    private static void HandleStatusTest(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var player = connection.Player;

        if (parts.Length < 2)
        {
            _logger.LogWarning("!status usage: !status <stun|sleep|silence|root|fear|confuse|freeze|berserk|poison> [seconds] [dummy] — or !status clear");
            return;
        }

        if (parts[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            StatusEffects.ClearAll(player);
            _logger.LogInformation("!status -> cleared all statuses on the player.");
            return;
        }

        if (!StatusEffects.TryParse(parts[1], out var kind))
        {
            _logger.LogWarning("!status: unknown effect '{kind}'.", parts[1]);
            return;
        }

        var seconds = parts.Length > 2 && int.TryParse(parts[2], out var s) ? Math.Clamp(s, 1, 600) : 10;
        var onDummy = parts[^1].Equals("dummy", StringComparison.OrdinalIgnoreCase);

        if (onDummy)
        {
            Npc? nearest = null;
            var bestDistSq = 15f * 15f;
            if (player.Zone is BaseZone zone)
            {
                foreach (var npc in zone.Npcs)
                {
                    if (!npc.IsAlive)
                        continue;
                    var dx = npc.Position.X - player.Position.X;
                    var dz = npc.Position.Z - player.Position.Z;
                    var distSq = dx * dx + dz * dz;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        nearest = npc;
                    }
                }
            }

            if (nearest == null)
            {
                _logger.LogWarning("!status: no NPC within 15u to apply {kind} to.", kind);
                return;
            }

            StatusEffects.Apply(nearest, kind, seconds * 1000, source: player);
            _logger.LogInformation("!status -> {kind} on NPC {guid} ({model}) for {s}s.", kind, nearest.Guid, nearest.ModelId, seconds);
        }
        else
        {
            StatusEffects.Apply(player, kind, seconds * 1000, source: player);
            _logger.LogInformation("!status -> {kind} on the player for {s}s.", kind, seconds);
        }
    }

    private static void HandleCastTest(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var p = new AbilityPacketStartCasting
        {
            Unknown = connection.Player.Guid,   // best guess: caster guid
            Unknown2 = connection.Player.Guid,
            CompositeEffectId = 0,
            Animation = -1,
            AbilityId = 1,
            ActionTime = 5f,
            HasActionProgress = true,
        };

        if (parts.Length > 1 && ulong.TryParse(parts[1], out var u)) p.Unknown = u;
        if (parts.Length > 2 && ulong.TryParse(parts[2], out var u2)) p.Unknown2 = u2;
        if (parts.Length > 3 && int.TryParse(parts[3], out var ce)) p.CompositeEffectId = ce;
        if (parts.Length > 4 && int.TryParse(parts[4], out var an)) p.Animation = an;
        if (parts.Length > 5 && int.TryParse(parts[5], out var ab)) p.AbilityId = ab;
        if (parts.Length > 6 && float.TryParse(parts[6], out var at)) p.ActionTime = at;
        if (parts.Length > 7) p.HasActionProgress = parts[7] == "1" || parts[7].Equals("true", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation(
            "!cast -> StartCasting Unknown={u} Unknown2={u2} CompositeEffectId={ce} Animation={an} AbilityId={ab} ActionTime={at} HasActionProgress={h}",
            p.Unknown, p.Unknown2, p.CompositeEffectId, p.Animation, p.AbilityId, p.ActionTime, p.HasActionProgress);

        connection.SendTunneled(p);
    }

    private static void HandleAnimTest(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length <= 1 || parts[1] == "0" || parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            AbilityPacketClientRequestStartAbilityHandler.DebugAnimationOverride = null;
            _logger.LogInformation("!anim -> cleared animation override (abilities use their own anim again).");
            return;
        }

        if (!int.TryParse(parts[1], out var animId))
            return;

        AbilityPacketClientRequestStartAbilityHandler.DebugAnimationOverride = animId;
        _logger.LogInformation("!anim -> animation override = {anim}. Press an ability key (1/2) to play it.", animId);

        connection.SendTunneled(new AbilityPacketStartCasting
        {
            Unknown = connection.Player.Guid,
            Unknown2 = connection.Player.Guid,
            CompositeEffectId = 0,
            Animation = animId,
            AbilityId = 1,
            ActionTime = 0.1f,
            HasActionProgress = false,
        });
    }

    private static void HandleAtkTest(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var dmg = parts.Length > 1 && int.TryParse(parts[1], out var a) ? a : 250;
        var maxHp = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 5000;
        var fx = parts.Length > 3 && int.TryParse(parts[3], out var c) ? c : 7;
        var b1 = parts.Length > 4 && parts[4] == "1";
        var b2 = parts.Length > 5 && parts[5] == "1";
        var i4 = parts.Length > 6 && int.TryParse(parts[6], out var d) ? d : 0;
        var i5 = parts.Length > 7 && int.TryParse(parts[7], out var e) ? e : maxHp;

        var npc = connection.Player.Zone.Npcs.FirstOrDefault(n => n.IsHostile && n.IsDamageable);

        if (npc is null)
        {
            _logger.LogInformation("!atk -> no hostile NPC found.");
            return;
        }

        var p = new CombatPacketAttackProcessed
        {
            AttackerGuid = connection.Player.Guid,
            TargetGuid = npc.Guid,
            Damage = dmg,
            MaxHealth = maxHp,
            CompositeEffectId = fx,
            Bool1 = b1,
            Bool2 = b2,
            Int4 = i4,
            CurrentHealth = i5,
        };

        _logger.LogInformation("!atk -> AttackProcessed npc={guid} dmg={dmg} maxHp={max} fx={fx} b1={b1} b2={b2} i4={i4} i5={i5}",
            npc.Guid, dmg, maxHp, fx, b1, b2, i4, i5);

        connection.SendTunneled(p);
    }

    private static void HandleDmgTest(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var u2 = parts.Length > 1 && int.TryParse(parts[1], out var a) ? a : -50;
        var u3 = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
        var u4 = parts.Length > 3 && int.TryParse(parts[3], out var c) ? c : 0;
        var u1 = parts.Length > 4 && parts[4] == "1";
        var u5 = parts.Length > 5 && parts[5] == "1";

        var npc = connection.Player.Zone.Npcs.FirstOrDefault(n => n.IsHostile && n.IsDamageable);

        if (npc is null)
        {
            _logger.LogInformation("!dmg -> no hostile NPC found.");
            return;
        }

        var p = new PlayerUpdatePacketHitPointModification
        {
            Guid = connection.Player.Guid,
            Guid2 = npc.Guid,
            Unknown = u1,
            Unknown2 = u2,
            Unknown3 = u3,
            Unknown4 = u4,
            Unknown5 = u5,
        };

        _logger.LogInformation("!dmg -> HitPointModification npc={guid} u1={u1} u2={u2} u3={u3} u4={u4} u5={u5}",
            npc.Guid, u1, u2, u3, u4, u5);

        connection.SendTunneled(p);
    }

    private static void HandleHpTest(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var self = parts[0].Equals("!hpme", StringComparison.OrdinalIgnoreCase);

        var cur = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c : 100;
        var max = parts.Length > 2 && int.TryParse(parts[2], out var m) ? m : 5000;
        var u3 = parts.Length > 3 && int.TryParse(parts[3], out var u) ? u : 0;

        ulong targetGuid;

        if (self)
        {
            targetGuid = connection.Player.Guid;
        }
        else
        {
            var npc = connection.Player.Zone.Npcs.FirstOrDefault(n => n.IsHostile && n.IsDamageable);

            if (npc is null)
            {
                _logger.LogInformation("!hp -> no hostile NPC found.");
                return;
            }

            targetGuid = npc.Guid;
        }

        var p = new PlayerUpdatePacketUpdateHitpoints
        {
            Guid = targetGuid,
            Hitpoints = cur,
            MaxHitpoints = max,
            Unknown = u3,
        };

        _logger.LogInformation("!hp -> UpdateHitpoints Guid={guid} cur={cur} max={max} u3={u3} (self={self})",
            targetGuid, cur, max, u3, self);

        connection.SendTunneled(p);
    }

    private static void HandleIconProbe(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length <= 1)
        {
            NinjaWeaponAbilities.DebugMeleeIcon = null;
            NinjaWeaponAbilities.DebugSpecialIcon = null;
            _logger.LogInformation("!ticon -> cleared icon override (back to weapon icon).");
        }
        else
        {
            var melee = int.TryParse(parts[1], out var m) ? m : 0;
            var special = parts.Length > 2 && int.TryParse(parts[2], out var s) ? s : melee;

            NinjaWeaponAbilities.DebugMeleeIcon = melee;
            NinjaWeaponAbilities.DebugSpecialIcon = special;
            _logger.LogInformation("!ticon -> melee icon {melee}, special icon {special}.", melee, special);
        }

        connection.SendTunneled(NinjaWeaponAbilities.BuildToolbar(connection.Player, _resourceManager));
    }

    private static void HandleGiveJobWeapons(GatewayConnection connection)
    {
        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);

        using var dbContext = _dbContextFactory.CreateDbContext();

        var dbCharacter = dbContext.Characters
            .Include(x => x.Items)
            .SingleOrDefault(x => x.Id == characterId);

        if (dbCharacter is null)
        {
            _logger.LogWarning("!give -> character {id} not found.", characterId);
            return;
        }

        var weaponDefIds = connection.Player.ActiveProfileId == ArcherWeaponAbilities.ArcherProfileId
            ? ArcherWeaponAbilities.AllWeaponDefIds
            : NinjaWeaponAbilities.AllWeaponDefIds;

        var nextId = dbCharacter.Items.Count > 0 ? dbCharacter.Items.Max(i => i.Id) : 0;
        var granted = 0;

        foreach (var defId in weaponDefIds)
        {
            if (dbCharacter.Items.Any(i => i.Definition == defId))
                continue;

            if (!_resourceManager.ClientItemDefinitions.ContainsKey(defId))
                continue;

            var dbItem = new DbItem
            {
                Id = ++nextId,
                Definition = defId,
                Tint = 0,
                Count = 1,
            };

            dbCharacter.Items.Add(dbItem);

            var clientItem = new ClientItem
            {
                Id = dbItem.Id,
                Tint = 0,
                Count = 1,
                Definition = defId,
            };

            connection.Player.Items.Add(clientItem);

            using var writer = new PacketWriter();
            clientItem.Serialize(writer);

            connection.SendTunneled(new ClientUpdatePacketItemAdd { Payload = writer.Buffer });

            granted++;
        }

        if (granted > 0)
            dbContext.SaveChanges();

        _logger.LogInformation("!give -> granted {n} kit weapon(s) to character {id} (profile {profile}).",
            granted, characterId, connection.Player.ActiveProfileId);
    }

    private static void HandleHome(GatewayConnection connection)
    {
        var zone = _zoneManager.StartingZone;

        if (connection.Player.Zone != zone)
        {
            if (connection.Player.Zone is Sanctuary.Game.Zones.FrostfangArenaZone arena)
                arena.EndEncounterForPlayer(connection.Player);

            connection.Player.TeleportToZone(zone, zone.SpawnPosition, zone.SpawnRotation, sky: null, geometryId: 0);

            _logger.LogInformation("!home -> TeleportToZone back to {name} ({id}).", zone.Name, zone.Id);
            return;
        }

        connection.Player.UpdatePosition(zone.SpawnPosition, zone.SpawnRotation);

        connection.SendTunneled(new PacketClientBeginZoning
        {
            Name = zone.Name,
            Sky = null,
            Position = zone.SpawnPosition,
            Rotation = zone.SpawnRotation,
            Id = zone.Id,
            WaitForZoneReadyPacket = false,
        });

        _logger.LogInformation("!home -> ClientBeginZoning back to {name} ({id}) at spawn.", zone.Name, zone.Id);
    }

    private static void HandleFrostfangTest(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var x = parts.Length > 1 && float.TryParse(parts[1], out var px) ? px : 350f;
        var y = parts.Length > 2 && float.TryParse(parts[2], out var py) ? py : 100f;
        var z = parts.Length > 3 && float.TryParse(parts[3], out var pz) ? pz : 350f;
        var worldName = parts.Length > 4 ? parts[4] : "sh_frostfang_cavern";

        var beginZoning = new PacketClientBeginZoning
        {
            Name = worldName,
            Sky = null,
            Position = new Vector4(x, y, z, 1f),
            Rotation = Quaternion.Identity,
            Id = 59,
            WaitForZoneReadyPacket = false,
        };

        _logger.LogInformation("!frostfang -> ClientBeginZoning Name={name} pos=({x},{y},{z})", worldName, x, y, z);

        connection.SendTunneled(beginZoning);
    }

    private static void HandleSlotTest(GatewayConnection connection, string msg)
    {
        var parts = msg.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        var barId = parts.Length > 1 && int.TryParse(parts[1], out var bi) ? bi : 2;

        for (var slot = 0; slot < 4; slot++)
        {
            var p = new ClientUpdatePacketUpdateActionBarSlot();
            p.Data.Id = barId;
            p.Data.Slot = slot;
            p.Slot.IsEmpty = false;
            p.Slot.IconId = 543;
            p.Slot.IconTintId = 0;
            p.Slot.NameId = 422910;
            p.Slot.Unknown5 = 1;        // from capture
            p.Slot.Unknown6 = 2;        // from capture
            p.Slot.Unknown7 = 0;
            p.Slot.ManaCost = 0;
            p.Slot.Enabled = true;

            connection.SendTunneled(p);
        }

        _logger.LogInformation("!slot -> filled bar {bar} slots 0-3 (UpdateActionBarSlot).", barId);
    }
}
