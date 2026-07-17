namespace Sanctuary.Core.Configuration;

public sealed class GatewayServerOptions : ServerOptions
{
    public required string Environment { get; set; }

    public required string ClientVersion { get; set; }

    public required string ServerAddress { get; set; }

    public required string LoginGatewayAddress { get; set; }
    public required string LoginGatewayChallenge { get; set; }

    public bool ShowMemberNagScreen { get; set; }
}
