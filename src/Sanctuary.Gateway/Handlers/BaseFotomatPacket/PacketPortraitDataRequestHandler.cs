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

        // The client requests a portrait for an arbitrary character guid (self OR a party/group member).
        // Resolve the TARGET whose appearance we must serialize — NOT the requester. Sending the requester's
        // own appearance made every member's headshot look like the requester (and the missing-PNG bail-out
        // dropped the response entirely, leaving a silhouette).
        Player? target = packet.Guid == connection.Player.Guid
            ? connection.Player
            : (_zoneManager.TryGetPlayer(packet.Guid, out var found) ? found : null);

        if (target is null)
        {
            _logger.LogTrace("No online player for portrait guid {guid}.", packet.Guid);
            return true;
        }

        connection.SendTunneled(BuildImageData(target, packet.Provider));

        return true;
    }

    /// <summary>Builds the S2C <see cref="PacketPlayerImageData"/> (Fotomat op156/sub3) that fills the
    /// client's portrait cache for <paramref name="target"/>. The client renders another player's headshot
    /// from the served PNG (appearance fields alone render blank — verified), so the party roster needs a
    /// real <c>headshot.png</c> on disk for each character.
    ///
    /// ★★ REQUIRES A HARVESTER (still needs to be written) ★★
    /// Party MEMBER headshots only render once each player's 70x70 <c>headshot.png</c> exists under
    /// <c>Images/&lt;characterId&gt;/</c>. The end-to-end pipeline is PROVEN working (request/response +
    /// this push both deliver the PNG and the client renders it — tested with a real PNG). The ONLY missing
    /// piece is the UPLOAD: this client build renders each player's own headshot locally
    /// (client FUN_00bd4930 -> a clean 70x70 PNG) but NEVER transmits it — not via the WebAPI /image POST,
    /// not via a game packet — for ANY trigger (login, job/appearance change, character creation, or a
    /// server-sent PortraitDataRequest). Its upload code is dormant. So a HARVESTER must supply that upload:
    /// hook the client's headshot render (FUN_00bd4930), grab the PNG, and POST it to WebAPI /image
    /// (multipart, boundary "AaBb432101234bBaA", imageType=portrait, characterId, thumbnailFile=headshot.png).
    /// To scale to many players it should be a launcher-injected client DLL so every player self-uploads
    /// automatically. Until that harvester is written, member portraits fall back to the client's default
    /// silhouette; SELF portraits always work (the client renders its own locally).</summary>
    public static PacketPlayerImageData BuildImageData(Player target, string? provider, bool includeAttachments = true)
    {
        // The harvester uploads the headshot under Images/<characterId>/, but callers key by the entity guid
        // (guid = charId<<4 | 1). Look under BOTH so a captured portrait is found regardless of which id was
        // used for the upload.
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

                // Attachments (weapons/equipment) balloon the packet to ~800+ bytes and aren't needed for a
                // headshot; the roster push skips them so op156 stays a small, reliably-delivered packet.
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

    private static byte[]? ReadHeadshot(ulong id)
    {
        var path = Path.Combine("Images", id.ToString(), "headshot.png");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}