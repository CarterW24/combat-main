using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

using Collections.Pooled;

using Microsoft.Extensions.DependencyInjection;

using Sanctuary.Core.IO;
using Sanctuary.UdpLibrary.Abstractions;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.UdpLibrary.Internal;
using Sanctuary.UdpLibrary.Packets;
using Sanctuary.UdpLibrary.Statistics;

namespace Sanctuary.UdpLibrary;

public class UdpManager<TConnection> : IUdpManager, IDisposable where TConnection : UdpConnection
{
    private readonly Lock _clockGuard = new();
    private readonly Lock _eventListGuard = new();
    private readonly Lock _availableEventGuard = new();
    private readonly Lock _statsGuard = new();
    private readonly Lock _disconnectPendingGuard = new();
    private readonly Lock _giveTimeGuard = new();
    private readonly Lock _connectionGuard = new();
    private readonly Lock _handlerGuard = new();

    public UdpClockStamp CachedClock { get; private set; }

    private UdpClockStamp LastReceiveTime;
    private UdpClockStamp LastSendTime;

    public UdpParams Params { get; set; }

    public readonly IServiceProvider _serviceProvider;

    private readonly IUdpDriver _driver;

    protected UdpClockStamp LastEmptySocketBufferStamp;

    public UdpClockStamp ProcessingInducedLag { get; private set; }

    protected ErrorCondition ErrorCondition;

    protected readonly PooledList<TConnection> ConnectionList;
    protected readonly PooledList<UdpConnection> DisconnectPendingList;

    protected readonly ConcurrentDictionary<int, TConnection> AddressHashTable;
    protected readonly ConcurrentDictionary<int, TConnection> ConnectCodeHashTable;

    private readonly Internal.PriorityQueue<UdpConnection, UdpClockStamp>? PriorityQueue;

    private UdpClockStamp MinimumScheduledStamp;

    private int RandomSeed;

    private int EventListBytes;
    private LinkedList<CallbackEvent> EventList = new();
    private LinkedList<CallbackEvent> AvailableEventList = new();

    private byte[] _buffer;
    private SocketAddress _socketAddress;

    public bool EventQueuing
    {

        get
        {
            lock (_giveTimeGuard)
            {
                return Params.EventQueuing;
            }
        }
        set
        {
            lock (_giveTimeGuard)
            {
                Params.EventQueuing = value;
            }
        }
    }

    UdpManagerStatistics ManagerStats;
    UdpClockStamp ManagerStatsResetTime;

