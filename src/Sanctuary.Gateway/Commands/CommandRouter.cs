using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Chat;
using System.Linq;
using Sanctuary.Packet.Common;
using Sanctuary.Gateway.Handlers;

namespace Sanctuary.Gateway.Commands;

public static class CommandRouter
{
    private static ILogger _logger = null!;
    private static IZoneManager _zoneManager = null!;
    private static IResourceManager _resourceManager = null!;
    private static string _dbConnectionString = "Data Source=sanctuary.db";
    private static readonly HashSet<ulong> _flyingPlayers = [];

    public static void Initialize(IServiceProvider sp)
    {
        var lf = sp.GetRequiredService<ILoggerFactory>();
        _logger = lf.CreateLogger("Commands");
        _zoneManager = sp.GetRequiredService<IZoneManager>();
        _resourceManager = sp.GetRequiredService<IResourceManager>();

        try
        {
            var dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "sanctuary.db");
            _dbConnectionString = $"Data Source={dbPath}";
            _logger.LogInformation("CommandRouter using database: {DbPath}", dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine database path, using default");
        }
    }

    public static bool TryHandle(GatewayConnection conn, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        bool isCommand = message[0] == '/';
        if (!isCommand && !message.StartsWith("!"))
            return false;

        _logger.LogInformation("Command received: {Message} from {Player}", message, conn.Player.Name);

        var parts = message.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var verb = parts[0].Substring(1).ToLowerInvariant();

        if (verb is "combatdbg" or "dbg" || (verb == "combat" && parts.Length > 1 && parts[1].Equals("dbg", StringComparison.OrdinalIgnoreCase)))
        {
            Sanctuary.Game.Combat.CombatDebug.Verbose = !Sanctuary.Game.Combat.CombatDebug.Verbose;
            SendSystem(conn, $"combat debug {(Sanctuary.Game.Combat.CombatDebug.Verbose ? "ON" : "OFF")}");
            return true;
        }

        switch (verb)
        {
            case "help":
                return HandleHelp(conn);
            case "dungeon":
                return HandleDungeon(conn, parts);
            case "npc":
                return HandleNpc(conn, parts);
            case "admin":
                return HandleAdmin(conn, parts);
            case "enforcer":
                return HandleEnforcer(conn, parts);
            case "where":
                return HandleWhere(conn, parts);
            case "tp":
                return HandleTp(conn, parts);
            case "bring":
                return HandleBring(conn, parts);
            case "goto":
                return HandleGoto(conn, parts);
            case "kick":
                return HandleKick(conn, parts);
            case "warn":
                return HandleWarn(conn, parts);
            case "gift":
                return HandleGift(conn, parts);
            case "announce":
                return HandleAnnounce(conn, parts);
            case "listplayers":
                return HandlePlayers(conn, parts);
            case "gohouse":
                return HandleGoHouse(conn, parts);
            case "listhouses":
                return HandleListHouses(conn, parts);
            case "createhouse":
                return HandleCreateHouse(conn, parts);
            case "spawnhouse":
                return HandleSpawnHouse(conn, parts);
            case "testeffect":
                return HandleTestEffect(conn, parts);
            case "playeffect":
                return HandlePlayEffect(conn, parts);
            case "petspawn":
                return HandlePetSpawn(conn, parts);
            case "petdespawn":
                return HandlePetDespawn(conn, parts);
            case "petlist":
                return HandlePetList(conn, parts);
            case "respawn":
                return HandleRespawn(conn);
            case "die":
                return HandleDie(conn);
            case "dodge":
                return HandleDodge(conn, parts);
            case "spawnenemy":
                return HandleSpawnEnemy(conn, parts);
            case "hp":
                return HandleHp(conn, parts);
            case "xp":
                return HandleXp(conn, parts);
            case "testtransform":
                return HandleTestTransform(conn, parts);
            case "fly":
                return HandleFly(conn);
            case "testicons":
                return HandleTestIcons(conn);
            case "testsubtext":
                return HandleTestSubText(conn, parts);
            case "spawntest":
                return HandleSpawnTest(conn, parts);
            case "giveitem":
                return HandleGiveItem(conn, parts);
            case "lua":
                return HandleLua(conn, message);
            case "proj":
                return HandleProj(conn, parts);
            case "trail":
                return HandleTrail(conn, parts, 1);
            case "trails":
                return HandleTrailList(conn);

            case "paccept":
                BaseGroupPacketHandler.AcceptInvite(conn.Player);
                return true;
            case "pleave":
                BaseGroupPacketHandler.LeaveParty(conn.Player);
                return true;

            case "ptest":
                conn.Player.SendTunneled(new GroupPacketGroupInvite
                {
                    InviterGuid = conn.Player.Guid,
                    InviterName = new Sanctuary.Packet.Common.NameData { FirstName = "Test", LastName = "Inviter" },
                });
                SendSystem(conn, "!ptest -> sent a candidate S2C GroupInvite to you (watch Frida).");
                return true;

            case "proster":
                conn.Player.SendTunneled(new GroupPacketGroupUpdate
                {
                    LeaderGuid = conn.Player.Guid,
                    Members =
                    {
                        new GroupPacketGroupUpdate.Member { Guid = conn.Player.Guid, Name = conn.Player.Name, ProfileId = conn.Player.ActiveProfileId, ProfileRank = 1 },
                        new GroupPacketGroupUpdate.Member { Guid = conn.Player.Guid + 1, Name = new Sanctuary.Packet.Common.NameData { FirstName = "Party", LastName = "Member" }, ProfileId = conn.Player.ActiveProfileId, ProfileRank = 1 },
                    },
                });
                SendSystem(conn, "!proster -> sent a candidate S2C GroupUpdate (watch Frida).");
                return true;

            default:
                // NOT handled here — fall through so the legacy probe commands in
                // PacketChatHandler (!pufx, !pu, !puspawn, !status, !dmg, !give, !ko, ...) still
                // work. Returning true here silently killed the whole probe toolkit.
                return false;
        }
    }

    private static bool HandleDungeon(GatewayConnection conn, string[] parts)
    {
        var catalog = Sanctuary.Game.Dungeons.DungeonCatalog.ByActivity;
        if (parts.Length < 2 || !int.TryParse(parts[1], out var id) || !catalog.ContainsKey(id))
        {
            SendSystem(conn, "Usage: !dungeon <id>. Available:");
            foreach (var d in catalog.Values)
                SendSystem(conn, $"  {d.ActivityId} - {d.Comment}");
            return true;
        }
        EncounterParticipantRequestEntranceHandler.EnterEncounterArena(conn, id);
        SendSystem(conn, $"Entering dungeon {id} ({catalog[id].Comment})...");
        return true;
    }

    private static bool HandleHelp(GatewayConnection conn)
    {
        string helpText =
            "Available commands:\n" +
            "/help\n" +
            "/listplayers\n" +
            "/createhouse [HouseDefId] - Create a new house\n" +
            "/listhouses - List your houses\n" +
            "/gohouse [HouseId] - Enter a house\n" +
            "/petspawn [PetId] - Spawn a pet\n" +
            "/petdespawn - Despawn your active pet\n" +
            "/respawn - Revive after death\n" +
            "/hp - Check HP/Mana status\n" +
            "/hp full - Heal to full HP and Mana";

        if (IsEnforcer(conn))
        {
            helpText += "\n\nEnforcer commands:\n" +
                "/tp [PlayerName] - Teleport to player\n" +
                "/bring [PlayerName] - Bring player to you\n" +
                "/where [PlayerName] - Show player location\n" +
                "/kick [PlayerName] [reason] - Kick a player\n" +
                "/warn [PlayerName] [message] - Warn a player\n" +
                "/gift [PlayerName] [ItemId] [quantity] - Gift items\n" +
                "/enforcer list - List active enforcers";
        }

        if (IsAdmin(conn))
        {
            helpText += "\n\nAdmin commands:\n" +
                "/npc spawn [NameId] [ModelId] [TextureAlias]\n" +
                "/goto x y z\n" +
                "/announce [Message]\n" +
                "/admin list\n" +
                "/spawnhouse [ModelId] - Test house models\n" +
                "/testeffect [effectId] [modelId] [animId] [standAnimId]\n" +
                "/spawnenemy [ModelId] [Level] [Name] - Spawn a combat NPC";
        }

        SendSystem(conn, helpText);
        return true;
    }

    private static bool RequireAdmin(GatewayConnection conn)
    {
        if (!IsAdmin(conn))
        {
            SendSystem(conn, "You do not have permission to use this command.");
            return false;
        }
        return true;
    }

    private static bool IsAdmin(GatewayConnection conn)
    {
        long characterId = (long)conn.Player.CharacterId;

        try
        {
            _logger.LogInformation("Checking admin status for character ID: {CharId}, DB: {DbConn}", characterId, _dbConnectionString);

            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
            SELECT u.IsAdmin
            FROM Users u
            JOIN Characters c ON c.UserId = u.Id
            WHERE c.Id = $charId
            LIMIT 1;
        ";

            cmd.Parameters.AddWithValue("$charId", characterId);

            var result = cmd.ExecuteScalar();

            _logger.LogInformation("Admin check result for char {CharId}: {Result}", characterId, result);

            if (result == null || result is DBNull)
            {
                _logger.LogWarning("No admin result found for character {CharId}", characterId);
                return false;
            }

            bool isAdmin = Convert.ToInt32(result) == 1;
            _logger.LogInformation("Character {CharId} admin status: {IsAdmin}", characterId, isAdmin);
            return isAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking admin status for character {CharId}", characterId);
            return false;
        }
    }

