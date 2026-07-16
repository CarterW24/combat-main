namespace Sanctuary.Core.Configuration;

public sealed class GatewayServerOptions : ServerOptions
{
    // live
    public required string Environment { get; set; }

    // Client version the server supports.
    // 1.910.1.530630
    public required string ClientVersion { get; set; }

    public required string ServerAddress { get; set; }

    public required string LoginGatewayAddress { get; set; }
    public required string LoginGatewayChallenge { get; set; }

    public bool ShowMemberNagScreen { get; set; }
}