    public UdpManager(UdpParams udpParams, IServiceProvider serviceProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(udpParams.ClockSyncDelay);

        ArgumentOutOfRangeException.ThrowIfNegative(udpParams.CrcBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(udpParams.CrcBytes, 4);

        for (var i = 0; i < Constants.EncryptPasses; i++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative((byte)udpParams.EncryptMethod[i]);
            ArgumentOutOfRangeException.ThrowIfGreaterThan((byte)udpParams.EncryptMethod[i], 4);
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(udpParams.HashTableSize, 0);

        ArgumentOutOfRangeException.ThrowIfLessThan(udpParams.MaxRawPacketSize, 64);

        ArgumentOutOfRangeException.ThrowIfLessThan(udpParams.IncomingBufferSize, udpParams.MaxRawPacketSize);

        ArgumentOutOfRangeException.ThrowIfNegative(udpParams.KeepAliveDelay);

        ArgumentOutOfRangeException.ThrowIfNegative(udpParams.PortAliveDelay);

        ArgumentOutOfRangeException.ThrowIfLessThan(udpParams.MaxConnections, 1);

        ArgumentOutOfRangeException.ThrowIfLessThan(udpParams.OutgoingBufferSize, udpParams.MaxRawPacketSize);

        ArgumentOutOfRangeException.ThrowIfLessThan(udpParams.PacketHistoryMax, 1);

        ArgumentOutOfRangeException.ThrowIfNegative(udpParams.Port);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(udpParams.UserSuppliedEncryptExpansionBytes + udpParams.UserSuppliedEncryptExpansionBytes2, udpParams.MaxRawPacketSize);

        for (var i = 0; i < Constants.ReliableChannelCount; i++)
            ArgumentOutOfRangeException.ThrowIfLessThan(udpParams.Reliable[i].MaxOutstandingBytes, udpParams.MaxRawPacketSize);

        if (udpParams.Port == 0 && udpParams.PortRange != 0)
            throw new ArgumentOutOfRangeException("port range requires a valid port");

        Params = udpParams;

        _serviceProvider = serviceProvider;

        Params.MaxRawPacketSize = Math.Min(Params.MaxRawPacketSize, Constants.HardMaxRawPacketSize);

        if (Params.MaxDataHoldSize == -1)
            Params.MaxDataHoldSize = Params.MaxRawPacketSize;

        if (Params.PooledPacketSize == -1)
            Params.PooledPacketSize = Params.MaxRawPacketSize;

        Params.MaxDataHoldSize = Math.Min(Params.MaxDataHoldSize, Params.MaxRawPacketSize);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _driver = udpParams.UdpDriver ?? new UdpDriverWindows();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _driver = udpParams.UdpDriver ?? new UdpDriverLinux();
        }

        ArgumentNullException.ThrowIfNull(_driver);

        Clock();

        RandomSeed = (int)CachedClock;

        LastReceiveTime = 0;
        LastSendTime = 0;
        LastEmptySocketBufferStamp = 0;
        ProcessingInducedLag = 0;
        MinimumScheduledStamp = 0;
        EventListBytes = 0;

        if (!udpParams.AvoidPriorityQueue)
            PriorityQueue = new Internal.PriorityQueue<UdpConnection, UdpClockStamp>(Params.MaxConnections);

        ConnectionList = new PooledList<TConnection>();
        DisconnectPendingList = new PooledList<UdpConnection>();

        AddressHashTable = new ConcurrentDictionary<int, TConnection>(Environment.ProcessorCount, Params.HashTableSize);

        ConnectCodeHashTable = new ConcurrentDictionary<int, TConnection>(Environment.ProcessorCount, Math.Max(Params.HashTableSize / 5, 10));

        if (Params.PortRange == 0)
        {
            CreateAndBindSocket(Params.Port);
        }
        else
        {
            var r = Random() % Params.PortRange;

            for (var i = 0; i < Params.PortRange; i++)
            {
                CreateAndBindSocket(Params.Port + ((r + i) % Params.PortRange));

                if (ErrorCondition != ErrorCondition.CouldNotBindSocket)
                    break;
            }
        }

        _socketAddress = new SocketAddress(AddressFamily.InterNetwork);
        _buffer = GC.AllocateArray<byte>(Params.MaxRawPacketSize, true);
    }

    protected void CloseSocket()
    {
        _driver.SocketClose();
    }

    protected void CreateAndBindSocket(int usePort)
    {
        CloseSocket();

        ErrorCondition = ErrorCondition.None;

        if (!_driver.SocketOpen(usePort, Params.IncomingBufferSize, Params.OutgoingBufferSize, Params.BindIpAddress))
            ErrorCondition = ErrorCondition.CouldNotBindSocket;
    }

    private UdpClockStamp Clock()
    {
        lock (_clockGuard)
        {
            return CachedClock = _driver.Clock();
        }
    }

    private UdpClockStamp ClockElapsed(UdpClockStamp stamp)
    {
        return UdpMisc.ClockDiff(stamp, Clock());
    }

    public uint LocalSyncStampLong()
    {
        return (uint)Clock();
    }

    public ushort LocalSyncStampShort()
    {
        return (ushort)Clock();
    }

    public UdpClockStamp CachedClockElapsed(UdpClockStamp start)
    {
        return UdpMisc.ClockDiff(start, CachedClock);
    }

    public bool GiveTime(int maxPollingTime = 500, bool giveConnectionsTime = true)
    {
        lock (_giveTimeGuard)
        {
            Clock();

            if (Params.EventQueuing && EventList.Count > 0)
            {
                DeliverEvents(maxPollingTime);
                return true;
            }

            lock (_statsGuard)
            {
                ManagerStats.Iterations++;
            }

            var found = false;

            if (maxPollingTime != 0)
            {
                var start = CachedClock;

                while (true)
                {
                    var curStamp = CachedClock;

                    var res = _driver.SocketReceive(_buffer, _socketAddress);

                    if (res < 0)
                    {
                        LastEmptySocketBufferStamp = CachedClock;
                        break;
                    }

                    lock (_statsGuard)
                    {
                        LastReceiveTime = curStamp;
                        ManagerStats.BytesReceived += res;
                        ManagerStats.PacketsReceived++;
                    }

                    var data = _buffer.AsSpan(0, res);

                    ProcessingInducedLag = CachedClockElapsed(LastEmptySocketBufferStamp);

                    found = true;

                    ProcessRawPacket(_socketAddress, data);

                    if (ClockElapsed(start) >= maxPollingTime)
                    {
                        lock (_statsGuard)
                        {
                            ManagerStats.MaxPollingTimeExceeded++;
                        }

                        break;
                    }
                }
            }

            if (giveConnectionsTime)
            {
                if (PriorityQueue is not null)
                {
                    var curPriority = CachedClock;

                    lock (_connectionGuard)
                    {
                        MinimumScheduledStamp = curPriority + 1;
                    }

                    var processed = 0;

                    while (true)
                    {
                        UdpConnection? top;

                        lock (_connectionGuard)
                        {
                            top = PriorityQueue.TopRemove(curPriority);
                        }

                        if (top is null)
                            break;

                        top.GiveTime();

                        processed++;
                    }

                    lock (_statsGuard)
                    {
                        ManagerStats.PriorityQueueProcessed += processed;
                        ManagerStats.PriorityQueuePossible += ConnectionList.Count;
                    }
                }
                else
                {
                    foreach (var con in ConnectionList)
                    {
                        con.GiveTime();
                    }
                }

                ProcessDisconnectPending();
            }

            return found;
        }
    }

    private void DeliverEvents(int maxProcessingTime)
    {
        var start = Clock();

        while (true)
        {
            var ce = EventListPop();

            if (ce is null)
                break;

            switch (ce.EventType)
            {
                case CallbackEventType.RoutePacket:
                    {
                        if (ce.Payload is null)
                            break;

                        ce.Source?.OnRoutePacket(ce.Payload.GetDataPtr());
                    }
                    break;

                case CallbackEventType.ConnectComplete:
                    {
                        ce.Source?.OnConnectComplete();
                    }
                    break;

                case CallbackEventType.Terminated:
                    {
                        ce.Source?.OnTerminated();
                    }
                    break;

                case CallbackEventType.CrcReject:
                    {
                        if (ce.Payload is null)
                            break;

                        var data = ce.Payload.GetDataPtr();

                        ce.Source?.OnCrcReject(data.Slice(0, ce.Payload.GetDataLen()));
                    }
                    break;

                case CallbackEventType.PacketCorrupt:
                    {
                        if (ce.Payload is null)
                            break;

                        var data = ce.Payload.GetDataPtr();

                        ce.Source?.OnPacketCorrupt(data.Slice(0, ce.Payload.GetDataLen()), ce.Reason);
                    }
                    break;

                case CallbackEventType.ConnectRequest:
                    {
                        if (ce.Source is null)
                            break;

                        if (!OnConnectRequest(ce.Source))
                            ce.Source.InternalDisconnect(0, DisconnectReason.ConnectionRefused);

                    }
                    break;

                default:
                    break;
            }

            if (ClockElapsed(start) >= maxProcessingTime)
            {
                lock (_statsGuard)
                {
                    ManagerStats.MaxDeliveryTimeExceeded++;
                }

                break;
            }
        }
    }

    private void ProcessDisconnectPending()
    {
        lock (_disconnectPendingGuard)
        {
            DisconnectPendingList.RemoveAll(x => x.Status == Status.Disconnected);
        }
    }

    protected void ProcessRawPacket(SocketAddress socketAddress, Span<byte> data)
    {
        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte zeroByte))
            return;

