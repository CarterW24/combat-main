using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class MiniGameEndPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MiniGameEndPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!MiniGameEndPacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(MiniGameEndPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(MiniGameEndPacket), packet);

        var miniGameLeavePacket = new MiniGameLeavePacket(packet.StateId);

        connection.SendTunneled(miniGameLeavePacket);

        // LEAVE BUTTON: this op39/sub6 is the minigame UI's "Leave" button. In a combat dungeon/encounter it
        // must also take the player back to the overworld — otherwise the button just closed the panel and
        // left them stuck in the instance. LeaveEncounter (NOT UseExitDoor, which is the victory door and
        // raises a "You Win!" card): bails out with no card, or, if a result card is already up, treats this
        // as the player dismissing it and exits the same way.
        var player = connection.Player;

        if (player.Zone is CombatEncounterZone encounter)
        {
            _logger.LogInformation("Leave button pressed in {zone} — returning {name} to the overworld.", encounter.Name, player.Name);
            encounter.LeaveEncounter(player);
        }

        return true;
    }
}