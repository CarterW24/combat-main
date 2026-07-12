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

        // LEAVE BUTTON: this op39/sub6 is the minigame UI's "Leave" button. In a combat dungeon/encounter
        // it must also take the player back to the overworld (same as the victory exit door) — otherwise
        // the button just closed the panel and left them stuck in the instance. Route to whichever arena
        // they're in; UseExitDoor -> ReturnHome no-ops if they're not actually in that zone.
        var player = connection.Player;
        switch (player.Zone)
        {
            case EncounterArenaZone arena:
                _logger.LogInformation("Leave button pressed in {zone} — returning {name} to the overworld.", arena.Name, player.Name);
                arena.UseExitDoor(player);
                break;
            case FrostfangArenaZone frostfang:
                frostfang.UseExitDoor(player);
                break;
            case TormentedSpiritsArenaZone spirits:
                spirits.UseExitDoor(player);
                break;
        }

        return true;
    }
}