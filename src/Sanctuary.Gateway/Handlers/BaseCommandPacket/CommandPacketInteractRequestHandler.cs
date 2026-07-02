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

            connection.SendTunneled(new EncounterDetailsResponsePacket
            {
                // REAL ids from the team's minigame branch: Resources/ClientActivityDefinitions.json, activity
                // Id 174 "Frostfang Growler!" (Category 99 = wandering combat encounter, ServerType 1 = world/arena
                // launch). These are the ACTIVITY string ids (NameId/DescriptionId), a different id-space than the
                // raw en_us_data hash 3078903256 that the client didn't know. TEST: does 93276 resolve in-client?
                // If it stays blank, we must feed the activity definitions to the client (ActivityProfileList) first.
                // Previous placeholders were 5698 ("Frostfang Caverns" = activity Id 33) / 382845 / icon 670.
                NameId = 93276,                       // "Frostfang Growler!"  (ClientActivityDefinitions Id 174)
                DescriptionId = 104171,               // Growler description
                Difficulty = 1,                       // 1 of 5 pips (matches the def)
                IconId = 1345,                        // wolf emblem ImageSetId (was default squirrel 670)
            });

            // Auto-complete the ready handshake (sub107 -> "HandlerMiniGameStart:setReady") shortly after the
            // popup opens: the spinner flips to the green GO! without needing the "!ready" chat command.
            // Small delay so the panel exists client-side before setReady lands (packet order alone is enough,
            // but the spinner beat also matches the real game's feel).
            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                connection.SendTunneled(new EncounterZoneIsReadyPacket());
            });

            return true;
        }

        entity.OnInteract(connection.Player);

        return true;
    }
}