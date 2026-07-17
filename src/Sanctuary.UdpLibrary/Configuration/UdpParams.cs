using Sanctuary.UdpLibrary.Abstractions;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.UdpLibrary.Internal;

namespace Sanctuary.UdpLibrary.Configuration;

public class UdpParams
{
    public int MaxConnections;

    public int Port;

    public int PortRange;

    public int OutgoingBufferSize;

    public int IncomingBufferSize;

    public int PacketHistoryMax;

    public int KeepAliveDelay;

    public int PortAliveDelay;

    public bool ReplyUnreachableConnection;

    public bool AllowPortRemapping;

    public bool AllowAddressRemapping;

    public int IcmpErrorRetryPeriod;

    public int NoDataTimeout;

    public int OldestUnacknowledgedTimeout;

    public int ReliableOverflowBytes;

    public int MaxDataHoldTime;

    public int MaxDataHoldSize;

    public int MaxRawPacketSize;

    public int HashTableSize;

    public bool AvoidPriorityQueue;

    public int ClockSyncDelay;

    public int LingerDelay;

    public int PooledPacketMax;

    public int PooledPacketInitial;

    public int PooledPacketSize;

    public int CallbackEventPoolMax;

    public bool ProcessIcmpErrors;

    public bool ProcessIcmpErrorsDuringNegotiating;

    public int ConnectAttemptDelay;

    public int ThreadSleepTime;

    public string BindIpAddress;

    public UdpReliableConfig[] Reliable = new UdpReliableConfig[Constants.ReliableChannelCount];

    public int UserSuppliedEncryptExpansionBytes;
    public int UserSuppliedEncryptExpansionBytes2;

    public IUdpDriver? UdpDriver;

    public bool EventQueuing;

    public int IncomingLogicalPacketMax;

    public string ProtocolName;

    public byte CrcBytes;

    public EncryptMethod[] EncryptMethod = new EncryptMethod[Constants.EncryptPasses];

