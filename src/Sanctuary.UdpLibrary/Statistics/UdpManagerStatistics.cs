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

    // number of times we have resent a packet due to receiving a later packet in the series
    public long ResentPacketsAccelerated;

    // number of times we have resent a packet due to the ack-timeout expiring
    public long ResentPacketsTimedOut;

    // cumulative number of times a priority-queue entry has received processing time
    public long PriorityQueueProcessed;

    // cumulative number of priority-queue entries that could have received processing time
    public long PriorityQueuePossible;
    public long ApplicationPacketsSent;
    public long ApplicationPacketsReceived;

    // number of times GiveTime has been called
    public long Iterations;

    // number of mis formed/corrupt packets
    public long CorruptPacketErrors;

    // number of times the socket buffer was full when a send was attempted.
    public long SocketOverflowErrors;

    // number of times GiveTime has aborted due to time, before exhausting all data in the socket buffer
    public long MaxPollingTimeExceeded;

    // number of times DeliverEvents has aborted due to time, before exhausting all data in the event queue
    public long MaxDeliveryTimeExceeded;

    // number of connections currently being managed
    public int ConnectionCount;

    // number of connections that are pending disconnection
    public int DisconnectPendingCount;

    // number of events that are in the call back event queue
    public int EventListCount;

    // number of bytes of packet data that are in the call back event queue
    public int EventListBytes;

    // number of packets created in the pool
    public int PoolCreated;

    // number of packets available in the pool
    public int PoolAvailable;

    // how long these statistics have been gathered (in milliseconds), useful for figuring out averages
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