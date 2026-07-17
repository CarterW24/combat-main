using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseAbilityPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseAbilityPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short opCode))
        {
            _logger.LogError("Failed to read opcode from packet. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        _logger.LogInformation("BaseAbilityPacket sub-opcode={sub} | remaining bytes={hex}",
            opCode, Convert.ToHexString(reader.Span));

        return opCode switch
        {
            AbilityPacketClientRequestStartAbility.OpCode => AbilityPacketClientRequestStartAbilityHandler.HandlePacket(connection, reader.Span),
            AbilityPacketRequestAbilityDefinition.OpCode => HandleAbilityDefinitionRequest(connection, reader.Span),
            _ => true
        };
    }

    private static bool HandleAbilityDefinitionRequest(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!AbilityPacketRequestAbilityDefinition.TryDeserialize(data, out var packet))
            return false;

        var def = Sanctuary.Game.Combat.JobWeaponAbilities.ResolveAbilityDefinition(connection.Player, packet.AbilityId);

        connection.SendTunneled(new AbilityPacketAbilityDefinition
        {
            AbilityId = packet.AbilityId,
            NameId = def?.NameId ?? 0,
            DescriptionId = def?.DescId ?? 0,
            IconId = def?.IconId ?? 0,
        });

        return true;
    }
}
