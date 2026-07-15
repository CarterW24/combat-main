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

        // COMBAT WIP: capture EVERY ability sub-opcode the client sends. Per the packet map,
        // sub-op 12 = RequestAbilityDefinition (client asks us to define an ability when swapping to a
        // combat job) -> the remaining bytes carry the requested ability id(s). (See docs/STATUS.md.)
        _logger.LogInformation("BaseAbilityPacket sub-opcode={sub} | remaining bytes={hex}",
            opCode, Convert.ToHexString(reader.Span));

        return opCode switch
        {
            AbilityPacketClientRequestStartAbility.OpCode => AbilityPacketClientRequestStartAbilityHandler.HandlePacket(connection, reader.Span),
            AbilityPacketRequestAbilityDefinition.OpCode => HandleAbilityDefinitionRequest(connection, reader.Span),
            // observe-only: don't hard-fail unknown/unhandled ability sub-opcodes while we map them
            _ => true
        };
    }

    private static bool HandleAbilityDefinitionRequest(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!AbilityPacketRequestAbilityDefinition.TryDeserialize(data, out var packet))
            return false;

        // Answer with the FULL AbilityDefinition record so the client inserts it into its ability-def map, which
        // is what the AbilitiesScreen's Attack / Special Attack columns read (name/desc/icon). Resolve name+desc+
        // icon from the active job's weapon kit; null = not one of our slot def ids (send a bare record so the
        // client still has an entry rather than re-requesting forever).
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
