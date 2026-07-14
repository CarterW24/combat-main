using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// The player closed a minigame's result card (BaseCommandPacket sub-op 42). For a combat encounter that's
/// the cue to actually send them home: the win ("You Win!") and fail ("TRY AGAIN!") screens now stay up until
/// they're dismissed, and the teardown + teleport out of the instance happen here rather than immediately on
/// the exit-door click / on a fixed timer.
/// </summary>
[PacketHandler]
public static class CommandPacketClosedMinigameEndScreenHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CommandPacketClosedMinigameEndScreenHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CommandPacketClosedMinigameEndScreen.TryDeserialize(data, out _))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CommandPacketClosedMinigameEndScreen));
            return false;
        }

        // Only combat encounters gate their exit on the card being closed; every other minigame ignores this.
        if (connection.Player.Zone is CombatEncounterZone encounter)
            encounter.OnEndScreenClosed(connection.Player);

        return true;
    }
}
