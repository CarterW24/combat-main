namespace Sanctuary.UdpLibrary.Configuration;

public struct UdpReliableConfig
{
    public int MaxOutstandingBytes = 200000;

    public int MaxOutstandingPackets = 400;

    public int MaxInstandingPackets = 400;

    public int FragmentSize = 0;

    public int TrickleSize;

    public int TrickleRate = 0;

    public int ResendDelayAdjust = 300;

    public int ResendDelayPercent = 125;

    public int ResendDelayCap = 5000;

    public int CongestionWindowMinimum = 0;

    public int CongestionWindowMaximum;

    public int ToleranceLossCount = 0;

    public bool OutOfOrder = false;

    public bool Coalesce = true;

    public bool AckDeduping = true;

    public UdpReliableConfig()
    {
    }
}