        if (!reader.TryRead(out byte packetType))
            return;

        if (data.Length == 2 && zeroByte == 0 && packetType == (byte)UdpPacketType.PortAlive)
            return;

        if (data.Length == 2 && zeroByte == 0 && packetType == (byte)UdpPacketType.ServerStatus)
        {
            OnServerStatusRequest(socketAddress);
            return;
        }

        var con = AddressGetConnection(socketAddress);

        if (con == null)
        {
            if (data.Length == 0)
                return;

            if (data.Length >= 6 && zeroByte == 0 && packetType == (byte)UdpPacketType.Unknown)
                return;

            if (zeroByte == 0 && packetType == (byte)UdpPacketType.Connect)
            {
                if (ConnectionList.Count >= Params.MaxConnections)
                    return;

                reader.Advance(4);

                if (!reader.TryReadInt32(out int connectCode))
                    return;

                var newCon = ActivatorUtilities.CreateInstance<TConnection>(_serviceProvider, this, socketAddress, connectCode);

                if (newCon is not null)
                {
                    AddConnection(newCon);

                    newCon.GiveTime();
                    newCon.ProcessRawPacket(data);

                    CallbackConnectRequest(newCon);
                }
            }
            else
            {
                if (Params.AllowPortRemapping)
                {
                    if (zeroByte == 0 && packetType == (byte)UdpPacketType.RequestRemap)
                    {

                        if (!reader.TryReadInt32(out int connectCode))
                            return;

                        if (!reader.TryReadInt32(out int encryptCode))
                            return;

                        var curCon = ConnectCodeGetConnection(connectCode);

                        if (curCon is not null)
                        {
                            var tempEndPoint = new IPEndPoint(IPAddress.Any, 0);
                            var ipEndPoint = (IPEndPoint)tempEndPoint.Create(socketAddress);

                            if (Params.AllowAddressRemapping || curCon.EndPoint.Address == ipEndPoint.Address)
                            {

                                if (curCon.ConnectionConfig.EncryptCode == encryptCode)
                                {

                                    lock (_connectionGuard)
                                    {
                                        AddressHashTable.Remove(AddressHashValue(curCon.SocketAddress), out _);

                                        curCon.EndPoint = ipEndPoint;
                                        curCon.SocketAddress = socketAddress;

                                        AddressHashTable.TryAdd(AddressHashValue(curCon.SocketAddress), curCon);
                                    }

                                    return;
                                }
                            }
                        }
                    }
                }

                if (Params.ReplyUnreachableConnection)
                {
                    if (zeroByte != 0 || (zeroByte == 0 && packetType != (byte)UdpPacketType.UnreachableConnection && packetType != (byte)UdpPacketType.Terminate))
                    {
                        Span<byte> buf = [0, (byte)UdpPacketType.UnreachableConnection];
                        ActualSend(buf, buf.Length, socketAddress);
                    }
                }
            }

            return;
        }

