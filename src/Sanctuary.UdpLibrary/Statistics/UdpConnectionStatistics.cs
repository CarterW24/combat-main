using Sanctuary.UdpLibrary.Configuration;

namespace Sanctuary.UdpLibrary.Statistics;

public struct UdpConnectionStatistics
{
    // These statistics are valid even if clock-sync is not used.
    // These statistics are never reset and should not be as the negotiated
    // packetloss stats would get messed up if they were
    // as such, use ConnectionAge to determine how long they have been accumulating.

    public long TotalBytesSent;
    public long TotalBytesReceived;

    // Total packets we have sent.
    public long TotalPacketsSent;

    // Total packets we have received.
    public long TotalPacketsReceived;

    // Total packets on our connection that have been rejected due to a crc error.
    public long CrcRejectedPackets;

    // Total packets on our connection that have been rejected due to an order error (only applicable for ordered channel).
    public long OrderRejectedPackets;

    // Total reliable packets that we received where we had already received it before and threw it away.
    public long DuplicatePacketsReceived;

    // Number of times we have resent a packet due to receiving a later packet in the series.
    public long ResentPacketsAccelerated;

    // Number of times we have resent a packet due to the ack-timeout expiring.
    public long ResentPacketsTimedOut;

    public long ApplicationPacketsSent;
    public long ApplicationPacketsReceived;

    // Number of times this connection has been given processing time.
    public long Iterations;

    // Number of misformed/corrupt packets.
    public long CorruptPacketErrors;

    // These statistics are only valid if clock-sync'ing is enabled (highly recommended) (will be valid on both client and server side).
    // These statistics are reset by PingStatReset and are negotiated periodically by the clock-sync stuff ClockSyncDelay.

    // Only valid (and applicable) on client side.
    public long MasterPingAge;

    public uint MasterPingTime;
    public uint AveragePingTime;
    public uint LowPingTime;
    public uint HighPingTime;
    public uint LastPingTime;

    // The average time (over last 3 acks) for a reliable packet to get acked (when packet is not lost).
    public long ReliableAveragePing;

    // Total packets we have sent at time they reported their numbers.
    public long SyncOurSent;

    // Total packets we have received at time they reported their numbers.
    public long SyncOurReceived;

    // Total packets they have sent.
    public long SyncTheirSent;

    // Total packets they have received.
    public long SyncTheirReceived;

    public float PercentSentSuccess;
    public float PercentReceivedSuccess;
}