namespace Sanctuary.UdpLibrary.Statistics;

public struct UdpManagerStatistics
{
    public long BytesSent;
    public long PacketsSent;
    public long BytesReceived;
    public long PacketsReceived;
    public long ConnectionRequests;
    public long CrcRejectedPackets;
    public long OrderRejectedPackets;
    public long DuplicatePacketsReceived;

    public long ResentPacketsAccelerated;

    public long ResentPacketsTimedOut;

    public long PriorityQueueProcessed;

    public long PriorityQueuePossible;
    public long ApplicationPacketsSent;
    public long ApplicationPacketsReceived;

    public long Iterations;

    public long CorruptPacketErrors;

    public long SocketOverflowErrors;

    public long MaxPollingTimeExceeded;

    public long MaxDeliveryTimeExceeded;

    public int ConnectionCount;

    public int DisconnectPendingCount;

    public int EventListCount;

    public int EventListBytes;

    public int PoolCreated;

    public int PoolAvailable;

    public UdpClockStamp ElapsedTime;

    public void Reset()
    {
        BytesSent = 0;
        PacketsSent = 0;
        BytesReceived = 0;
        PacketsReceived = 0;
        ConnectionRequests = 0;
        CrcRejectedPackets = 0;
        OrderRejectedPackets = 0;
        DuplicatePacketsReceived = 0;
        ResentPacketsAccelerated = 0;
        ResentPacketsTimedOut = 0;
        PriorityQueueProcessed = 0;
        PriorityQueuePossible = 0;
        ApplicationPacketsSent = 0;
        ApplicationPacketsReceived = 0;
        Iterations = 0;
        CorruptPacketErrors = 0;
        SocketOverflowErrors = 0;
        MaxPollingTimeExceeded = 0;
        MaxDeliveryTimeExceeded = 0;
        ConnectionCount = 0;
        DisconnectPendingCount = 0;
        EventListCount = 0;
        EventListBytes = 0;
        PoolCreated = 0;
        PoolAvailable = 0;
        ElapsedTime = 0;
    }
}
