namespace Sanctuary.UdpLibrary.Internal;

internal static class Constants
{
    internal const int EncryptPasses = 2;
    internal const int ReliableChannelCount = 4;

    internal const int ProtocolVersion = 3;

    internal const int HardMaxRawPacketSize = 0x2000;

    internal const int HardMaxOutstandingPackets = 30000;

    internal const int UdpPacketOrderedSize = 4;
    internal const int UdpPacketReliableSize = 4;

    internal const int MaximumTimeAllowedForPortRemapping = 5000;
}