    public UdpParams(ManagerRole role = ManagerRole.Default)
    {
        OutgoingBufferSize = 64 * 1024;
        IncomingBufferSize = 64 * 1024;
        PacketHistoryMax = 4;
        MaxDataHoldTime = 50;
        MaxDataHoldSize = -1;
        MaxRawPacketSize = 512;
        HashTableSize = 100;
        AvoidPriorityQueue = false;
        ClockSyncDelay = 0;
        CrcBytes = 0;
        EncryptMethod[0] = Enumerations.EncryptMethod.None;
        EncryptMethod[1] = Enumerations.EncryptMethod.None;
        KeepAliveDelay = 0;
        PortAliveDelay = 0;
        NoDataTimeout = 0;
        MaxConnections = 10;
        Port = 0;
        PortRange = 0;
        PooledPacketMax = 1000;
        PooledPacketSize = -1;
        PooledPacketInitial = 0;
        ReplyUnreachableConnection = true;
        AllowPortRemapping = true;
        AllowAddressRemapping = false;
        IcmpErrorRetryPeriod = 5000;
        OldestUnacknowledgedTimeout = 120000;
        ProcessIcmpErrors = true;
        ProcessIcmpErrorsDuringNegotiating = false;
        ConnectAttemptDelay = 1000;
        ReliableOverflowBytes = 0;
        LingerDelay = 10;
        BindIpAddress = string.Empty;
        UdpDriver = null;
        CallbackEventPoolMax = 5000;
        EventQueuing = false;
        ThreadSleepTime = 20;
        IncomingLogicalPacketMax = 20 * 1024 * 1024;
        ProtocolName = string.Empty;
        UserSuppliedEncryptExpansionBytes = 0;
        UserSuppliedEncryptExpansionBytes2 = 0;

        Reliable[0].MaxInstandingPackets = 400;
        Reliable[0].MaxOutstandingBytes = 200 * 1024;
        Reliable[0].MaxOutstandingPackets = 400;
        Reliable[0].OutOfOrder = false;
        Reliable[0].Coalesce = true;
        Reliable[0].AckDeduping = true;
        Reliable[0].FragmentSize = 0;
        Reliable[0].ResendDelayAdjust = 300;
        Reliable[0].ResendDelayPercent = 125;
        Reliable[0].ResendDelayCap = 8000;
        Reliable[0].ToleranceLossCount = 0;
        Reliable[0].CongestionWindowMinimum = 0;
        Reliable[0].CongestionWindowMaximum = 8 * 1024;
        Reliable[0].TrickleRate = 0;
        Reliable[0].TrickleSize = 0;

        switch (role)
        {
            case ManagerRole.InternalServer:
                OutgoingBufferSize = 4 * 1024 * 1024;
                IncomingBufferSize = 4 * 1024 * 1024;
                CrcBytes = 2;
                IcmpErrorRetryPeriod = 500;
                MaxRawPacketSize = 1460;
                HashTableSize = 10000;
                KeepAliveDelay = 30000;
                NoDataTimeout = 90000;
                MaxConnections = 2000;
                PooledPacketMax = 20000;
                PooledPacketInitial = 1000;
                AllowPortRemapping = false;
                Reliable[0].MaxInstandingPackets = 1000;
                Reliable[0].MaxOutstandingBytes = 1024 * 1024;
                Reliable[0].MaxOutstandingPackets = 1000;
                Reliable[0].CongestionWindowMinimum = 4 * 1024;
                Reliable[0].CongestionWindowMaximum = 16 * 1024;
                Reliable[0].ResendDelayAdjust = 150;
                break;

            case ManagerRole.InternalClient:
                OutgoingBufferSize = 1024 * 1024;
                IncomingBufferSize = 1024 * 1024;
                CrcBytes = 2;
                IcmpErrorRetryPeriod = 500;
                MaxRawPacketSize = 1460;
                HashTableSize = 10;
                KeepAliveDelay = 30000;
                NoDataTimeout = 90000;
                MaxConnections = 2;
                PooledPacketMax = 2000;
                PooledPacketInitial = 100;
                AllowPortRemapping = false;
                Reliable[0].MaxInstandingPackets = 1000;
                Reliable[0].MaxOutstandingBytes = 1024 * 1024;
                Reliable[0].MaxOutstandingPackets = 1000;
                Reliable[0].CongestionWindowMinimum = 4 * 1024;
                Reliable[0].CongestionWindowMaximum = 16 * 1024;
                Reliable[0].ResendDelayAdjust = 150;
                break;

            case ManagerRole.ExternalServer:
                OutgoingBufferSize = 2 * 1024 * 1024;
                IncomingBufferSize = 2 * 1024 * 1024;
                CrcBytes = 2;
                IcmpErrorRetryPeriod = 2500;
                HashTableSize = 10000;
                KeepAliveDelay = 30000;
                NoDataTimeout = 90000;
                MaxConnections = 2000;
                PooledPacketMax = 20000;
                PooledPacketInitial = 1000;
                break;

            case ManagerRole.ExternalClient:
                CrcBytes = 2;
                IcmpErrorRetryPeriod = 2500;
                HashTableSize = 10;
                KeepAliveDelay = 30000;
                NoDataTimeout = 90000;
                MaxConnections = 2;
                PooledPacketMax = 2000;
                PooledPacketInitial = 10;
                break;

            case ManagerRole.Lfn:
                OutgoingBufferSize = 16 * 1024 * 1024;
                IncomingBufferSize = 16 * 1024 * 1024;
                CrcBytes = 2;
                IcmpErrorRetryPeriod = 1500;
                MaxRawPacketSize = 1460;
                KeepAliveDelay = 30000;
                NoDataTimeout = 90000;
                MaxConnections = 2;
                PooledPacketMax = 50000;
                PooledPacketInitial = 5000;
                AllowPortRemapping = false;
                IncomingLogicalPacketMax = 200 * 1024 * 1024;
                CallbackEventPoolMax = 50000;
                Reliable[0].MaxInstandingPackets = 32000;
                Reliable[0].MaxOutstandingBytes = 50 * 1024 * 1024;
                Reliable[0].MaxOutstandingPackets = 32000;
                Reliable[0].CongestionWindowMinimum = 300000;
                Reliable[0].CongestionWindowMaximum = 300000;
                Reliable[0].ToleranceLossCount = 100;
                break;

            case ManagerRole.Default:
            default:
                break;
        }

        for (var i = 1; i < Constants.ReliableChannelCount; i++)
            Reliable[i] = Reliable[0];
    }
}