    private static long? GetUserGuid(GatewayConnection conn)
    {
        long characterId = (long)conn.Player.CharacterId;

        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
            SELECT c.UserId
            FROM Characters c
            WHERE c.Id = $charId
            LIMIT 1;
        ";
            cmd.Parameters.AddWithValue("$charId", characterId);

            var result = cmd.ExecuteScalar();
            if (result == null || result is DBNull)
                return null;

            return Convert.ToInt64(result);
        }
        catch
        {
            return null;
        }
    }

    private static bool RequireOwnerForAdminManagement(GatewayConnection conn)
    {
        var userGuid = GetUserGuid(conn);

        if (userGuid != 1)
        {
            SendSystem(conn, "Only the server owner can manage admins.");
            return false;
        }

        return true;
    }

    private static bool HandleNpc(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /npc spawn <NameId> <ModelId> [TextureAlias]");
            return true;
        }

        var sub = parts[1].ToLowerInvariant();
        return sub switch
        {
            "spawn" => HandleNpcSpawn(conn, parts),
            _ => UnknownSubCommand(conn, "npc", sub)
        };
    }

    private static bool HandleNpcSpawn(GatewayConnection conn, string[] parts)
    {
        if (parts.Length < 4)
        {
            SendSystem(conn, "Usage: /npc spawn <NameId> <ModelId> [TextureAlias]");
            return true;
        }

        if (!int.TryParse(parts[2], out var nameId) ||
            !int.TryParse(parts[3], out var modelId))
        {
            SendSystem(conn, "Usage: /npc spawn <NameId> <ModelId> [TextureAlias]");
            return true;
        }

        string? texture = parts.Length >= 5 ? parts[4] : null;

        var zone = conn.Player.Zone;
        if (zone == null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        if (!zone.TryCreateNpc(out var npc) || npc is null)
        {
            SendSystem(conn, "Failed to create NPC.");
            return true;
        }

        npc.NameId = nameId;
        npc.ModelId = modelId;
        npc.TextureAlias = texture;
        npc.Scale = 1f;
        npc.Visible = true;

        npc.UpdatePosition(conn.Player.Position, conn.Player.Rotation);

        var tile = zone.GetTileFromPosition(conn.Player.Position);
        tile.Entities.TryAdd(npc.Guid, npc);

        SendSystem(conn, $"NPC spawned (Guid={npc.Guid}, NameId={nameId}, ModelId={modelId}).");
        return true;
    }

    private static bool HandleAdmin(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /admin <add|remove|list> [...]");
            return true;
        }

        var sub = parts[1].ToLowerInvariant();

        return sub switch
        {
            "add" => HandleAdminAdd(conn, parts),
            "remove" => HandleAdminRemove(conn, parts),
            "list" => HandleAdminList(conn),
            _ => UnknownSubCommand(conn, "admin", sub)
        };
    }

    private static bool HandleAdminAdd(GatewayConnection conn, string[] parts)
    {
        if (!RequireOwnerForAdminManagement(conn))
            return true;

        if (parts.Length < 3)
        {
            SendSystem(conn, "Usage: /admin add <Username>");
            return true;
        }

        string pattern = string.Join(' ', parts, 2, parts.Length - 2);

        if (!TryResolveUsernamePattern(pattern, out var resolvedUsername, out var error))
        {
            SendSystem(conn, error);
            return true;
        }

        int rows = ExecuteNonQuery(
            "UPDATE Users SET IsAdmin = 1 WHERE Username = $u;",
            ("$u", resolvedUsername));

        if (rows > 0)
            SendSystem(conn, $"User '{resolvedUsername}' is now an admin.");
        else
            SendSystem(conn, $"User '{resolvedUsername}' not found.");

        return true;
    }

    private static bool HandleAdminRemove(GatewayConnection conn, string[] parts)
    {
        if (!RequireOwnerForAdminManagement(conn))
            return true;

        if (parts.Length < 3)
        {
            SendSystem(conn, "Usage: /admin remove <Username>");
            return true;
        }

        string pattern = string.Join(' ', parts, 2, parts.Length - 2);

        if (!TryResolveUsernamePattern(pattern, out var resolvedUsername, out var error))
        {
            SendSystem(conn, error);
            return true;
        }

        int rows = ExecuteNonQuery(
            "UPDATE Users SET IsAdmin = 0 WHERE Username = $u;",
            ("$u", resolvedUsername));

        if (rows > 0)
            SendSystem(conn, $"User '{resolvedUsername}' is no longer an admin.");
        else
            SendSystem(conn, $"User '{resolvedUsername}' not found.");

        return true;
    }

    private static bool HandleAdminList(GatewayConnection conn)
    {
        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT Username FROM Users WHERE IsAdmin = 1 ORDER BY Username;";

            using var reader = cmd.ExecuteReader();

            var list = new List<string>();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }

            if (list.Count == 0)
            {
                SendSystem(conn, "No admins configured.");
            }
            else
            {
                SendSystem(conn, "Admins: " + string.Join(", ", list));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list admins.");
            SendSystem(conn, "Error listing admins.");
        }

        return true;
    }

    private static bool IsEnforcer(GatewayConnection conn)
    {
        return IsAdmin(conn);
    }

    private static bool IsPlayerAdmin(Player player)
    {
        long characterId = (long)player.CharacterId;

        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT u.IsAdmin
                FROM Users u
                JOIN Characters c ON c.UserId = u.Id
                WHERE c.Id = $charId
                LIMIT 1;
            ";

            cmd.Parameters.AddWithValue("$charId", characterId);

            var result = cmd.ExecuteScalar();

            if (result == null || result is DBNull)
                return false;

            return Convert.ToInt32(result) == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check admin status for player {Player}", player.Name.FullName);
            return false;
        }
    }

    private static bool RequireEnforcer(GatewayConnection conn)
    {
        if (!IsEnforcer(conn))
        {
            SendSystem(conn, "You must be a Referee (admin) to use this command.");
            return false;
        }
        return true;
    }

    private static bool HandleEnforcer(GatewayConnection conn, string[] parts)
    {
        if (!RequireOwnerForAdminManagement(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /enforcer <add|remove|list> [Username]");
            return true;
        }

        var sub = parts[1].ToLowerInvariant();

        return sub switch
        {
            "list" => HandleEnforcerList(conn),
            _ => UnknownSubCommand(conn, "enforcer", sub)
        };
    }

    private static bool HandleEnforcerList(GatewayConnection conn)
    {
        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT u.Username
                FROM Users u
                WHERE u.IsAdmin = 1
                ORDER BY u.Username;
            ";

            using var reader = cmd.ExecuteReader();

            var list = new List<string>();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }

            if (list.Count == 0)
            {
                SendSystem(conn, "No Referees (admins) configured.");
            }
            else
            {
                SendSystem(conn, "Referees: " + string.Join(", ", list));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list referees.");
            SendSystem(conn, "Error listing referees.");
        }

        return true;
    }

    private static bool HandleKick(GatewayConnection conn, string[] parts)
    {
        if (!RequireEnforcer(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /kick <PlayerName> [reason]");
            return true;
        }

        string pattern = parts[1];
        string reason = parts.Length > 2 ? string.Join(' ', parts, 2, parts.Length - 2) : "Kicked by an Enforcer";

        if (!TryResolvePlayerNamePattern(pattern, out var resolvedName, out var error))
        {
            SendSystem(conn, error);
            return true;
        }

        if (!_zoneManager.TryGetPlayer(resolvedName, out var target))
        {
            SendSystem(conn, $"Player '{resolvedName}' not found.");
            return true;
        }

        if (IsPlayerAdmin(target))
        {
            SendSystem(conn, "You cannot kick other admins/Referees.");
            return true;
        }

        _logger.LogWarning("Player {Player} kicked by Referee {Referee}. Reason: {Reason}",
            target.Name.FullName, conn.Player.Name.FullName, reason);

        SendMessageToPlayer(target, $"You have been kicked from the server. Reason: {reason}");

        System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
        {
            target.Disconnect();
        });

        SendSystem(conn, $"Kicked {target.Name.FullName}. Reason: {reason}");
        return true;
    }

    private static bool HandleWarn(GatewayConnection conn, string[] parts)
    {
        if (!RequireEnforcer(conn))
            return true;

        if (parts.Length < 3)
        {
            SendSystem(conn, "Usage: /warn <PlayerName> <message>");
            return true;
        }

        string pattern = parts[1];
        string message = string.Join(' ', parts, 2, parts.Length - 2);

        if (!TryResolvePlayerNamePattern(pattern, out var resolvedName, out var error))
        {
            SendSystem(conn, error);
            return true;
        }

        if (!_zoneManager.TryGetPlayer(resolvedName, out var target))
        {
            SendSystem(conn, $"Player '{resolvedName}' not found.");
            return true;
        }

        _logger.LogInformation("Player {Player} warned by Referee {Referee}. Message: {Message}",
            target.Name.FullName, conn.Player.Name.FullName, message);

        SendMessageToPlayer(target, $"[REFEREE WARNING] {message}");
        SendSystem(conn, $"Warning sent to {target.Name.FullName}");
        return true;
    }

    private static bool HandleGift(GatewayConnection conn, string[] parts)
    {
        if (!RequireEnforcer(conn))
            return true;

        if (parts.Length < 3)
        {
            SendSystem(conn, "Usage: /gift <PlayerName> <ItemId> [quantity]");
            return true;
        }

        string pattern = parts[1];

        if (!int.TryParse(parts[2], out var itemId))
        {
            SendSystem(conn, "ItemId must be a number.");
            return true;
        }

        int quantity = 1;
        if (parts.Length >= 4 && !int.TryParse(parts[3], out quantity))
        {
            SendSystem(conn, "Quantity must be a number.");
            return true;
        }

        if (!TryResolvePlayerNamePattern(pattern, out var resolvedName, out var error))
        {
            SendSystem(conn, error);
            return true;
        }

        if (!_zoneManager.TryGetPlayer(resolvedName, out var target))
        {
            SendSystem(conn, $"Player '{resolvedName}' not found.");
            return true;
        }

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemId, out var itemDef))
        {
            SendSystem(conn, $"Item {itemId} not found in item definitions.");
            return true;
        }

        _logger.LogInformation("Referee {Referee} gifted {Quantity}x Item {ItemId} to {Player}",
            conn.Player.Name.FullName, quantity, itemId, target.Name.FullName);

        SendMessageToPlayer(target, $"[GIFT] A Referee has gifted you {quantity}x {itemDef.NameId}!");
        SendSystem(conn, $"Gifted {quantity}x Item {itemId} to {target.Name.FullName}");
        SendSystem(conn, "Note: Inventory system not yet implemented - item not actually added.");

        return true;
    }

    private static bool HandleWhere(GatewayConnection conn, string[] parts)
    {
        if (!RequireEnforcer(conn))
            return true;

        var zone = conn.Player.Zone;
        if (zone == null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        var target = conn.Player;

        if (parts.Length >= 2)
        {
            string pattern = string.Join(' ', parts, 1, parts.Length - 1);

            if (!TryResolvePlayerNamePattern(pattern, out var resolvedName, out var error))
            {
                SendSystem(conn, error);
                return true;
            }

            if (!_zoneManager.TryGetPlayer(resolvedName, out var found))
            {
                SendSystem(conn, $"Player '{resolvedName}' not found (after resolving pattern).");
                return true;
            }

            target = found;
            zone = target.Zone ?? zone;
        }

        var pos = target.Position;
        SendSystem(conn, $"{target.Name.FullName} is at ({pos.X:0.0}, {pos.Y:0.0}, {pos.Z:0.0}) in zone {zone.Id}.");
        return true;
    }

    private static bool HandleTp(GatewayConnection conn, string[] parts)
    {
        if (!RequireEnforcer(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /tp <PlayerName>");
            return true;
        }

        string pattern = string.Join(' ', parts, 1, parts.Length - 1);

        if (!TryResolvePlayerNamePattern(pattern, out var resolvedName, out var error))
        {
            SendSystem(conn, error);
            return true;
        }

        if (!_zoneManager.TryGetPlayer(resolvedName, out var target))
        {
            SendSystem(conn, $"Player '{resolvedName}' not found (after resolving pattern).");
            return true;
        }

        var targetZone = target.Zone;
        if (targetZone == null)
        {
            SendSystem(conn, $"Player '{resolvedName}' is not in a valid zone.");
            return true;
        }

        conn.Player.TeleportToZone(targetZone, target.Position, target.Rotation);

        SendSystem(conn, $"Teleported to {target.Name.FullName}.");
        return true;
    }

    private static bool HandleBring(GatewayConnection conn, string[] parts)
    {
        if (!RequireEnforcer(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /bring <PlayerName>");
            return true;
        }

        string pattern = string.Join(' ', parts, 1, parts.Length - 1);

        if (!TryResolvePlayerNamePattern(pattern, out var resolvedName, out var error))
        {
            SendSystem(conn, error);
            return true;
        }

        if (!_zoneManager.TryGetPlayer(resolvedName, out var target))
        {
            SendSystem(conn, $"Player '{resolvedName}' not found (after resolving pattern).");
            return true;
        }

        var zone = conn.Player.Zone;
        if (zone == null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        target.TeleportToZone(zone, conn.Player.Position, conn.Player.Rotation);

        SendSystem(conn, $"Brought {target.Name.FullName} to your position.");
        return true;
    }

    private static bool HandleGoto(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 4)
        {
            SendSystem(conn, "Usage: /goto <x> <y> <z>");
            return true;
        }

        if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ||
            !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
        {
            SendSystem(conn, "Usage: /goto <x> <y> <z>");
            return true;
        }

        var zone = conn.Player.Zone;
        if (zone == null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        var newPos = new System.Numerics.Vector4(x, y, z, 1);
        var rot = conn.Player.Rotation;

        conn.Player.TeleportToZone(zone, newPos, rot);

        SendSystem(conn, $"Teleported to ({x:0.0}, {y:0.0}, {z:0.0}) in zone {zone.Id}.");
        return true;
    }

    private static bool HandleAnnounce(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /announce <message>");
            return true;
        }

        string msg = string.Join(" ", parts, 1, parts.Length - 1);

        var chatPacket = new PacketChat
        {
            Channel = ChatChannel.System,
            FromGuid = 0,
            FromName = new NameData(),
            Message = "[ANNOUNCEMENT] " + msg
        };

        int sentCount = 0;

        foreach (var player in _zoneManager.StartingZone.Players)
        {
            player.SendTunneled(chatPacket);
            sentCount++;
        }

        SendSystem(conn, $"Announcement sent to {sentCount} player(s).");
        return true;
    }

    private static bool HandlePlayers(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        var list = new List<string>();

        foreach (var p in _zoneManager.StartingZone.Players)
        {
            list.Add($"{p.Guid} — {p.Name.FullName}");
        }

        if (list.Count == 0)
        {
            SendSystem(conn, "No players online.");
            return true;
        }

        string msg = "Online players:\n" + string.Join("\n", list);

        SendSystem(conn, msg);
        return true;
    }

    private static bool HandleCreateHouse(GatewayConnection conn, string[] parts)
    {
        int houseDefId = 1;

        if (parts.Length >= 2 && int.TryParse(parts[1], out var customDefId))
        {
            houseDefId = customDefId;
        }

        if (!_resourceManager.Houses.TryGetValue(houseDefId, out var houseDef))
        {
            var availableIds = string.Join(", ", _resourceManager.Houses.Keys.OrderBy(k => k).Take(10));
            SendSystem(conn, $"House definition {houseDefId} not found.");
            SendSystem(conn, $"Available house types: {availableIds}...");
            return true;
        }

        long characterId = (long)conn.Player.CharacterId;

        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Houses (OwnerId, HouseDefinitionId, NameId, IsLocked, IsMembersOnly, IsFloraAllowed,
                                   PetAutospawn, MaxFixtureCount, MaxLandmarkCount, IconId, Rating, Votes,
                                   Created, LastVisited)
                VALUES ($ownerId, $houseDefId, 0, 0, 0, 1, 0, 100, 10, 0, 0.0, 0, datetime('now'), datetime('now'));

                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("$ownerId", characterId);
            cmd.Parameters.AddWithValue("$houseDefId", houseDefId);

            var newHouseId = cmd.ExecuteScalar();

            SendSystem(conn, $"Created house #{newHouseId} (Type: {houseDef.NameId}). Use /gohouse {newHouseId} to enter!");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create house for character {CharId}", characterId);
            SendSystem(conn, "Error creating house.");
            return true;
        }
    }

    private static bool HandleListHouses(GatewayConnection conn, string[] parts)
    {
        long characterId = (long)conn.Player.CharacterId;

        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, HouseDefinitionId, CustomName, Created
                FROM Houses
                WHERE OwnerId = $charId
                ORDER BY Created DESC;
            ";
            cmd.Parameters.AddWithValue("$charId", characterId);

            using var reader = cmd.ExecuteReader();

            var houses = new List<string>();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var defId = reader.GetInt32(1);
                var customName = reader.IsDBNull(2) ? null : reader.GetString(2);
                var created = reader.GetString(3);

                var name = customName ?? $"House #{id}";
                houses.Add($"#{id}: {name} (Def: {defId}, Created: {created})");
            }

            if (houses.Count == 0)
            {
                SendSystem(conn, "You don't have any houses. Use /createhouse to get one!");
            }
            else
            {
                SendSystem(conn, "Your houses:\n" + string.Join("\n", houses));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list houses for character {CharId}", characterId);
            SendSystem(conn, "Error listing houses.");
            return true;
        }
    }

    private static bool HandleGoHouse(GatewayConnection conn, string[] parts)
    {
        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /gohouse <HouseId>");
            return true;
        }

        if (!long.TryParse(parts[1], out var houseId))
        {
            SendSystem(conn, "House ID must be a number.");
            return true;
        }

        long characterId = (long)conn.Player.CharacterId;

        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT h.OwnerId, h.HouseDefinitionId
                FROM Houses h
                WHERE h.Id = $houseId
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("$houseId", houseId);

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
            {
                SendSystem(conn, $"House #{houseId} not found.");
                return true;
            }

            var ownerId = reader.GetInt64(0);
            var houseDefId = reader.GetInt32(1);

            if (ownerId != characterId)
            {
                SendSystem(conn, $"You don't have permission to enter house #{houseId}.");
                return true;
            }

            if (!_resourceManager.Houses.TryGetValue(houseDefId, out var houseDef))
            {
                SendSystem(conn, $"House definition {houseDefId} not found. Using default.");
                var defaultPacket = new PacketClientBeginZoning
                {
                    Name = "hsg_emptylot_seaside_beach_01",
                    Type = 2,
                    Position = new System.Numerics.Vector4(440.632f, -0.071f, 432.801f, 1.0f),
                    Rotation = new System.Numerics.Quaternion(-0.9999741f, 0.0f, -0.0072035603f, 0.0f),
                    Sky = "sky_seaside24.xml",
                    Unknown = 1,
                    Id = (int)houseId,
                    GeometryId = 214,
                    OverrideUpdateRadius = true
                };
                conn.SendTunneled(defaultPacket);
                SendSystem(conn, $"Entering house #{houseId}...");
                return true;
            }

            string zoneName = "hsg_emptylot_seaside_beach_01";
            string sky = "sky_seaside24.xml";
            int geometryId = 214;
            var spawnPosition = houseDef.SpawnPosition;
            var spawnRotation = new System.Numerics.Quaternion(
                houseDef.SpawnRotation.X,
                houseDef.SpawnRotation.Y,
                houseDef.SpawnRotation.Z,
                houseDef.SpawnRotation.W
            );

            if (_resourceManager.Zones.TryGetValue(houseDef.ZoneId, out var zoneDef))
            {
                zoneName = zoneDef.Name;
                if (zoneDef is Sanctuary.Game.Resources.Definitions.Zones.StartingZoneDefinition startingZone)
                {
                    spawnPosition = new System.Numerics.Vector4(
                        startingZone.SpawnPosition.X,
                        startingZone.SpawnPosition.Y + 2f,
                        startingZone.SpawnPosition.Z,
                        0
                    );

                    spawnRotation = new System.Numerics.Quaternion(
                        startingZone.SpawnRotation.X,
                        startingZone.SpawnRotation.Y,
                        0,
                        0
                    );

                    _logger.LogInformation("Using zone spawn position: ({X}, {Y}, {Z})",
                        spawnPosition.X, spawnPosition.Y, spawnPosition.Z);
                }

                _logger.LogInformation("Using zone {ZoneName} (ID: {ZoneId}) for house def {HouseDefId}",
                    zoneName, houseDef.ZoneId, houseDefId);
            }
            else
            {
                spawnPosition = new System.Numerics.Vector4(
                    houseDef.SpawnPosition.X,
                    houseDef.SpawnPosition.Y + 2f,
                    houseDef.SpawnPosition.Z,
                    houseDef.SpawnPosition.W
                );

                _logger.LogWarning("Zone {ZoneId} not found for house def {HouseDefId}, using default zone",
                    houseDef.ZoneId, houseDefId);
            }

            var packetClientBeginZoning = new PacketClientBeginZoning
            {
                Name = zoneName,
                Type = 2,
                Position = spawnPosition,
                Rotation = spawnRotation,
                Sky = sky,
                Unknown = 1,
                Id = (int)houseId,
                GeometryId = geometryId,
                OverrideUpdateRadius = true
            };

            conn.SendTunneled(packetClientBeginZoning);

            SendSystem(conn, $"Entering house #{houseId} (Type: {houseDef.NameId})...");
            _logger.LogInformation("Player {Player} entering house {HouseId} (Def: {DefId}, Zone: {ZoneName})",
                conn.Player.Name.FullName, houseId, houseDefId, zoneName);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enter house {HouseId} for character {CharId}", houseId, characterId);
            SendSystem(conn, "Error entering house.");
            return true;
        }
    }

    private static bool HandleSpawnHouse(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /spawnhouse <ModelId>");
            SendSystem(conn, "Try different model IDs to find house models (e.g., 5000-6000)");
            return true;
        }

        if (!int.TryParse(parts[1], out var modelId))
        {
            SendSystem(conn, "Model ID must be a number.");
            return true;
        }

        var zone = conn.Player.Zone;
        if (zone == null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        if (!zone.TryCreateNpc(out var houseNpc) || houseNpc is null)
        {
            SendSystem(conn, "Failed to create house NPC.");
            return true;
        }

        houseNpc.NameId = 0;
        houseNpc.ModelId = modelId;
        houseNpc.Name = $"House Model {modelId}";
        houseNpc.Scale = 1f;
        houseNpc.Visible = true;
        houseNpc.HideNamePlate = false;

        houseNpc.UpdatePosition(conn.Player.Position, conn.Player.Rotation);

        var tile = zone.GetTileFromPosition(conn.Player.Position);
        tile.Entities.TryAdd(houseNpc.Guid, houseNpc);

        conn.Player.OnAddVisibleNpcs([houseNpc]);

        SendSystem(conn, $"Spawned house model {modelId} at your position (GUID: {houseNpc.Guid})");
        return true;
    }

    private static void SpawnHouseStructure(GatewayConnection conn, int houseDefId)
    {
        try
        {
            var zone = conn.Player.Zone;
            if (zone == null)
            {
                _logger.LogWarning("Cannot spawn house structure - player not in zone");
                return;
            }

            if (!zone.TryCreateNpc(out var houseNpc) || houseNpc is null)
            {
                _logger.LogError("Failed to create house NPC");
                return;
            }

            if (!_resourceManager.Houses.TryGetValue(houseDefId, out var houseDef))
            {
                _logger.LogError("House definition {HouseDefId} not found", houseDefId);
                return;
            }

            int gameItemId = 0;
            foreach (var store in _resourceManager.Stores.Values)
            {
                foreach (var bundle in store.Bundles.Values)
                {
                    if (bundle.NameId == houseDef.NameId && bundle.Entries.Count > 0)
                    {
                        gameItemId = bundle.Entries[0].GameItemId;
                        _logger.LogInformation("Found GameItemId {GameItemId} for house NameId {NameId} (Def {HouseDefId})",
                            gameItemId, houseDef.NameId, houseDefId);
                        break;
                    }
                }
                if (gameItemId > 0) break;
            }

            var houseModelMapping = new Dictionary<int, int>
            {
                { 1, 5001 },
                { 2, 5002 },
                { 3, 5003 },
                { 4, 5004 },
                { 5, 5005 },
                { 6, 5006 },
                { 7, 5007 },
                { 8, 5008 },
                { 9, 5009 },
                { 10, 5010 },
            };

            int houseModelId = 0;

            if (houseModelMapping.TryGetValue(houseDefId, out var mappedModelId))
            {
                houseModelId = mappedModelId;
                _logger.LogInformation("Using mapped model {ModelId} for house def {HouseDefId}",
                    houseModelId, houseDefId);
            }
            else if (gameItemId > 0 && _resourceManager.ClientItemDefinitions.TryGetValue(gameItemId, out var itemDef))
            {
                houseModelId = itemDef.Param1;
                _logger.LogInformation("Found house model {ModelId} for house def {HouseDefId} from item {GameItemId}",
                    houseModelId, houseDefId, gameItemId);
            }

            if (houseModelId == 0)
            {
                houseModelId = 5000 + houseDefId;
                _logger.LogWarning("Could not find model for house def {HouseDefId} (NameId: {NameId}), using placeholder {ModelId}",
                    houseDefId, houseDef.NameId, houseModelId);
            }

            houseNpc.NameId = 0;
            houseNpc.ModelId = houseModelId;
            houseNpc.Name = "House";
            houseNpc.Scale = 1f;
            houseNpc.Visible = true;
            houseNpc.HideNamePlate = true;

            var housePosition = houseDef.SpawnPosition;
            var houseRotation = new System.Numerics.Quaternion(
                houseDef.SpawnRotation.X,
                houseDef.SpawnRotation.Y,
                houseDef.SpawnRotation.Z,
                houseDef.SpawnRotation.W
            );

            houseNpc.UpdatePosition(housePosition, houseRotation);

            var tile = zone.GetTileFromPosition(housePosition);
            tile.Entities.TryAdd(houseNpc.Guid, houseNpc);

            conn.Player.OnAddVisibleNpcs([houseNpc]);

            _logger.LogInformation("Spawned house structure with model {ModelId} at position {Pos}", houseModelId, housePosition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn house structure");
        }
    }

    private static bool HandleProj(GatewayConnection conn, string[] parts)
    {
        if (parts.Length < 2)
        {
            SendSystem(conn,
                $"proj {(Sanctuary.Game.Combat.ProjectileProbe.Enabled ? "ON" : "OFF")}: model={Sanctuary.Game.Combat.ProjectileProbe.Model} " +
                $"speed={Sanctuary.Game.Combat.ProjectileProbe.Speed} accel={Sanctuary.Game.Combat.ProjectileProbe.Acceleration} " +
                $"type={Sanctuary.Game.Combat.ProjectileProbe.FlightType} spin={Sanctuary.Game.Combat.ProjectileProbe.SpinRate} " +
                $"trail={Sanctuary.Game.Combat.ProjectileProbe.TrailEffectId} fx={Sanctuary.Game.Combat.ProjectileProbe.CompositeEffectId} " +
                $"height={Sanctuary.Game.Combat.ProjectileProbe.MuzzleHeight}");
            SendSystem(conn, "Usage: !proj <model|on|off|sync> [speed] [type] [spin]");
            SendSystem(conn, "       !proj speed|accel|type|spin|fx|height <n>");
            SendSystem(conn, "       !trail = cycle fx, !trail <keyword>, !trail off, !trails = list");
            SendSystem(conn, "Models: arrow flaming magical green purple laser sonic heart bolt ice icicle bone fish (or any *.adr)");
            SendSystem(conn, "Types: 1=beam 2=arc 4=seek 7=throw 8=boomerang. Fires on every archer attack.");
            return true;
        }

        var arg = parts[1].ToLowerInvariant();
        switch (arg)
        {
            case "on":
                Sanctuary.Game.Combat.ProjectileProbe.Enabled = true;
                SendSystem(conn, "proj ON");
                return true;
            case "off":
                Sanctuary.Game.Combat.ProjectileProbe.Enabled = false;
                SendSystem(conn, "proj OFF");
                return true;
            case "sync":
                Sanctuary.Game.Combat.ProjectileProbe.SyncDamage = !Sanctuary.Game.Combat.ProjectileProbe.SyncDamage;
                SendSystem(conn, $"proj damage-sync {(Sanctuary.Game.Combat.ProjectileProbe.SyncDamage ? "ON — damage lands with the arrow" : "OFF — old fixed delay")}");
                return true;
            case "speed" or "accel" or "type" or "spin" or "fx" or "height":
            {
                if (parts.Length < 3 || !float.TryParse(parts[2], out var value))
                {
                    SendSystem(conn, $"!proj {arg} <number>");
                    return true;
                }

                switch (arg)
                {
                    case "speed": Sanctuary.Game.Combat.ProjectileProbe.Speed = value; break;
                    case "accel": Sanctuary.Game.Combat.ProjectileProbe.Acceleration = value; break;
                    case "type": Sanctuary.Game.Combat.ProjectileProbe.FlightType = (int)value; break;
                    case "spin": Sanctuary.Game.Combat.ProjectileProbe.SpinRate = value; break;
                    case "fx": Sanctuary.Game.Combat.ProjectileProbe.CompositeEffectId = (int)value; break;
                    case "height": Sanctuary.Game.Combat.ProjectileProbe.MuzzleHeight = value; break;
                }

                SendSystem(conn, $"proj {arg} = {value}");
                return true;
            }
            case "trail":
                return HandleTrail(conn, parts, 2);
            case "trails":
                return HandleTrailList(conn);
            default:
            {
                Sanctuary.Game.Combat.ProjectileProbe.Model = ResolveProjModel(parts[1]);
                if (parts.Length >= 3 && float.TryParse(parts[2], out var speed))
                    Sanctuary.Game.Combat.ProjectileProbe.Speed = speed;
                if (parts.Length >= 4 && int.TryParse(parts[3], out var flightType))
                    Sanctuary.Game.Combat.ProjectileProbe.FlightType = flightType;
                if (parts.Length >= 5 && float.TryParse(parts[4], out var spin))
                    Sanctuary.Game.Combat.ProjectileProbe.SpinRate = spin;
                Sanctuary.Game.Combat.ProjectileProbe.Enabled = true;

                SendSystem(conn,
                    $"proj model={Sanctuary.Game.Combat.ProjectileProbe.Model} speed={Sanctuary.Game.Combat.ProjectileProbe.Speed} " +
                    $"type={Sanctuary.Game.Combat.ProjectileProbe.FlightType} spin={Sanctuary.Game.Combat.ProjectileProbe.SpinRate} — attack as archer");
                return true;
            }
        }
    }

    // Shared by `!trail ...` (argIndex 1) and `!proj trail ...` (argIndex 2).
    // Bare `!trail` = cycle to the next curated FX — shoot, !trail, shoot, !trail.
    private static bool HandleTrail(GatewayConnection conn, string[] parts, int argIndex)
    {
        var candidates = Sanctuary.Game.Combat.ProjectileProbe.TrailCandidates;

        var sub = parts.Length > argIndex ? parts[argIndex].ToLowerInvariant() : "next";

        if (sub is "next" or "prev")
        {
            var idx = Sanctuary.Game.Combat.ProjectileProbe.TrailIndex + (sub == "next" ? 1 : -1);
            if (idx < 0) idx = candidates.Length - 1;
            if (idx >= candidates.Length) idx = 0;
            SendSystem(conn, $"trail [{idx + 1}/{candidates.Length}] {Sanctuary.Game.Combat.ProjectileProbe.ApplyTrailEntry(idx)}");
            return true;
        }

        if (sub is "off" or "0" or "none")
        {
            Sanctuary.Game.Combat.ProjectileProbe.ClearTrail();
            SendSystem(conn, "trail OFF — plain arrow");
            return true;
        }

        if (sub is "list" or "help" or "?")
            return HandleTrailList(conn);

        var keyIndex = Sanctuary.Game.Combat.ProjectileProbe.FindTrailIndexByKey(sub);
        if (keyIndex >= 0)
        {
            SendSystem(conn, $"trail {Sanctuary.Game.Combat.ProjectileProbe.ApplyTrailEntry(keyIndex)}");
            return true;
        }

        if (int.TryParse(sub, out var trailId) && trailId > 0)
        {
            var known = Array.FindIndex(candidates, c => c.Id == trailId);
            if (known >= 0)
            {
                SendSystem(conn, $"trail {Sanctuary.Game.Combat.ProjectileProbe.ApplyTrailEntry(known)}");
            }
            else
            {
                Sanctuary.Game.Combat.ProjectileProbe.TrailEffectId = trailId;
                Sanctuary.Game.Combat.ProjectileProbe.TrailIndex = -1;
                SendSystem(conn, $"trail raw fx {trailId}");
            }
            return true;
        }

        SendSystem(conn, $"unknown trail '{parts[argIndex]}' — !trails lists keywords; !trail alone cycles");
        return true;
    }

    private static bool HandleTrailList(GatewayConnection conn)
    {
        var candidates = Sanctuary.Game.Combat.ProjectileProbe.TrailCandidates;

        SendSystem(conn, $"arrow FX ({candidates.Length}): !trail <keyword> sets it, bare !trail cycles, !trail off clears. ★ = model swap (always visible)");
        foreach (var c in candidates)
            SendSystem(conn, $"  {(c.Model.Length > 0 ? "★" : " ")}{c.Key,-14} {c.Name}");
        return true;
    }

    private static string ResolveProjModel(string arg)
    {
        var shorthand = arg.ToLowerInvariant() switch
        {
            "arrow" => "arrow_projectile.adr",
            "flaming" or "fire" => "arrow_projectile_flaming.adr",
            "magical" or "magic" => "arrow_projectile_magical.adr",
            "green" => "arrow_projectile_magical_green.adr",
            "purple" => "arrow_projectile_magical_purple.adr",
            "laser" => "arrow_projectile_laser-red.adr",
            "sonic" => "arrow_projectile_sonic.adr",
            "heart" => "arrow_projectile_heart.adr",
            "bolt" => "bolt_projectile.adr",
            "ice" => "ice_projectile.adr",
            "icicle" => "icicle_projectile.adr",
            "bone" => "bone_projectile.adr",
            "fish" => "fish_projectile.adr",
            _ => null,
        };

        if (shorthand is not null)
            return shorthand;

        return arg.EndsWith(".adr", StringComparison.OrdinalIgnoreCase) ? arg : arg + ".adr";
    }

    private static bool HandleTestEffect(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2)
        {
            SendSystem(conn, "Usage: /testeffect <effectId> [modelId] [animId] [standAnimId]");
            SendSystem(conn, "Example: /testeffect 16448 2201 1 2901");
            SendSystem(conn, "Boombox effects: 16448-16453 (red/blue/green/orange/purple/yellow)");
            SendSystem(conn, "Models: 1062=basic, 2201=tiki, 3893=robo, 4095=ballet");
            SendSystem(conn, "StandAnimId: 2901-2910 (env_loop_01-10)");
            return true;
        }

        if (!int.TryParse(parts[1], out var effectId))
        {
            SendSystem(conn, "Effect ID must be a number.");
            return true;
        }

        int modelId = 2201;
        if (parts.Length >= 3 && int.TryParse(parts[2], out var model))
        {
            modelId = model;
        }

        int animId = 1;
        if (parts.Length >= 4 && int.TryParse(parts[3], out var anim))
        {
            animId = anim;
        }

        int standAnimId = 0;
        if (parts.Length >= 5 && int.TryParse(parts[4], out var standAnim))
        {
            standAnimId = standAnim;
        }

        var zone = conn.Player.Zone;
        if (zone == null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        if (!zone.TryCreateNpc(out var npc) || npc is null)
        {
            SendSystem(conn, "Failed to create test NPC.");
            return true;
        }

        npc.NameId = 0;
        npc.ModelId = modelId;
        npc.Name = $"E{effectId} M{modelId}";
        npc.Scale = 1f;
        npc.Visible = true;
        npc.HideNamePlate = false;
        npc.CompositeEffectId = effectId;
        npc.Animation = animId;
        npc.StandAnimId = standAnimId;

        npc.UpdatePosition(conn.Player.Position, conn.Player.Rotation);

        var tile = zone.GetTileFromPosition(conn.Player.Position);
        tile.Entities.TryAdd(npc.Guid, npc);

        conn.Player.OnAddVisibleNpcs([npc]);

        var effectPacket = new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = npc.Guid,
            CompositeEffectId = effectId,
            Position = npc.Position,
            EffectDelay = 0
        };
        conn.Player.SendTunneled(effectPacket);

        SendSystem(conn, $"Spawned: effect={effectId}, model={modelId}, anim={animId}, standAnim={standAnimId}");
        return true;
    }

    private static bool HandlePlayEffect(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2 || !int.TryParse(parts[1], out var effectId))
        {
            SendSystem(conn, "Usage: /playeffect <effectId>");
            SendSystem(conn, "Plays the composite effect on your character. Use to find the right ID for food effects.");
            return true;
        }

        var packet = new PlayerUpdatePacketPlayCompositeEffect
        {
            Guid = conn.Player.Guid,
            CompositeEffectId = effectId,
            Position = conn.Player.Position,
        };

        conn.Player.SendTunneledToVisible(packet, true);
        SendSystem(conn, $"Playing effect {effectId} on your character.");
        return true;
    }

    private static bool HandleGiveItem(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2 || !int.TryParse(parts[1], out var itemId))
        {
            SendSystem(conn, "Usage: /giveitem <itemId> [count]");
            return true;
        }

        int count = 1;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out count) || count < 1))
        {
            SendSystem(conn, "Count must be a positive number.");
            return true;
        }

        if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemId, out var def))
        {
            SendSystem(conn, $"Item {itemId} not found.");
            return true;
        }

        using var defWriter = new PacketWriter();
        defWriter.Write(new[] { def });
        conn.SendTunneled(new PlayerUpdatePacketItemDefinitions { Payload = defWriter.Buffer });

        var existing = conn.Player.Items.FirstOrDefault(x => x.Definition == def.Id && x.Tint == 0);
        if (existing is not null)
        {
            existing.Count += count;
            conn.SendTunneled(new ClientUpdatePacketItemUpdate { ItemGuid = existing.Id, Count = existing.Count });

            try
            {
                using var db = new Microsoft.Data.Sqlite.SqliteConnection(_dbConnectionString);
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "UPDATE Items SET Count = $count WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$count", existing.Count);
                cmd.Parameters.AddWithValue("$id", existing.Id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update item count for item {Id}", existing.Id);
            }

            SendSystem(conn, $"Added {count}x item {itemId} (now have {existing.Count}).");
            return true;
        }

        var newItem = new ClientItem { Definition = def.Id, Count = count, Tint = 0 };

        if (!conn.SaveItemToDatabase(newItem))
        {
            SendSystem(conn, "Failed to save item to database.");
            return true;
        }

        conn.Player.Items.Add(newItem);

        using var itemWriter = new PacketWriter();
        newItem.Serialize(itemWriter);
        conn.SendTunneled(new ClientUpdatePacketItemAdd { Payload = itemWriter.Buffer });

        SendSystem(conn, $"Gave {count}x item {itemId} (NameId={def.NameId}).");
        return true;
    }

    private static bool HandleLua(GatewayConnection conn, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return true;

        int sp = message.IndexOf(' ');
        if (sp < 0 || sp + 1 >= message.Length)
        {
            SendSystem(conn, "Usage: /lua <script>");
            return true;
        }

        string script = message.Substring(sp + 1).Trim();

        conn.SendTunneled(new ExecuteScriptPacket { Script = script });
        conn.SendTunneled(new AbilityPacketExecuteClientLua { Script = script });

        SendSystem(conn, $"[lua] sent (both packets): {script}");
        _logger.LogInformation("/lua from {Player}: {Script}", conn.Player.Name.FullName, script);
        return true;
    }

    private static void SendMessageToPlayer(Player player, string message)
    {
        var packet = new PacketChat
        {
            Channel = ChatChannel.System,
            FromGuid = 0,
            FromName = new NameData(),
            Message = message
        };
        player.SendTunneled(packet);
    }

    private static bool TryResolveUsernamePattern(string pattern, out string resolvedUsername, out string error)
    {
        resolvedUsername = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "Username cannot be empty.";
            return false;
        }

        pattern = pattern.Trim();

        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT Username FROM Users;";
            using var reader = cmd.ExecuteReader();

            var allUsernames = new List<string>();
            while (reader.Read())
            {
                allUsernames.Add(reader.GetString(0));
            }

            if (allUsernames.Count == 0)
            {
                error = "No users exist in the database.";
                return false;
            }

            var exact = allUsernames
                .Where(u => string.Equals(u, pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (exact.Count == 1)
            {
                resolvedUsername = exact[0];
                return true;
            }
            if (exact.Count > 1)
            {
                error = $"Pattern '{pattern}' matches multiple usernames exactly: {string.Join(", ", exact)}. Please be more specific.";
                return false;
            }

            var prefix = allUsernames
                .Where(u => u.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (prefix.Count == 1)
            {
                resolvedUsername = prefix[0];
                return true;
            }
            if (prefix.Count > 1)
            {
                error = $"Pattern '{pattern}' is ambiguous (prefix of: {string.Join(", ", prefix)}). Please type more of the username.";
                return false;
            }

            var contains = allUsernames
                .Where(u => u.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (contains.Count == 1)
            {
                resolvedUsername = contains[0];
                return true;
            }
            if (contains.Count > 1)
            {
                error = $"Pattern '{pattern}' matches multiple usernames: {string.Join(", ", contains)}. Please be more specific.";
                return false;
            }

            error = $"No user found matching '{pattern}'.";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve username pattern.");
            error = "Error while resolving username pattern.";
            return false;
        }
    }

    private static bool TryResolvePlayerNamePattern(string pattern, out string resolvedName, out string error)
    {
        resolvedName = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "Player name cannot be empty.";
            return false;
        }

        pattern = pattern.Trim();

        var allNames = _zoneManager.StartingZone.Players
            .Select(p => p.Name.FullName)
            .Distinct()
            .ToList();

        if (allNames.Count == 0)
        {
            error = "No players online.";
            return false;
        }

        var exact = allNames
            .Where(n => string.Equals(n, pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exact.Count == 1)
        {
            resolvedName = exact[0];
            return true;
        }
        if (exact.Count > 1)
        {
            error = $"Pattern '{pattern}' matches multiple players exactly: {string.Join(", ", exact)}. Please be more specific.";
            return false;
        }

        var prefix = allNames
            .Where(n => n.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (prefix.Count == 1)
        {
            resolvedName = prefix[0];
            return true;
        }
        if (prefix.Count > 1)
        {
            error = $"Pattern '{pattern}' is ambiguous (prefix of: {string.Join(", ", prefix)}). Please type more of the name.";
            return false;
        }

        var contains = allNames
            .Where(n => n.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        if (contains.Count == 1)
        {
            resolvedName = contains[0];
            return true;
        }
        if (contains.Count > 1)
        {
            error = $"Pattern '{pattern}' matches multiple players: {string.Join(", ", contains)}. Please be more specific.";
            return false;
        }

        error = $"No player found matching '{pattern}'.";
        return false;
    }

    private static int ExecuteNonQuery(string sql, params (string name, object value)[] parameters)
    {
        try
        {
            using var db = new SqliteConnection(_dbConnectionString);
            db.Open();

            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;

            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value);

            return cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteNonQuery failed.");
            return 0;
        }
    }

    private static bool UnknownSubCommand(GatewayConnection conn, string root, string sub)
    {
        SendSystem(conn, $"Unknown /{root} subcommand '{sub}'. Try /help.");
        return true;
    }

    private static void SendSystem(GatewayConnection conn, string text)
    {
        var packet = new PacketChat
        {
            Channel = ChatChannel.System,
            FromGuid = conn.Player.Guid,
            FromName = conn.Player.Name,
            Message = text
        };

        conn.Player.SendTunneled(packet);
    }

    private static bool HandlePetSpawn(GatewayConnection conn, string[] parts)
    {
        if (parts.Length < 2)
        {
            if (conn.Player.Pets.Count == 0)
            {
                SendSystem(conn, "You don't own any pets. Usage: /petspawn [DbPetId]");
                return true;
            }

            SendSystem(conn, "Your pets: " + string.Join(", ", conn.Player.Pets.Select(p => $"DbId:{p.Id}")));
            return true;
        }

        if (!uint.TryParse(parts[1], out var dbPetId))
        {
            SendSystem(conn, "Invalid pet ID.");
            return true;
        }

        var petInfo = conn.Player.Pets.FirstOrDefault(x => x.Id == (int)dbPetId);
        if (petInfo is null)
        {
            SendSystem(conn, $"You don't own a pet with database ID {dbPetId}. Your pets: " + string.Join(", ", conn.Player.Pets.Select(p => $"DbId:{p.Id}")));
            return true;
        }

        if (conn.Player.Pet is not null)
        {
            SendSystem(conn, "You already have a pet active. Use /petdespawn first.");
            return true;
        }

        if (!_resourceManager.Pets.TryGetValue(petInfo.Definition, out var petDefinition))
        {
            SendSystem(conn, $"Pet definition not found (Definition ID: {petInfo.Definition}).");
            return true;
        }

        if (!conn.Player.Zone.TryCreatePet(conn.Player, petDefinition, out var pet))
        {
            SendSystem(conn, "Failed to spawn pet in zone.");
            return true;
        }

        pet.Visible = true;

        pet.Name = string.Empty;
        pet.NameId = petDefinition.NameId;
        pet.ModelId = petDefinition.ModelId;

        pet.TextureAlias = petDefinition.TextureAlias;
        pet.TintAlias = petDefinition.TintAlias;
        pet.TintId = petInfo.TintId;

        pet.Scale = petDefinition.Scale;
        pet.Disposition = 1;

        pet.HideNamePlate = false;

        pet.ImageSetId = petDefinition.ImageSetId;

        pet.MovementType = 2;

        pet.Animation = 1;

        conn.Player.Pet = pet;

        pet.UpdatePosition(conn.Player.Position, conn.Player.Rotation);

        var petSpawnResponsePacket = new PetSpawnResponsePacket();
        petSpawnResponsePacket.OwnerGuid = conn.Player.Guid;
        petSpawnResponsePacket.PetGuid = pet.Guid;
        petSpawnResponsePacket.CompositeEffectId = 0;
        conn.Player.SendTunneledToVisible(petSpawnResponsePacket, true);

        var petActivePacket = new PetActivePacket();
        petActivePacket.OwnerGuid = conn.Player.Guid;
        petActivePacket.PetGuid = pet.Guid;
        petActivePacket.CompositeEffectId = 46;
        conn.Player.SendTunneledToVisible(petActivePacket, true);

        SendSystem(conn, $"Pet spawned!");
        return true;
    }

    private static bool HandlePetDespawn(GatewayConnection conn, string[] parts)
    {
        if (conn.Player.Pet is null)
        {
            SendSystem(conn, "You don't have an active pet.");
            return true;
        }

        var petDismountResponsePacket = new PetDismountResponsePacket
        {
            OwnerGuid = conn.Player.Guid,
            CompositeEffectId = 0
        };

        conn.Player.SendTunneledToVisible(petDismountResponsePacket, true);

        conn.Player.Pet.Dispose();
        conn.Player.Pet = null;

        SendSystem(conn, "Pet despawned!");
        return true;
    }

    private static bool HandlePetList(GatewayConnection conn, string[] parts)
    {
        if (conn.Player.Pets.Count == 0)
        {
            SendSystem(conn, "You don't own any pets.");
            return true;
        }

        var petList = string.Join("\n", conn.Player.Pets.Select((p, i) =>
            $"Pet {i + 1}: DB ID={p.Id}, NameId={p.NameId}, ImageSetId={p.ImageSetId}, TintId={p.TintId}"));

        SendSystem(conn, "Your pets:\n" + petList);
        return true;
    }

    private static bool HandleRespawn(GatewayConnection conn)
    {
        if (!conn.Player.IsDead)
        {
            SendSystem(conn, "You are not dead!");
            return true;
        }

        conn.Player.Zone.OnPlayerRespawn(conn.Player);
        SendSystem(conn, "You have been revived!");
        return true;
    }

    private static bool HandleDodge(GatewayConnection conn, string[] parts)
    {
        var self = conn.Player.Guid;
        var arg = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";

        if (arg == "on" || arg == "off")
        {
            conn.Player.ForceDodgeDebug = arg == "on";
            SendSystem(conn, $"Force-dodge {(conn.Player.ForceDodgeDebug ? "ON — every enemy hit will dodge. Go fight a mob and watch for 'Dodge' text." : "OFF")}.");
            return true;
        }

        if (arg == "miss")
        {
            conn.Player.SendTunneledToVisible(new CombatPacketAttackAttackerMissed { AttackerGuid = self, TargetGuid = self }, sendToSelf: true);
            SendSystem(conn, "Sent op32/5 (Missed). Do you see 'Miss' text?");
            return true;
        }

        var attacker = arg == "self" ? self + 1 : self;
        conn.Player.SendTunneledToVisible(new CombatPacketAttackTargetDodged { AttackerGuid = attacker, TargetGuid = self }, sendToSelf: true);
        SendSystem(conn, $"Sent op32/6 (Dodged) attacker={attacker} target={self}. Do you see 'Dodge' text?");
        return true;
    }

    private static bool HandleDie(GatewayConnection conn)
    {
        if (conn.Player.IsDead)
        {
            SendSystem(conn, "You are already knocked out. Use /respawn.");
            return true;
        }

        conn.Player.Knockout();
        SendSystem(conn, "You collapsed. (Knockout triggered — /respawn to get back up.)");
        return true;
    }

    private static bool HandleHp(GatewayConnection conn, string[] parts)
    {
        if (parts.Length < 2)
        {
            var maxHp = conn.Player.Stats[CharacterStatId.MaxHealth].Int;
            SendSystem(conn, $"HP: {conn.Player.CurrentHitpoints}/{maxHp} | Mana: {conn.Player.CurrentMana}/{conn.Player.Stats[CharacterStatId.MaxMana].Int} | In Combat: {conn.Player.InCombat}");
            return true;
        }

        if (parts[1].ToLower() == "set" && parts.Length >= 3 && int.TryParse(parts[2], out var newHp))
        {
            var maxHp = conn.Player.Stats[CharacterStatId.MaxHealth].Int;
            conn.Player.CurrentHitpoints = Math.Clamp(newHp, 0, maxHp);

            conn.Player.SendTunneled(new ClientUpdatePacketHitpoints
            {
                CurrentHitpoints = conn.Player.CurrentHitpoints,
                MaxHitpoints = maxHp
            });

            SendSystem(conn, $"HP set to {conn.Player.CurrentHitpoints}/{maxHp}");
            return true;
        }

        if (parts[1].ToLower() == "full")
        {
            var maxHp = conn.Player.Stats[CharacterStatId.MaxHealth].Int;
            var maxMana = conn.Player.Stats[CharacterStatId.MaxMana].Int;
            conn.Player.CurrentHitpoints = maxHp;
            conn.Player.CurrentMana = maxMana;

            conn.Player.SendTunneled(new ClientUpdatePacketHitpoints
            {
                CurrentHitpoints = maxHp,
                MaxHitpoints = maxHp
            });

            conn.Player.SendTunneled(new ClientUpdatePacketMana
            {
                CurrentMana = maxMana,
                MaxMana = maxMana,
                ShowOverHead = false
            });

            SendSystem(conn, $"Healed to full! HP: {maxHp}/{maxHp}, Mana: {maxMana}/{maxMana}");
            return true;
        }

        SendSystem(conn, "Usage: /hp | /hp set <value> | /hp full");
        return true;
    }

    private static bool HandleXp(GatewayConnection conn, string[] parts)
    {
        var profile = conn.Player.ActiveProfile;

        if (parts.Length < 2)
        {
            SendSystem(conn, $"Job {profile.NameId}: level {profile.Rank}/{Sanctuary.Game.Leveling.JobLeveling.MaxLevel}, " +
                $"{profile.LevelXpRaw}/{Sanctuary.Game.Leveling.JobLeveling.XpForLevel(profile.Rank)} XP ({profile.RankPercent}%). Usage: /xp <amount>");
            return true;
        }

        if (!int.TryParse(parts[1], out var amount) || amount <= 0)
        {
            SendSystem(conn, "Usage: /xp <amount>");
            return true;
        }

        int before = profile.Rank;
        conn.Player.AwardXp(amount);

        if (profile.Rank > before)
            SendSystem(conn, $"Gained {amount} XP - leveled up to {profile.Rank}! (HP {conn.Player.CurrentHitpoints}/{conn.Player.Stats[CharacterStatId.MaxHealth].Int})");
        else
            SendSystem(conn, $"Gained {amount} XP. Level {profile.Rank}, {profile.LevelXpRaw}/{Sanctuary.Game.Leveling.JobLeveling.XpForLevel(profile.Rank)} ({profile.RankPercent}%)");

        return true;
    }

    private static bool HandleSpawnEnemy(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2 || !int.TryParse(parts[1], out var modelId))
        {
            SendSystem(conn, "Usage: /spawnenemy <ModelId> [Level] [Name]");
            return true;
        }

        var level = parts.Length >= 3 && int.TryParse(parts[2], out var lvl) ? lvl : 1;
        var name = parts.Length >= 4 ? string.Join(" ", parts[3..]) : "Enemy";

        var zone = conn.Player.Zone;

        if (!zone.TryCreateCombatNpc(out var combatNpc))
        {
            SendSystem(conn, "Failed to create combat NPC.");
            return true;
        }

        combatNpc.ModelId = modelId;
        combatNpc.Name = name;
        combatNpc.Scale = 1.0f;
        combatNpc.Disposition = 0;
        combatNpc.IsInteractable = true;
        combatNpc.InteractRange = 100;
        combatNpc.Speed = 6.0f;

        combatNpc.InitializeFromLevel(level);

        var forward = new System.Numerics.Vector3(
            2.0f * (conn.Player.Rotation.X * conn.Player.Rotation.Z + conn.Player.Rotation.W * conn.Player.Rotation.Y),
            0f,
            1.0f - 2.0f * (conn.Player.Rotation.X * conn.Player.Rotation.X + conn.Player.Rotation.Y * conn.Player.Rotation.Y)
        );

        var spawnPos = new System.Numerics.Vector4(
            conn.Player.Position.X + forward.X * 8f,
            conn.Player.Position.Y,
            conn.Player.Position.Z + forward.Z * 8f,
            1f
        );

        combatNpc.SpawnPosition = spawnPos;
        combatNpc.SpawnRotation = conn.Player.Rotation;
        combatNpc.UpdatePosition(spawnPos, conn.Player.Rotation);
        combatNpc.LastSentPosition = spawnPos;
        combatNpc.Visible = true;
        combatNpc.UpdateZoneTile();

        var addPacket = combatNpc.GetAddNpcPacket();
        conn.Player.SendTunneled(addPacket);
        conn.Player.VisibleNpcs.TryAdd(combatNpc.Guid, combatNpc);

        SendSystem(conn, $"Spawned combat NPC '{name}' (Level {level}, HP: {combatNpc.MaxHitpoints}, DMG: {combatNpc.AttackDamage}, XP: {combatNpc.XpReward})");
        return true;
    }

    private static bool HandleSpawnTest(GatewayConnection conn, string[] parts)
    {
        if (parts.Length < 3)
        {
            SendSystem(conn, "Usage: /spawntest <field> <value>  — fields: nameplate, imageset, profile, u67, u68, effect, nameid, namescale, clone");
            return true;
        }
        int.TryParse(parts[2], out var value);
        float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fvalue);

        var field = parts[1].ToLowerInvariant();

        var zone = conn.Player.Zone;
        if (zone is null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        if (!zone.TryCreateNpc(out var npc))
        {
            SendSystem(conn, "Failed to create NPC.");
            return true;
        }

        var vendorModel = zone.Npcs.FirstOrDefault(n => n.CursorId == 5)?.ModelId ?? 9240;
        var origin = conn.Player.Position;
        var rotation = conn.Player.Rotation;

        npc.ModelId = vendorModel;
        npc.NameId = 0;
        npc.Name = $"{field}={value}";
        npc.CursorId = 5;
        npc.Scale = 1f;
        npc.Disposition = 1;

        switch (field)
        {
            case "nameplate": npc.NameplateImageId = value; break;
            case "imageset":  npc.ImageSetId = value; break;
            case "profile":   npc.ActiveProfile = value; break;
            case "u67":       npc.NotificationImageSetId = value; break;
            case "u68":       npc.Unknown68 = value; break;
            case "effect":    npc.CompositeEffectId = value; break;
            case "notif":     npc.NotificationImageSetId = value; break;
            case "nameid":    npc.NameId = value; npc.Name = null; break;
            case "namescale":
                npc.NameplateImageId = 22663;
                npc.NameScale = fvalue;
                break;
            case "clone":
                if (zone.TryGetNpc((ulong)value, out var src))
                {
                    npc.ModelId      = src.ModelId;
                    npc.NameId       = src.NameId;
                    npc.Name         = src.Name;
                    npc.SubTextNameId = src.SubTextNameId;
                    npc.ActiveProfile = src.ActiveProfile;
                    npc.ImageSetId   = src.ImageSetId;
                    npc.NameplateImageId = src.NameplateImageId;
                    npc.TextureAlias = src.TextureAlias;
                }
                else
                {
                    SendSystem(conn, $"NPC {value} not found.");
                    return true;
                }
                break;
            default:
                SendSystem(conn, $"Unknown field '{field}'. Use: nameplate, imageset, profile, u67, u68, effect, nameid, clone");
                return true;
        }

        npc.Visible = false;
        var pos = origin with { X = origin.X + 3f };
        npc.UpdatePosition(pos, rotation);

        npc.Visible = true;
        zone.GetTileFromPosition(pos).Entities.TryAdd(npc.Guid, npc);
        conn.Player.OnAddVisibleNpcs([npc]);

        SendSystem(conn, $"Spawned NPC with {field}={value}.");
        return true;
    }

    private static bool HandleTestSubText(GatewayConnection conn, string[] parts)
    {
        var zone = conn.Player.Zone;
        if (zone is null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        int start = 2910;
        int end = 2940;
        if (parts.Length >= 2) int.TryParse(parts[1], out start);
        if (parts.Length >= 3) int.TryParse(parts[2], out end);
        if (end < start) end = start + 30;

        var origin = conn.Player.Position;
        var rotation = conn.Player.Rotation;
        var vendorModel = zone.Npcs.FirstOrDefault(n => n.CursorId == 5)?.ModelId ?? 9240;
        const int cols = 6;

        int count = 0;
        int i = 0;
        for (int subTextId = start; subTextId <= end; subTextId++, i++)
        {
            if (!zone.TryCreateNpc(out var npc))
                continue;

            int col = i % cols;
            int row = i / cols;
            var pos = origin with
            {
                X = origin.X + col * 4f,
                Z = origin.Z + 5f + row * 5f
            };

            npc.ModelId = vendorModel;
            npc.NameId = 0;
            npc.Name = $"ST={subTextId}";
            npc.SubTextNameId = subTextId;
            npc.NotificationImageSetId = 294;
            npc.ActiveProfile = 200;
            npc.CursorId = 5;
            npc.Scale = 1f;
            npc.Disposition = 1;

            npc.UpdatePosition(pos, rotation);
            npc.Visible = true;
            zone.GetTileFromPosition(pos).Entities.TryAdd(npc.Guid, npc);
            conn.Player.OnAddVisibleNpcs([npc]);
            count++;
        }

        SendSystem(conn, $"Spawned {count} NPCs with SubTextNameId {start}-{end}.");
        return true;
    }

    private static bool HandleTestIcons(GatewayConnection conn)
    {
        var zone = conn.Player.Zone;
        if (zone is null)
        {
            SendSystem(conn, "You are not in a zone.");
            return true;
        }

        int[] values = [281, 282, 283, 284, 285, 286, 287, 288, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301, 302, 303, 304, 305, 306, 307, 308, 309, 310, 311, 312, 313, 314, 315, 316, 317, 318, 319, 320];
        const int cols = 6;

        var origin = conn.Player.Position;
        var rotation = conn.Player.Rotation;
        var vendorModel = zone.Npcs.FirstOrDefault(n => n.CursorId == 5)?.ModelId ?? 9240;

        var created = new List<Sanctuary.Game.Entities.Npc>();

        for (int i = 0; i < values.Length; i++)
        {
            if (!zone.TryCreateNpc(out var npc))
                continue;

            npc.ModelId = vendorModel;
            npc.NameId = 0;
            npc.Name = $"NP {values[i]}";
            npc.NameplateImageId = values[i];
            npc.ImageSetId = 381;
            npc.CursorId = 5;
            npc.Scale = 1f;
            npc.Disposition = 1;

            int col = i % cols;
            int row = i / cols;
            var pos = origin with
            {
                X = origin.X + col * 4f,
                Z = origin.Z + 5f + row * 5f
            };
            npc.UpdatePosition(pos, rotation);
            npc.Visible = true;

            zone.GetTileFromPosition(pos).Entities.TryAdd(npc.Guid, npc);
            created.Add(npc);
        }

        conn.Player.OnAddVisibleNpcs(created);
        SendSystem(conn, $"Spawned {created.Count} test icon NPCs at your position.");
        return true;
    }

    private static bool HandleFly(GatewayConnection conn)
    {
        var guid = conn.Player.Guid;
        bool enabling = _flyingPlayers.Add(guid);
        if (!enabling)
            _flyingPlayers.Remove(guid);

        var packet = new ClientUpdatePacketUpdateStat { Guid = guid };

        if (enabling)
        {
            packet.Stats.AddRange([
                new CharacterStat(CharacterStatId.GlideEnabled, 1),
                new CharacterStat(CharacterStatId.GlideDefaultForwardSpeed, 50f),
                new CharacterStat(CharacterStatId.GlideMinForwardSpeed, 0f),
                new CharacterStat(CharacterStatId.GlideMaxForwardSpeed, 100f),
                new CharacterStat(CharacterStatId.GlideAccel, 50f),
                new CharacterStat(CharacterStatId.GlideFallSpeed, 0f),
                new CharacterStat(CharacterStatId.GlideFallTime, 999999f),
                new CharacterStat(CharacterStatId.MaxMovementSpeed, 50f),
            ]);
            SendSystem(conn, "Fly mode ON — jump to activate glide.");
        }
        else
        {
            packet.Stats.AddRange([
                new CharacterStat(CharacterStatId.GlideEnabled, 0),
                new CharacterStat(CharacterStatId.GlideDefaultForwardSpeed, 0f),
                new CharacterStat(CharacterStatId.GlideMinForwardSpeed, 0f),
                new CharacterStat(CharacterStatId.GlideMaxForwardSpeed, 0f),
                new CharacterStat(CharacterStatId.GlideAccel, 0f),
                new CharacterStat(CharacterStatId.GlideFallSpeed, 0f),
                new CharacterStat(CharacterStatId.GlideFallTime, 0f),
                new CharacterStat(CharacterStatId.MaxMovementSpeed, 8f),
            ]);
            SendSystem(conn, "Fly mode OFF.");
        }

        conn.Player.SendTunneled(packet);
        return true;
    }

    private static bool HandleTestTransform(GatewayConnection conn, string[] parts)
    {
        if (!RequireAdmin(conn))
            return true;

        if (parts.Length < 2 || !int.TryParse(parts[1], out var modelId))
        {
            SendSystem(conn, "Usage: /testtransform <modelId>  (use 0 to revert)");
            SendSystem(conn, "Examples: /testtransform 50 (cat)  /testtransform 176 (wolf)  /testtransform 0 (revert)");
            return true;
        }

        if (modelId == 0)
        {
            AbilityPacketClientRequestStartAbilityHandler.RemoveTransform(conn);
            SendSystem(conn, "Transform removed.");
        }
        else
        {
            AbilityPacketClientRequestStartAbilityHandler.ApplyTransform(conn, modelId, 60_000);
            SendSystem(conn, $"Applied NPC overlay transform modelId={modelId} for 60s — check the 2nd screen.");
        }

        return true;
    }
}
