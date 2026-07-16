namespace Sanctuary.UdpLibrary.Enumerations;

public enum ManagerRole
{
    // Original default UdpLibrary settings.
    Default,

    // Server process that is servicing multiple internal/local/high-bandwidth connections.
    InternalServer,

    // Client process that is connecting to internal servers (ie. local high-bandwidth connections).
    InternalClient,

    // Server process that is servicing multiple external/relative-low-bandwidth connections.
    ExternalServer,

    // Client process that is connection to an external server (ie. a typical end-user client setup, relatively low bandwidth).
    ExternalClient,

    // Highly specialized role for talking on a long-fat-network (super-high bandwidth, high-latency, slight packetloss, semi-dedicated pipe).
    Lfn
}