using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketClientFinishedLoadingHandler
{
    private static ILogger _logger = null!;
    private static ICombatManager _combatManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketClientFinishedLoadingHandler));

        _combatManager = serviceProvider.GetRequiredService<ICombatManager>();
    }

    public static bool HandlePacket(GatewayConnection connection)
    {
        _logger.LogTrace("Received {name} packet.", nameof(PacketClientFinishedLoading));

        connection.Player.Visible = true;

        if (connection.Player.Mount is not null)
            connection.Player.Mount.Visible = true;

        connection.Player.UpdatePosition(connection.Player.Position, connection.Player.Rotation);

        if (connection.Player.Mount is not null)
        {
            connection.Player.SendTunneled(connection.Player.Mount.GetAddNpcPacket());
            connection.Player.SendTunneled(connection.Player.Mount.GetMountResponsePacket());
        }

        connection.Player.Zone.OnClientFinishedLoading(connection.Player);

        _combatManager.SendToolbar(connection.Player);

        return true;
    }
}