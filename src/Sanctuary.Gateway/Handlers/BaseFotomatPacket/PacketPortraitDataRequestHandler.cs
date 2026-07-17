using System;
using System.IO;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketPortraitDataRequestHandler
{
    private static ILogger _logger = null!;
    private static IZoneManager _zoneManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketPortraitDataRequestHandler));
        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PacketPortraitDataRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketPortraitDataRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketPortraitDataRequest), packet);

        Player? target = packet.Guid == connection.Player.Guid
            ? connection.Player
            : (_zoneManager.TryGetPlayer(packet.Guid, out var found) ? found : null);

        if (target is null)
        {
            _logger.LogTrace("No online player for portrait guid {guid}.", packet.Guid);
            return true;
        }

        if (!HasHeadshot(target))
        {
            _logger.LogInformation("FOTOMAT: no headshot on disk for {name} (guid {guid}, provider {provider}) — not replying (an empty payload would blank the slot).",
                target.Name?.FullName, packet.Guid, packet.Provider);
            return true;
        }

        connection.SendTunneled(BuildImageData(target, packet.Provider));

        return true;
    }

    public static PacketPlayerImageData BuildImageData(Player target, string? provider, bool includeAttachments = true)
    {
        var png = ReadHeadshot(GuidHelper.GetPlayerId(target.Guid)) ?? ReadHeadshot(target.Guid) ?? Array.Empty<byte>();

        return new PacketPlayerImageData
        {
            Guid = target.Guid,
            Provider = provider,
            Portrait =
            {
                Unknown2 = 1,

                Guid = target.Guid,

                ModelId = target.Model,

                Attachments = includeAttachments ? target.GetAttachments() : new(),

                Head = target.Head,
                Hair = target.Hair,
                SkinTone = target.SkinTone,
                FacePaint = target.FacePaint,
                ModelCustomization = target.ModelCustomization,

                HairColor = target.HairColor,
                EyeColor = target.EyeColor,
                HeadId = target.HeadId,
                HairId = target.HairId,
                SkinToneId = target.SkinToneId,
                FacePaintId = target.FacePaintId,

                Provider = provider
            },
            PngPayload = png
        };
    }

    public static bool HasHeadshot(Player target) =>
        ReadHeadshot(GuidHelper.GetPlayerId(target.Guid)) is not null || ReadHeadshot(target.Guid) is not null;

    private static byte[]? ReadHeadshot(ulong id)
    {
        var path = Path.Combine("Images", id.ToString(), "headshot.png");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
