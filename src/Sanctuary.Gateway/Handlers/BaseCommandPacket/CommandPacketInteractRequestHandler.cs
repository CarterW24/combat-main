using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CommandPacketInteractRequestHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CommandPacketInteractRequestHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CommandPacketInteractRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CommandPacketInteractRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CommandPacketInteractRequest), packet);

        if (!connection.Player.Zone.TryGetEntity(packet.Guid, out var entity))
            return true;

        // INSTANCE (Frostfang Fury): the victory EXIT DOOR (846 sg_exit_door_01, live-decoded) —
        // clicking it releases the encounter and sends the player home. This replaced the old
        // 6-second auto-kick; the player spins the loot wheel and leaves on their own time.
        if (connection.Player.Zone is FrostfangArenaZone arena && arena.IsExitDoor(packet.Guid))
        {
            arena.UseExitDoor(connection.Player);
            return true;
        }

        // INSTANCE WIP (Frostfang Fury): clicking the Frostfang Growler wolf opens the adventure offer popup
        // (EncounterDetailsResponsePacket). The interaction provides the encounter context the cold "!offer"
        // test lacked. If the client still doesn't render it on click, it likely needs the NPC flagged as an
        // encounter-giver or a specific request packet — this test tells us which.
        if (connection.Player.Zone is StartingZone startingZone
            && startingZone.GrowlerWolf is { } growler
            && growler.Guid == packet.Guid)
        {
            _logger.LogInformation("InteractRequest: Frostfang Growler ({guid}) clicked -> sending offer popup.",
                packet.Guid);

            // Encounter state machine (op41/sub106, mirrored from the live 2014-04-01 capture): the
            // real server steps 2 -> 3 -> 4 BEFORE the offer details, 5 with the ready ack, 6 in-zone.
            foreach (var state in new[] { 2, 3, 4 })
            {
                connection.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = FrostfangArenaZone.EncounterId,
                    InstanceId = FrostfangArenaZone.EncounterInstanceId,
                    State = state,
                });
            }

            connection.SendTunneled(new EncounterDetailsResponsePacket
            {
                // Header ints = [EncounterId][InstanceId] on the live wire (details + state + PlayerEnter
                // all share them).
                Unknown = FrostfangArenaZone.EncounterId,
                Unknown2 = FrostfangArenaZone.EncounterInstanceId,
                // REAL ids from the team's minigame branch: Resources/ClientActivityDefinitions.json, activity
                // Id 174 "Frostfang Growler!" (Category 99 = wandering combat encounter, ServerType 1 = world/arena
                // launch). These are the ACTIVITY string ids (NameId/DescriptionId), a different id-space than the
                // raw en_us_data hash 3078903256 that the client didn't know. TEST: does 93276 resolve in-client?
                // If it stays blank, we must feed the activity definitions to the client (ActivityProfileList) first.
                // Previous placeholders were 5698 ("Frostfang Caverns" = activity Id 33) / 382845 / icon 670.
                NameId = 93276,                       // "Frostfang Growler!"  (ClientActivityDefinitions Id 174)
                DescriptionId = 104171,               // Growler description
                Difficulty = 1,                       // 1 of 5 pips (matches the def)
                IconId = 1345,                        // wolf emblem ImageSetId (real launch used 28605; 1345 is live-proven here)
                MiniGameType = 4,                     // COMBAT (matches the real packet; the launch copy already sends it)
                // ★ PRIZES on the talk popup (2026-07-04): the popup's reward list renders from the
                // PREVIEW reward bundle ("BaseClient.MiniGame.RewardPreview.Entries", up to 4 non-hidden
                // rows). Set picked for the player's ACTIVE JOB server-side — live behavior; the packet
                // carries only the job CATEGORY (ProfileType 2 = combat jobs).
                PreviewRewards = FrostfangArenaZone.GetPrizePreviewFor(connection.Player),
                PreviewCoins = FrostfangArenaZone.PrizeCoins,
                PreviewXp = FrostfangArenaZone.PrizeXp,
                ProfileType = FrostfangArenaZone.CombatProfileType,
                ActivityId = FrostfangArenaZone.EncounterId,
            });

            // Auto-complete the ready handshake (sub107 -> "HandlerMiniGameStart:setReady") shortly after the
            // popup opens: the spinner flips to the green GO! without needing the "!ready" chat command.
            // Small delay so the panel exists client-side before setReady lands (packet order alone is enough,
            // but the spinner beat also matches the real game's feel).
            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                connection.SendTunneled(new EncounterZoneIsReadyPacket());
                // Live order: state 5 lands right after the ready ack (04-01 seq 27148 -> 27150).
                connection.SendTunneled(new EncounterStatePacket
                {
                    EncounterId = FrostfangArenaZone.EncounterId,
                    InstanceId = FrostfangArenaZone.EncounterInstanceId,
                    State = 5,
                });
            });

            return true;
        }

        entity.OnInteract(connection.Player);

        return true;
    }
}