        con.ProcessRawPacket(data);
    }

    private TConnection? ConnectCodeGetConnection(int connectCode)
    {
        lock (_connectionGuard)
        {
            if (ConnectCodeHashTable.TryGetValue(connectCode, out var con))
                return con;

            return null;
        }
    }

    private void CallbackConnectRequest(TConnection con)
    {
        if (Params.EventQueuing)
        {
            var ce = AvailableEventBorrow();
            ce.SetEventData(CallbackEventType.ConnectRequest, con);
            EventListAppend(ce);
        }
        else
        {
            if (!OnConnectRequest(con))
                con.InternalDisconnect(0, DisconnectReason.ConnectionRefused);
        }
    }

    protected TConnection? AddressGetConnection(SocketAddress socketAddress)
    {
        lock (_connectionGuard)
        {
            if (AddressHashTable.TryGetValue(AddressHashValue(socketAddress), out var udpConnection))
                return udpConnection;
        }

        return null;
    }

    protected int AddressHashValue(SocketAddress socketAddress)
    {
        return socketAddress.GetHashCode();
    }

    public void AddConnection(TConnection con)
    {
        lock (_connectionGuard)
        {
            ConnectionList.Add(con);
            AddressHashTable.TryAdd(AddressHashValue(con.SocketAddress), con);
            ConnectCodeHashTable.TryAdd(con.ConnectCode, con);
        }
    }

    public int Random()
    {
        return UdpMisc.Random(ref RandomSeed);
    }

    public void ActualSend(ReadOnlySpan<byte> data, int dataLen, SocketAddress socketAddress)
    {
        LastSendTime = CachedClock;

        ManagerStats.BytesSent += dataLen;
        ManagerStats.PacketsSent++;

        ActualSendHelper(data, dataLen, socketAddress);
    }

    private void ActualSendHelper(ReadOnlySpan<byte> data, int dataLen, SocketAddress socketAddress)
    {

        if (!_driver.SocketSend(data.Slice(0, dataLen), socketAddress))
        {
            lock (_statsGuard)
            {
                ManagerStats.SocketOverflowErrors++;
            }
        }
    }

    public void SetPriority(UdpConnection con, UdpClockStamp stamp)
    {
        lock (_connectionGuard)
        {
            if (stamp < MinimumScheduledStamp)
                stamp = MinimumScheduledStamp;

            PriorityQueue?.Add(con, stamp);
        }
    }

    public LogicalPacket CreatePacket(Span<byte> data, int dataLen, Span<byte> data2 = default, int dataLen2 = 0)
    {
        if (Params.PooledPacketMax > 0)
        {
            var totalLen = dataLen + dataLen2;

            if (totalLen <= Params.PooledPacketSize)
            {

                var lp = new PooledLogicalPacket(Params.PooledPacketSize);

                lp.SetData(data, dataLen, data2, dataLen2);

                return lp;
            }
        }

        return UdpMisc.CreateQuickLogicalPacket(data, dataLen, data2, dataLen2);
    }

    public void CallbackRoutePacket(UdpConnection con, Span<byte> data)
    {
        if (Params.EventQueuing)
        {
            var ce = AvailableEventBorrow();
            var packet = CreatePacket(data, data.Length);
            ce.SetEventData(CallbackEventType.RoutePacket, con, packet);
            EventListAppend(ce);
        }
        else
        {
            con.OnRoutePacket(data);
        }
    }

    public void CallbackCrcReject(UdpConnection con, Span<byte> data)
    {
        if (Params.EventQueuing)
        {
            var ce = AvailableEventBorrow();
            var packet = CreatePacket(data, data.Length);
            ce.SetEventData(CallbackEventType.CrcReject, con, packet);
            EventListAppend(ce);
        }
        else
        {
            con.OnCrcReject(data);
        }
    }

    public void CallbackPacketCorrupt(UdpConnection con, Span<byte> data, UdpCorruptionReason reason)
    {
        if (Params.EventQueuing)
        {
            var ce = AvailableEventBorrow();
            var packet = CreatePacket(data, data.Length);
            ce.SetEventData(CallbackEventType.PacketCorrupt, con, packet);
            ce.Reason = reason;
            EventListAppend(ce);
        }
        else
        {
            con.OnPacketCorrupt(data, reason);
        }
    }

    public void CallbackConnectComplete(UdpConnection con)
    {
        if (Params.EventQueuing)
        {
            var ce = AvailableEventBorrow();
            ce.SetEventData(CallbackEventType.ConnectComplete, con);
            EventListAppend(ce);
        }
        else
        {
            con.OnConnectComplete();
        }
    }

    public void SendPortAlive(SocketAddress socketAddress)
    {
        Span<byte> buf = [0, (byte)UdpPacketType.PortAlive];
        _driver.SocketSendPortAlive(buf, socketAddress);
    }

    public void KeepUntilDisconnected(UdpConnection udpConnection)
    {
        lock (_disconnectPendingGuard)
        {
            DisconnectPendingList.Add(udpConnection);
        }
    }

    public void RemoveConnection(UdpConnection con)
    {
        lock (_connectionGuard)
        {
            if (PriorityQueue is not null)
                PriorityQueue.Remove(con);

            ConnectCodeHashTable.Remove(con.ConnectCode, out _);

            if (!AddressHashTable.TryRemove(AddressHashValue(con.SocketAddress), out var conInstance))
                return;

            ConnectionList.Remove(conInstance);
        }
    }

    public void CallbackTerminated(UdpConnection con)
    {
        if (Params.EventQueuing)
        {
            var ce = AvailableEventBorrow();
            ce.SetEventData(CallbackEventType.Terminated, con);
            EventListAppend(ce);
        }
        else
        {
            con.OnTerminated();
        }
    }

    public virtual bool OnConnectRequest(UdpConnection udpConnection)
    {
        return false;
    }

    private CallbackEvent AvailableEventBorrow()
    {
        lock (_availableEventGuard)
        {
            var ce = AvailableEventList.First?.Value;

            if (ce is not null)
                AvailableEventList.RemoveFirst();
            else
                ce = new CallbackEvent();

            return ce;
        }
    }

    private void AvailableEventReturn(CallbackEvent ce)
    {
        lock (_availableEventGuard)
        {
            if (AvailableEventList.Count < Params.CallbackEventPoolMax)
            {
                AvailableEventList.AddFirst(ce);
            }
        }
    }

    private void EventListAppend(CallbackEvent ce)
    {
        lock (_eventListGuard)
        {
            EventList.AddLast(ce);

            if (ce.Payload is not null)
            {
                EventListBytes += ce.Payload.GetDataLen();
            }
        }
    }

    private CallbackEvent? EventListPop()
    {
        lock (_eventListGuard)
        {
            var ce = EventList.First?.Value;

            if (ce is not null && ce.Payload is not null)
            {
                EventList.RemoveFirst();
                EventListBytes -= ce.Payload.GetDataLen();
            }

            return ce;
        }
    }

    public TConnection? EstablishConnection(string serverAddress, int serverPort = 0, UdpClockStamp timeout = 0)
    {
        lock (_giveTimeGuard)
        {
            var portIndex = serverAddress.IndexOf(':');

            if (portIndex > 0)
            {
                int.TryParse(serverAddress.AsSpan(portIndex + 1), out serverPort);
                serverAddress = serverAddress.Substring(0, portIndex);
            }

            if (string.IsNullOrEmpty(serverAddress) || serverPort == 0)
                return null;

            if (ConnectionList.Count >= Params.MaxConnections)
                return null;

            if (!_driver.GetHostByName(out var destIp, serverAddress))
                return null;

            var endPoint = new IPEndPoint(destIp, serverPort);
            var socketAddress = endPoint.Serialize();

            var con = AddressGetConnection(socketAddress);

            if (con is not null)
                return null;

            con = ActivatorUtilities.CreateInstance<TConnection>(_serviceProvider, this, socketAddress, timeout);

            AddConnection(con);

            return con;
        }
    }

    public void GetStats(out UdpManagerStatistics stats)
    {
        lock (_statsGuard)
        {
            stats = ManagerStats;

            lock (_disconnectPendingGuard)
            {
                stats.DisconnectPendingCount = DisconnectPendingList.Count;
            }

            stats.ConnectionCount = ConnectionList.Count;
            stats.EventListCount = EventList.Count;
            stats.EventListBytes = EventListBytes;
            stats.ElapsedTime = CachedClockElapsed(ManagerStatsResetTime);
        }
    }

    public void ResetStats()
    {
        lock (_statsGuard)
        {
            ManagerStatsResetTime = CachedClock;
            ManagerStats.Reset();
        }
    }

    public void IncrementCrcRejectedPackets()
    {
        lock (_statsGuard)
        {
            ManagerStats.CrcRejectedPackets++;
        }
    }
    public void IncrementOrderRejectedPackets()
    {
        lock (_statsGuard)
        {
            ManagerStats.OrderRejectedPackets++;
        }
    }

    public void IncrementDuplicatePacketsReceived()
    {
        lock (_statsGuard)
        {
            ManagerStats.DuplicatePacketsReceived++;
        }
    }

    public void IncrementResentPacketsAccelerated()
    {
        lock (_statsGuard)
        {
            ManagerStats.ResentPacketsAccelerated++;
        }
    }

    public void IncrementResentPacketsTimedOut()
    {
        lock (_statsGuard)
        {
            ManagerStats.ResentPacketsTimedOut++;
        }
    }

    public void IncrementApplicationPacketsSent()
    {
        lock (_statsGuard)
        {
            ManagerStats.ApplicationPacketsSent++;
        }
    }

    public void IncrementApplicationPacketsReceived()
    {
        lock (_statsGuard)
        {
            ManagerStats.ApplicationPacketsReceived++;
        }
    }

    public void IncrementCorruptPacketErrors()
    {
        lock (_statsGuard)
        {
            ManagerStats.CorruptPacketErrors++;
        }
    }

    public virtual void OnServerStatusRequest(SocketAddress socketAddress)
    {
    }

    public virtual void Dispose()
    {
        lock (_connectionGuard)
        {
            var connectionList = ConnectionList.ToFrozenSet();

            foreach (var connection in connectionList)
            {
                if (connection is null)
                    continue;

                connection.InternalDisconnect(0, DisconnectReason.ManagerDeleted);
            }
        }

        lock (_disconnectPendingGuard)
        {
            DisconnectPendingList.Clear();
        }

        if (Params.LingerDelay != 0)
        {
            _driver.Sleep(Params.LingerDelay);
        }

        CloseSocket();
    }
}
