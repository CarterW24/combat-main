using Sanctuary.UdpLibrary.Configuration;

namespace Sanctuary.UdpLibrary.Statistics;

public struct UdpConnectionStatistics
{

    public long TotalBytesSent;
    public long TotalBytesReceived;

    public long TotalPacketsSent;

    public long TotalPacketsReceived;

    public long CrcRejectedPackets;

    public long OrderRejectedPackets;

    public long DuplicatePacketsReceived;

    public long ResentPacketsAccelerated;

    public long ResentPacketsTimedOut;

    public long ApplicationPacketsSent;
    public long ApplicationPacketsReceived;

    public long Iterations;

    public long CorruptPacketErrors;

    public long MasterPingAge;

    public uint MasterPingTime;
    public uint AveragePingTime;
    public uint LowPingTime;
    public uint HighPingTime;
    public uint LastPingTime;

    public long ReliableAveragePing;

    public long SyncOurSent;

    public long SyncOurReceived;

    public long SyncTheirSent;

    public long SyncTheirReceived;

    public float PercentSentSuccess;
    public float PercentReceivedSuccess;
}
