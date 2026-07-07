using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseCommandPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseCommandPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short opCode))
        {
            _logger.LogError("Failed to read opcode from packet. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        return opCode switch
        {
            CommandPacketInteractRequest.OpCode => CommandPacketInteractRequestHandler.HandlePacket(connection, reader.Span),
            CommandPacketFreeInteractionNpc.OpCode => CommandPacketFreeInteractionNpcHandler.HandlePacket(connection, reader.Span),
            ClearInteractionMerchantSetId.OpCode => ClearInteractionMerchantSetIdHandler.HandlePacket(connection, reader.Span),
            CommandPacketInteractionSelect.OpCode => CommandPacketInteractionSelectHandler.HandlePacket(connection, reader.Span),
            CommandPacketSetProfile.OpCode => CommandPacketSetProfileHandler.HandlePacket(connection, reader.Span),
            CommandPacketAddFriendRequest.OpCode => CommandPacketAddFriendRequestHandler.HandlePacket(connection, reader.Span),
            CommandPacketRemoveFriendRequest.OpCode => CommandPacketRemoveFriendRequestHandler.HandlePacket(connection, reader.Span),
            CommandPacketConfirmFriendResponse.OpCode => CommandPacketConfirmFriendResponseHandler.HandlePacket(connection, reader.Span),
            CommandPacketSetChatBubbleColor.OpCode => CommandPacketSetChatBubbleColorHandler.HandlePacket(connection, reader.Span),
            CommandPacketSelectPlayer.OpCode => CommandPacketSelectPlayerHandler.HandlePacket(connection, reader.Span),
            CommandPacketFriendsPositionRequest.OpCode => CommandPacketFriendsPositionRequestHandler.HandlePacket(connection),
            CommandPacketIgnoreRequest.OpCode => CommandPacketIgnoreRequestHandler.HandlePacket(connection, reader.Span),
            CommandPacketChatChannelOn.OpCode => CommandPacketChatChannelOnHandler.HandlePacket(connection, reader.Span),
            CommandPacketChatChannelOff.OpCode => CommandPacketChatChannelOffHandler.HandlePacket(connection, reader.Span),
            23 => CommandPacketQuestAbandonHandler.HandlePacket(connection, reader.Span), // "Drop Quest" (journal)
            6 => HandleDialogResponse(connection),                                        // 26/6 PacketDialogResponse
            _ => false
        };
    }

    // The player clicked a response button on a CommandPacketShowDialog (26/3) NPC conversation. Wire-
    // confirmed: the client sends 26/6 (payload = int response Id). Respond with the proper NPC-dialog
    // teardown CommandPacketEndDialog (26/4 -> client FUN_008a7ce0 frees the native dialog object at
    // +0x654 and restores the camera via FUN_009f6890). NOT sub-opcode 29 (QuestDialogComplete): that
    // dispatches "QuestStartHandler:DismissEndScreen", which is for the quest END SCREEN - sending it
    // here hid the whole HUD and locked player movement (no end screen was open to dismiss).
    private static bool HandleDialogResponse(GatewayConnection connection)
    {
        connection.Player.SendTunneled(new CommandPacketEndDialog());
        return true;
    }
}